"""Host-free contract tests for the real Editor matrix runner."""
import importlib.util
import json
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("matrix", ROOT / "scripts/e2e-matrix.py")
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)


class MatrixTests(unittest.TestCase):
    def test_gui_fixture_regenerates_only_urp_upgrade_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            settings = project / "ProjectSettings"
            settings.mkdir()
            cache = settings / "URPProjectSettings.asset"
            cache.write_text("m_LastMaterialVersion: 10\n")
            rendering = settings / "GraphicsSettings.asset"
            rendering.write_text("keep render pipeline\n")
            matrix.prepare_perf_fixture(project)
            self.assertFalse(cache.exists())
            self.assertEqual(rendering.read_text(), "keep render pipeline\n")
            self.assertTrue((project / ".unity/perf-owned-project").is_file())
            matrix.prepare_perf_fixture(project)

    def test_perf_requires_gui_without_changing_other_suites(self):
        self.assertTrue(getattr(matrix, "requires_gui", lambda _: False)("perf"))
        self.assertTrue(matrix.requires_gui("compile,perf"))
        self.assertFalse(matrix.requires_gui("input,timeline"))
        self.assertFalse(matrix.requires_gui(None))

    def test_packages_follow_editor_catalog_and_preserve_bridge(self):
        catalog = {"com.unity.visualeffectgraph": {"version": "14.0.12"},
                   "com.unity.render-pipelines.universal": {"version": "14.0.12"},
                   "com.unity.test-framework": {"version": "1.1.33"},
                   "com.unity.timeline": {"version": "1.7.7"},
                   "com.unity.ugui": {"version": "1.0.0"}}
        result = matrix.package_manifest("2022.3.62f3", catalog, [])
        self.assertEqual(result["dependencies"]["com.unity.visualeffectgraph"], "14.0.12")
        self.assertEqual(result["dependencies"]["com.unity.ugui"], "1.0.0")
        self.assertEqual(result["dependencies"]["com.akiojin.unity-cli-bridge"], "file:unity-cli-bridge")
        self.assertIn("com.akiojin.unity-cli-bridge", result["testables"])

    def test_missing_vfx_catalog_entry_is_not_silently_skipped(self):
        with self.assertRaisesRegex(ValueError, "visualeffectgraph"):
            matrix.package_manifest("6000.7.0b2", {}, [])

    def test_readiness_rejects_error_missing_and_busy_states(self):
        for value in [{}, {"error": "offline"}, {"isCompiling": True, "isUpdating": False},
                      {"success": False, "isCompiling": False, "isUpdating": False},
                      {"isCompiling": False, "isUpdating": False, "errorCount": 1}]:
            self.assertFalse(matrix.ready(value), value)
        self.assertTrue(matrix.ready({"isCompiling": False, "isUpdating": False}))

    def test_failed_or_partial_suites_cannot_pass_report(self):
        self.assertFalse(matrix.all_passed([]))
        self.assertFalse(matrix.all_passed([{"status": "PASS"}, {"status": "SKIP"}]))
        self.assertFalse(matrix.all_passed([{"status": "FAIL"}]))
        self.assertTrue(matrix.all_passed([{"status": "PASS"}]))

    def test_suite_timeout_terminates_the_owned_process(self):
        with tempfile.TemporaryFile(mode="w+") as log:
            with self.assertRaises(subprocess.TimeoutExpired):
                matrix.run_suite([sys.executable, "-c", "import time; time.sleep(30)"],
                                 {}, log, 0.05)
            self.assertEqual(matrix.run_suite([sys.executable, "-c", "raise SystemExit(3)"],
                                             {}, log, 5), 3)

    def test_fixture_is_isolated_and_ignores_package_hash_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "repo/UnityCliBridge"
            for name in ("Assets", "ProjectSettings", "Packages/unity-cli-bridge"):
                (source / name).mkdir(parents=True)
            (source / "ProjectSettings/ProjectVersion.txt").write_text("original\n")
            fixtures = root / "repo/tests/fixtures/hot-reload"
            fixtures.mkdir(parents=True)
            for name in ("HotReloadProbe.cs", "HotReloadE2EFixture.cs"):
                (fixtures / name).write_text("// fixture\n")
            editor = root / "2022.3.62f3/Unity.app/Contents/MacOS/Unity"
            editor.parent.mkdir(parents=True)
            pm = editor.parent.parent / "Resources/PackageManager"
            (pm / "Editor").mkdir(parents=True)
            catalog = {name: {"version": "1.0.0"} for name in (
                "com.unity.visualeffectgraph", "com.unity.render-pipelines.universal",
                "com.unity.ugui", "com.unity.test-framework", "com.unity.timeline")}
            (pm / "Editor/manifest.json").write_text(json.dumps({"packages": catalog}))
            (pm / "BuiltInPackages/com.unity.modules.ui").mkdir(parents=True)
            (pm / "BuiltInPackages/com.unity.modules.ui.sha1").touch()
            destination = root / "run"
            destination.mkdir()
            with patch.object(matrix, "ROOT", root / "repo"):
                version, project, manifest = matrix.prepare(editor, destination)
            self.assertEqual(version, "2022.3.62f3")
            self.assertIn("com.unity.modules.ui", manifest["dependencies"])
            self.assertNotIn("com.unity.modules.ui.sha1", manifest["dependencies"])
            self.assertEqual((source / "ProjectSettings/ProjectVersion.txt").read_text(), "original\n")
            self.assertIn(version, (project / "ProjectSettings/ProjectVersion.txt").read_text())
            self.assertTrue((project / "Assets/HotReloadProbe.cs").is_file())
            self.assertTrue((project / "Assets/Editor/HotReloadE2EFixture.cs").is_file())

    def test_real_hot_reload_suites_need_backend_and_add_x64_editor_when_present(self):
        from argparse import Namespace
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            editor = root / "hub/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
            x64 = root / "x64/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
            x64.parent.mkdir(parents=True)
            x64.touch()
            args = Namespace(fsr_path=None, x64_editor_root=root / "x64", port=6508)
            self.assertEqual(matrix.hot_reload_apply_suites(editor, "6000.3.25f1", args, root), {})
            args.fsr_path = root / "fsr"
            suites = matrix.hot_reload_apply_suites(editor, "6000.3.25f1", args, root)
            self.assertEqual(list(suites), ["hot-reload-apply", "hot-reload-apply-x64"])
            native, rosetta = suites.values()
            self.assertIn("UNITY_PATH=" + str(editor), native)
            self.assertIn("UNITY_PATH=" + str(x64), rosetta)
            self.assertEqual(rosetta[rosetta.index("--require-arch") + 1], "X64")
            for command in (native, rosetta):
                self.assertEqual(command[command.index("--expect") + 1], "supported")
                self.assertEqual(command[command.index("--port") + 1], "6509")
            # No x64 build of this version: only the native Editor runs, nothing is reported as skipped.
            self.assertEqual(list(matrix.hot_reload_apply_suites(editor, "2022.3.62f3", args, root)), ["hot-reload-apply"])

    def test_default_selection_appends_real_hot_reload_suites(self):
        self.assertEqual(matrix.selected_suites(None, [])[-1], "all-tools")
        self.assertNotIn("hot-reload-apply", matrix.selected_suites(None, []))
        chosen = matrix.selected_suites(None, ["hot-reload-apply", "hot-reload-apply-x64"])
        self.assertEqual(chosen[-2:], ["hot-reload-apply", "hot-reload-apply-x64"])
        self.assertEqual(matrix.selected_suites("eval,hot-reload-apply", ["hot-reload-apply"]), ["eval", "hot-reload-apply"])
        with self.assertRaisesRegex(ValueError, "hot-reload-apply-x64"):
            matrix.selected_suites("hot-reload-apply-x64", ["hot-reload-apply"])


if __name__ == "__main__":
    unittest.main()
