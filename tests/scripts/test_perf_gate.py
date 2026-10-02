import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

ROOT = Path(__file__).resolve().parents[2]
# Real release history from Issue #397: both Editors, both focus conditions, host anonymised.
HISTORY = ROOT / "tests/fixtures/perf/editor-ops-history-issue-397.jsonl"
LOCAL_OR_POLLED = {"read_csharp", "play_until_ready", "stop_until_ready"}


class PerfGateTests(unittest.TestCase):
    def setUp(self):
        path = ROOT / "scripts/perf_gate.py"
        self.assertTrue(path.exists(), "shared performance gate is missing")
        spec = importlib.util.spec_from_file_location("perf_gate", path)
        self.gate = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.gate)

    def test_nearest_rank_and_empty_samples(self):
        summary = self.gate.summarize(list(range(1, 101)))
        self.assertEqual((summary["n"], summary["p50_ms"], summary["p95_ms"]), (100, 50, 95))
        with self.assertRaises(ValueError):
            self.gate.summarize([])

    def test_absolute_boundary_and_missing_budget_fail_closed(self):
        measured = {"ping": {"n": 50, "p50_ms": 10, "p95_ms": 20}}
        self.assertEqual(self.gate.check(measured, {"ping": {"p50_ms": 10, "p95_ms": 20}}), [])
        self.assertTrue(self.gate.check(measured, {"ping": {"p50_ms": 9, "p95_ms": 20}}))
        self.assertTrue(self.gate.check(measured, {}))
        self.assertTrue(self.gate.check(measured, {"ping": {"p50_ms": 10}}))

    def test_history_uses_latest_five_matching_runs_and_strict_twenty_percent(self):
        current = {"ping": {"n": 30, "p50_ms": 12, "p95_ms": 20}}
        condition = {"version": "6000", "focus": "frontmost"}
        rows = [{"conditions": condition, "status": "PASS", "results": {"ping": {"p50_ms": n}}}
                for n in [100, 9, 10, 11, 10, 10]]
        rows += [{"conditions": {"version": "2022"}, "status": "PASS", "results": {"ping": {"p50_ms": 1}}},
                 {"conditions": condition, "status": "FAIL", "measurements_complete": False,
                  "results": {"ping": {"p50_ms": 1}}}]
        self.assertEqual(self.gate.regressions(current, condition, rows, 20), [])
        current["ping"]["p50_ms"] = 12.01
        self.assertTrue(self.gate.regressions(current, condition, rows, 20))
        self.assertEqual(self.gate.regressions(current, condition, rows, 30), [])
        self.assertEqual(self.gate.regressions(current, condition, rows[:4], 20), [])

    def test_completed_budget_failures_still_count_in_last_five_measurements(self):
        conditions = {"focus": "background"}
        rows = [{"conditions": conditions, "status": status, "results": {"ping": {"p50_ms": value}}}
                for status, value in [("PASS", 10)] * 5 + [("FAIL", 20)] * 5]
        current = {"ping": {"p50_ms": 24, "p95_ms": 30}}
        self.assertEqual(self.gate.regressions(current, conditions, rows, 20), [])
        self.assertTrue(self.gate.check(current, {"ping": {"p50_ms": 10, "p95_ms": 20}}))

    def declarations(self):
        return json.loads((ROOT / "perf-budgets.json").read_text())["two_phase_p50"]

    def replay(self, history, unity, focus, declarations):
        """Judge every complete run of one condition against only the rows recorded before it."""
        for index, row in enumerate(history):
            conditions = row["conditions"]
            if row["measurements_complete"] and (conditions["unity"], conditions["focus"]) == (unity, focus):
                yield row, self.gate.regressions(row["results"], conditions, history[:index], 20.0,
                                                 self.gate.two_phase_operations(declarations, conditions))

    def test_two_phase_history_accepts_either_phase(self):
        history = self.gate.read_history(HISTORY)
        runs = [row for row in history if row["measurements_complete"]
                and (row["conditions"]["unity"], row["conditions"]["focus"]) == ("6000.3.25f1", "background")]
        conditions = runs[-1]["conditions"]
        self.assertEqual([round(row["results"]["hierarchy"]["p50_ms"]) for row in runs],
                         [151, 151, 70, 155, 155, 148, 140, 74, 75, 76, 76, 162, 165, 167])
        for value in (70.0, 76.5, 140.0, 167.0):
            current = {"hierarchy": {"p50_ms": value}}
            self.assertEqual(self.gate.regressions(current, conditions, history, 20, {"hierarchy"}), [], value)

        judged = list(self.replay(history, "6000.3.25f1", "background", self.declarations()))
        self.assertEqual(len(judged), 14)
        for row, violations in judged:
            self.assertFalse([v for v in violations if v.startswith("hierarchy:")], row["started_at"])
        final, violations = judged[-1]
        self.assertEqual(violations, [])
        # The undeclared rule compares the slow phase with a fast-phase median: the Issue #461 defect.
        recorded = ["hierarchy: p50 166.524 ms > last-five median 76.087 ms + 20.0%"]
        self.assertEqual(final["violations"], recorded)
        self.assertEqual(self.gate.regressions(final["results"], conditions, history[:history.index(final)], 20.0),
                         recorded)

    def test_two_phase_uniform_thirty_percent_slowdown_fails_in_either_phase(self):
        history = self.gate.read_history(HISTORY)
        conditions = next(row["conditions"] for row in history
                          if (row["conditions"]["unity"], row["conditions"]["focus"]) == ("6000.3.25f1", "background"))
        for phase, median in (("low", 75.358), ("high", 162.287)):
            current = {"hierarchy": {"p50_ms": median * 1.3}}
            violations = self.gate.regressions(current, conditions, history, 20, {"hierarchy"})
            self.assertEqual(len(violations), 1, phase)
            self.assertIn(f"last-five {phase}-phase median {median:.3f} ms + 20%", violations[0])

        condition = {"unity": "6000", "focus": "background"}
        fast, slow = [74, 76, 75, 77, 73], [150, 149, 152, 148, 151]
        rows = [{"conditions": condition, "results": {"ping": {"p50_ms": value}}}
                for pair in zip(fast, slow) for value in pair]
        for value, failed in ((75 * 1.2, False), (75 * 1.3, True), (150 * 1.2, False), (150 * 1.3, True)):
            violations = self.gate.regressions({"ping": {"p50_ms": value}}, condition, rows, 20, {"ping"})
            self.assertEqual(bool(violations), failed, value)

    def test_two_phase_baseline_uses_latest_five_runs_of_the_same_phase(self):
        condition = {"focus": "background"}
        rows = [{"conditions": condition, "results": {"ping": {"p50_ms": value}}}
                for value in [200, 100, 100, 100, 100, 100, 50, 50, 50, 50, 50, 50]]
        current = {"ping": {"p50_ms": 120.01}}
        self.assertTrue(self.gate.regressions(current, condition, rows, 20, {"ping"}))
        current["ping"]["p50_ms"] = 120
        self.assertEqual(self.gate.regressions(current, condition, rows, 20, {"ping"}), [])
        # A declared operation still needs five complete matching runs before any relative baseline exists.
        current["ping"]["p50_ms"] = 500
        self.assertEqual(self.gate.regressions(current, condition, rows[:4], 20, {"ping"}), [])

    def test_two_phase_baseline_needs_five_runs_of_that_phase(self):
        condition = {"focus": "background"}
        rows = [{"conditions": condition, "results": {"ping": {"p50_ms": value}}}
                for value in [75, 150, 75, 150, 75, 150, 75, 150, 75, 75]]
        slow, fast = {"ping": {"p50_ms": 300.0}}, {"ping": {"p50_ms": 97.5}}
        self.assertEqual(self.gate.regressions(slow, condition, rows, 20, {"ping"}), [])
        self.assertTrue(self.gate.regressions(fast, condition, rows, 20, {"ping"}))
        rows.append({"conditions": condition, "results": {"ping": {"p50_ms": 150}}})
        self.assertTrue(self.gate.regressions(slow, condition, rows, 20, {"ping"}))

        # Issue #397: one earlier slow-phase run (134.5 ms) is no baseline for editor_state at 162.3 ms,
        # and two fast-phase runs are none for the asset_delete at 88.0 ms measured after this snapshot.
        history = self.gate.read_history(HISTORY)
        for row, violations in self.replay(history, "6000.3.25f1", "background", self.declarations()):
            self.assertFalse([v for v in violations if v.startswith("editor_state:")], row["started_at"])
        conditions = row["conditions"]
        self.assertEqual(self.gate.regressions({"asset_delete": {"p50_ms": 88.022}}, conditions, history, 20,
                                               {"asset_delete"}), [])

    def test_single_phase_judgments_are_unchanged(self):
        history = self.gate.read_history(HISTORY)
        recorded_relative = 0
        for unity, focus in (("2022.3.62f3", "background"), ("2022.3.62f3", "frontmost"),
                             ("6000.3.25f1", "frontmost")):
            for row, violations in self.replay(history, unity, focus, self.declarations()):
                recorded = [v for v in row["violations"] if "last-five median" in v]
                self.assertEqual(violations, recorded, (unity, focus, row["started_at"]))
                recorded_relative += len(recorded)
        self.assertEqual(recorded_relative, 23)

        # Declared operations whose history has no gap wider than the threshold keep the plain rule,
        # and a first-ever slow value is not excused as a new phase.
        condition = {"focus": "background"}
        rows = [{"conditions": condition, "results": {"ping": {"p50_ms": value}}}
                for value in [10, 9, 10, 11, 10, 12]]
        for value in (12.0, 12.01, 24.0):
            current = {"ping": {"p50_ms": value}}
            self.assertEqual(self.gate.regressions(current, condition, rows, 20, {"ping"}),
                             self.gate.regressions(current, condition, rows, 20), value)
        self.assertTrue(self.gate.regressions({"ping": {"p50_ms": 24.0}}, condition, rows, 20, {"ping"}))
        # A 0 ms p50 has no relative gap to split on.
        rows[0]["results"]["ping"]["p50_ms"] = 0
        self.assertTrue(self.gate.regressions({"ping": {"p50_ms": 24.0}}, condition, rows, 20, {"ping"}))

    def test_two_phase_declaration_is_scoped_to_unity6_background_remote_operations(self):
        spec = importlib.util.spec_from_file_location("bench_editor_ops", ROOT / "scripts/bench-editor-ops.py")
        bench = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(bench)
        remote = {name for name, _, _ in bench.operations()} - LOCAL_OR_POLLED
        declarations = self.declarations()
        conditions = {"host": "any", "unity": "6000.3.25f1", "focus": "background", "suite_version": 3}
        self.assertEqual(self.gate.two_phase_operations(declarations, conditions), remote)
        for other in ({"unity": "6000.3.25f1", "focus": "frontmost"}, {"unity": "2022.3.62f3", "focus": "background"},
                      {"unity": "6000.3.26f1", "focus": "background"}, {"focus": "background"}):
            self.assertEqual(self.gate.two_phase_operations(declarations, other), set(), other)
        self.assertEqual(self.gate.two_phase_operations([], conditions), set())
        for invalid in ({"conditions": {}, "operations": ["ping"]}, {"conditions": {"focus": "background"}},
                        {"conditions": {"focus": "background"}, "operations": []}, {"operations": ["ping"]}):
            with self.assertRaises(ValueError):
                self.gate.two_phase_operations([invalid], conditions)

    def test_history_append_preserves_previous_lines(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "nested/history.jsonl"
            self.gate.append_history(path, {"run": 1})
            before = path.read_bytes()
            self.gate.append_history(path, {"run": 2})
            self.assertTrue(path.read_bytes().startswith(before))
            self.assertEqual(self.gate.read_history(path), [{"run": 1}, {"run": 2}])
            path.write_text("broken\n")
            with self.assertRaises(ValueError):
                self.gate.read_history(path)

    def test_eval_history_conditions_exclude_run_specific_metadata(self):
        spec = importlib.util.spec_from_file_location("bench_eval", ROOT / "scripts/bench-eval.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertTrue(hasattr(module, "history_conditions"), "eval needs comparable history conditions")
        conditions = {"os": "Darwin", "arch": "arm64", "unity_editor_version": "6000.3.25f1",
                      "sample": "spawn-to-exit", "command": "unity-cli editor eval 1+2",
                      "editor_frontmost": "required", "port": 1234, "unity_cli_version": "old"}
        baseline = module.history_conditions(conditions)
        conditions.update(port=2345, unity_cli_version="new")
        self.assertEqual(module.history_conditions(conditions), baseline)
        conditions["editor_frontmost"] = "not checked"
        self.assertNotEqual(module.history_conditions(conditions), baseline)


if __name__ == "__main__":
    unittest.main()
