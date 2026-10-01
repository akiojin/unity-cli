import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

ROOT = Path(__file__).resolve().parents[2]


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
