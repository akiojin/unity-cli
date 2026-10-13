"""Protect the reliability measurement from false PASS and hidden regressions."""
import importlib.util
from pathlib import Path
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("reliability", ROOT / "scripts/lsp-write-reliability.py")
reliability = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(reliability)


class ReliabilityTests(unittest.TestCase):
    def test_scenarios_cover_every_write_tool_and_large_unicode_inputs(self):
        cases = reliability.scenarios()
        self.assertEqual({case["tool"] for case in cases}, {
            "create_csharp_file", "write_csharp_file", "apply_csharp_edits",
            "replace_symbol_body", "insert_before_symbol", "insert_after_symbol",
            "remove_symbol", "rename_symbol", "validate_text_edits", "create_class",
        })
        self.assertGreater(max(data.count(b"\n") for case in cases for data in case["before"].values()), 3000)
        self.assertTrue(any(data.startswith(b"\xef\xbb\xbf") and b"\r\n" in data
                            for case in cases for data in case["before"].values()))

    def test_byte_comparison_distinguishes_bom_newlines_text_and_extra_files(self):
        expected = {"Assets/A.cs": "// 日本語\r\nclass A {}\r\n".encode()}
        actual = {"Assets/A.cs": b"\xef\xbb\xbf" + expected["Assets/A.cs"].replace(b"\r\n", b"\n"),
                  "Assets/Unexpected.cs": b"class Extra {}"}
        signatures, differences = reliability.compare_files(expected, actual)
        self.assertEqual(set(signatures), {"bom_added", "newlines_changed", "file_set_changed"})
        self.assertEqual(len(differences), 2)
        self.assertIn("--- direct/", differences[0]["diff"])
        self.assertEqual(reliability.compare_files(expected, expected), ([], []))
        self.assertIn("text_changed", reliability.compare_files(expected, {"Assets/A.cs": b"class Wrong {}"})[0])

    def test_server_alias_tolerates_case_insensitive_filesystems(self):
        import os, tempfile
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "Server"
            source.write_bytes(b"binary")
            alias = Path(temp) / "server"
            if not alias.exists():
                # Case-sensitive filesystems: a hard link stands in for macOS / Windows aliasing.
                os.link(source, alias)
            reliability.install_server_alias(source, alias)
            self.assertEqual(alias.read_bytes(), b"binary")
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "Server"
            source.write_bytes(b"binary")
            target = Path(temp) / "sub" / "server"
            target.parent.mkdir()
            reliability.install_server_alias(source, target)
            self.assertEqual(target.read_bytes(), b"binary")

    def test_false_success_or_fallback_is_not_a_successful_lsp_edit(self):
        self.assertEqual(reliability.response_failures(0, {"success": True, "data": {"success": False, "backend": "lsp"}}), ["tool_rejected"])
        self.assertEqual(reliability.response_failures(0, {"success": True, "data": {"success": True, "backend": "unity"}}), ["wrong_backend"])
        self.assertEqual(reliability.response_failures(0, {"success": True, "data": {"success": True, "backend": "lsp"}}), [])

    def test_nearest_rank_percentiles_and_rate_include_failed_attempts(self):
        rows = [{"elapsed_ms": n, "direct_ms": 1, "failures": [] if n < 100 else ["timeout"]}
                for n in range(1, 101)]
        summary = reliability.summarize(rows)
        self.assertEqual(summary["success_rate"], 0.99)
        self.assertEqual(summary["p50_ms"], 50)
        self.assertEqual(summary["p95_ms"], 95)
        self.assertEqual(summary["failure_types"], {"timeout": 1})
        self.assertEqual(reliability.summarize([])["success_rate"], 0)

    def test_regression_baseline_allows_only_exact_documented_signatures(self):
        baseline = {"replace": ["bom_added"]}
        self.assertEqual(reliability.regressions([{"scenario": "replace", "failures": ["bom_added"]}], baseline), [])
        self.assertTrue(reliability.regressions([{"scenario": "replace", "failures": ["bom_added", "text_changed"]}], baseline))
        self.assertTrue(reliability.regressions([{"scenario": "new", "failures": ["bom_added"]}], baseline))
        self.assertEqual(reliability.regressions([{"scenario": "replace", "failures": []}], baseline), [])

    def test_missing_os_or_condition_never_produces_enforcement_recommendation(self):
        report = {"os": "Windows", "rows": [{"scenario": "create_plain", "condition": "warm_indexed", "failures": [], "elapsed_ms": 1, "direct_ms": 1}]}
        text = reliability.markdown([report])
        self.assertIn("Windows", text)
        self.assertIn("INSUFFICIENT", text)
        self.assertIn("macOS", text)
        self.assertIn("Linux", text)

    def test_known_text_defect_cannot_mask_a_different_destructive_edit(self):
        baseline = {"replace": {"failures": ["text_changed"], "text_hashes": {"Assets/A.cs": "known"}}}
        row = {"scenario": "replace", "failures": ["text_changed"], "differences": [
            {"path": "Assets/A.cs", "direct_text_sha256": "expected", "lsp_text_sha256": "known"}]}
        self.assertEqual(reliability.regressions([row], baseline), [])
        row["differences"][0]["lsp_text_sha256"] = "different-loss"
        self.assertTrue(reliability.regressions([row], baseline))
        row["failures"] = []
        self.assertEqual(reliability.regressions([row], baseline), [])

    def test_ci_retains_failures_and_runs_real_lsp_on_all_three_platforms(self):
        workflow = (ROOT / ".github/workflows/lsp-write-reliability.yml").read_text(encoding="utf-8")
        self.assertIn("os: [windows-latest, macos-latest, ubuntu-latest]", workflow)
        self.assertIn("dotnet publish lsp/Server.csproj", workflow)
        self.assertIn("--baseline tests/fixtures/lsp-write-baseline.json", workflow)
        self.assertIn("if: always()", workflow)
        self.assertNotIn("continue-on-error", workflow)
        self.assertNotIn("--record-only", workflow)
        required = (ROOT / ".github/workflows/test.yml").read_text(encoding="utf-8")
        lsp_job = required.split("  lsp-test:\n", 1)[1].split("  rust-coverage:\n", 1)[0]
        self.assertIn("name: LSP Tests (required)", lsp_job)
        self.assertIn("needs: lsp-write-reliability", lsp_job)
        self.assertIn("if: always()", lsp_job)
        self.assertIn('run: test "$RELIABILITY_RESULT" = success', lsp_job)

    def test_one_corrupt_edit_prevents_enforcement_even_at_99_percent(self):
        reports = []
        for system in reliability.OS_NAMES:
            rows = [{"scenario": case["id"], "condition": condition,
                     "failures": [], "elapsed_ms": 1, "direct_ms": 1}
                    for condition in reliability.CONDITIONS for case in reliability.scenarios() for _ in range(100)]
            reports.append({"os": system, "rows": rows, "finished_at": "2026-10-13T00:00:00Z"})
        self.assertIn("ELIGIBLE", reliability.markdown(reports))
        reports[0]["rows"][0]["failures"] = ["bom_added"]
        self.assertIn("DO NOT ENFORCE", reliability.markdown(reports))
        reports[0].pop("finished_at")
        self.assertIn("INSUFFICIENT", reliability.markdown(reports))

    def test_known_bom_defect_cannot_mask_an_unrelated_file_write(self):
        row = {"scenario": "rename_multifile", "failures": ["bom_added"], "differences": [{"path": "Assets/Other.cs"}]}
        self.assertTrue(reliability.regressions([row], {"rename_multifile": ["bom_added"]}))
        row["differences"] = [{"path": "Assets/Sample.cs"}]
        self.assertEqual(reliability.regressions([row], {"rename_multifile": ["bom_added"]}), [])

    def test_trial_cleanup_retries_windows_dll_locks_and_rejects_escape(self):
        with mock.patch.object(reliability.shutil, "rmtree", side_effect=[PermissionError("DLL still unloading"), None]) as remove:
            with mock.patch.object(reliability.time, "sleep"):
                reliability.cleanup_trial(ROOT / ".cache/trial", ROOT / ".cache")
            self.assertEqual(remove.call_count, 2)
        with mock.patch.object(reliability.shutil, "rmtree") as remove:
            with self.assertRaises(RuntimeError):
                reliability.cleanup_trial(ROOT, ROOT / ".cache")
            remove.assert_not_called()


if __name__ == "__main__":
    unittest.main()
