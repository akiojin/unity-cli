"""Host-free validation of player build evidence and runner arguments."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("player_build", ROOT / "scripts/e2e-player-build.py")
player = importlib.util.module_from_spec(spec)
spec.loader.exec_module(player)


class PlayerBuildTests(unittest.TestCase):
    def test_existing_editor_project_and_windows_target_arguments(self):
        args = player.parse_args(["--project-path", "/tmp/editor project", "--target", "StandaloneWindows64"])
        self.assertEqual(args.project_path, Path("/tmp/editor project"))
        self.assertEqual(args.target, "StandaloneWindows64")
        self.assertFalse(args.launch)

    def test_default_target_preserves_mac(self):
        self.assertEqual(player.parse_args([]).target, "StandaloneOSX")

    def test_windows_launch_target_requires_installed_playback_engine(self):
        with tempfile.TemporaryDirectory() as directory:
            unity = Path(directory) / "Unity.app/Contents/MacOS/Unity"
            unity.parent.mkdir(parents=True)
            unity.touch()
            self.assertEqual(player.launch_target_args(str(unity), "StandaloneWindows64"), [])
            (unity.parent.parent / "PlaybackEngines/WindowsStandaloneSupport").mkdir(parents=True)
            self.assertEqual(player.launch_target_args(str(unity), "StandaloneWindows64"), ["-buildTarget", "Win64"])
            self.assertEqual(player.launch_target_args(str(unity), "StandaloneOSX"), [])

    def test_missing_module_is_unsupported_but_target_mismatch_is_failure(self):
        self.assertEqual(player.preflight_status({"code": "BUILD_MODULE_MISSING"}), "UNSUPPORTED")
        self.assertEqual(player.preflight_status({"code": "INVALID_OUTPUT_PATH"}), "SUPPORTED")
        with self.assertRaisesRegex(AssertionError, "BUILD_TARGET_MISMATCH"):
            player.preflight_status({"code": "BUILD_TARGET_MISMATCH"})

    def test_windows_requires_executable_data_and_matching_report(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "Player.exe"
            output.touch()
            data = output.with_name("Player_Data")
            data.mkdir()
            payload = data / "globalgamemanagers"
            payload.touch()
            report = {"outputPath": str(output), "artifacts": [str(output), str(payload)]}
            self.assertTrue(player.valid_artifacts("StandaloneWindows64", output, report))
            self.assertFalse(player.valid_artifacts("StandaloneWindows64", output, dict(report, outputPath=str(output.with_name("Other.exe")))))
            self.assertFalse(player.valid_artifacts("StandaloneWindows64", output, dict(report, artifacts=[str(payload)])))
            self.assertFalse(player.valid_artifacts("StandaloneWindows64", output, dict(report, artifacts=[str(output)])))
            payload.unlink()
            self.assertFalse(player.valid_artifacts("StandaloneWindows64", output, report))
            data.rmdir()
            self.assertFalse(player.valid_artifacts("StandaloneWindows64", output, dict(report, artifacts=[str(output)])))

    def test_mac_requires_bundle_data_and_report_files(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "Player.app"
            data = output / "Contents/Resources/Data"
            data.mkdir(parents=True)
            payload = data / "globalgamemanagers"
            payload.touch()
            report = {"outputPath": str(output), "artifacts": [str(payload)]}
            self.assertTrue(player.valid_artifacts("StandaloneOSX", output, report))
            self.assertFalse(player.valid_artifacts("StandaloneOSX", output, dict(report, artifacts=[])))


if __name__ == "__main__":
    unittest.main()
