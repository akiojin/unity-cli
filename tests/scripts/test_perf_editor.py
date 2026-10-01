import importlib.util
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))


class EditorPerfTests(unittest.TestCase):
    def setUp(self):
        path = ROOT / "scripts/bench-editor-ops.py"
        self.assertTrue(path.exists(), "23-operation Editor benchmark is missing")
        spec = importlib.util.spec_from_file_location("bench_editor_ops", path)
        self.bench = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.bench)

    def test_exact_staff_operation_set_and_mutation_order(self):
        operations = self.bench.operations()
        names = [row[0] for row in operations]
        self.assertEqual(len(names), 23)
        self.assertEqual(len(set(names)), 23)
        self.assertEqual(names[:5], ["editor_state", "hierarchy", "find_gameobject", "transform", "position"])
        self.assertEqual(names[-3:], ["read_csharp", "play_until_ready", "stop_until_ready"])
        self.assertLess(names.index("asset_copy"), names.index("asset_move"))
        self.assertLess(names.index("asset_move"), names.index("asset_delete"))

    def test_failures_and_os_screenshot_fallback_are_not_samples(self):
        for payload in [{}, {"error": "offline"}, {"success": False}, {"status": "error"},
                        {"fallback": "os", "path": "/tmp/image.png"}]:
            with self.subTest(payload=payload), self.assertRaises(ValueError):
                self.bench.validate_result("capture_screenshot", payload)
        self.bench.validate_result("get_editor_state", {"status": "success", "state": {"isPlaying": False}})

    def test_screenshot_benchmark_explicitly_preserves_camera_only_capture(self):
        screenshot = next(params for _, tool, params in self.bench.operations()
                          if tool == "capture_screenshot")
        self.assertIs(screenshot.get("includeUI"), False)

    def test_play_transition_waits_for_observed_state(self):
        calls = []
        states = iter([False, False, True])

        def raw(tool, params):
            calls.append(tool)
            return {"state": {"isPlaying": next(states)}} if tool == "get_editor_state" else {"success": True}

        result = self.bench.transition(raw, True, timeout=1, interval=0)
        self.assertTrue(result["state"]["isPlaying"])
        self.assertEqual(calls, ["play_game", "get_editor_state", "get_editor_state", "get_editor_state"])
        with self.assertRaises(TimeoutError):
            self.bench.transition(lambda *args: {"state": {"isPlaying": False}}, True, timeout=0)

    def test_noop_material_and_missing_target_are_not_success(self):
        for tool, payload in [("modify_material", {"success": True, "propertiesModified": []}),
                              ("find_gameobject", {"count": 0, "objects": []}),
                              ("delete_gameobject", {"deletedCount": 0, "notFoundCount": 1}),
                              ("modify_gameobject", {"position": {"x": 0, "y": 0, "z": 0}})]:
            with self.subTest(tool=tool), self.assertRaises(ValueError):
                self.bench.validate_result(tool, payload)
        self.bench.validate_result("modify_material", {"success": True, "propertiesModified": ["_Color"]})
        self.bench.validate_result("find_gameobject", {"count": 1, "objects": [{"name": "Perf394Target"}]})
        self.bench.validate_result("delete_gameobject", {"deletedCount": 1, "notFoundCount": 0})
        self.bench.validate_result("modify_gameobject", {"position": {"x": 1, "y": 2, "z": 3}})

    def test_background_requires_a_known_different_frontmost_pid(self):
        self.assertTrue(self.bench.focus_matches("frontmost", 123, 123))
        self.assertFalse(self.bench.focus_matches("frontmost", 123, 321))
        self.assertTrue(self.bench.focus_matches("background", 123, 321))
        self.assertFalse(self.bench.focus_matches("background", 123, None))
        self.assertFalse(self.bench.focus_matches("background", 123, 123))

    def test_focus_activation_waits_for_the_requested_pid(self):
        with patch.object(self.bench._focus, "frontmost_pid", side_effect=[321, 321, 123]), \
             patch.object(self.bench._focus, "activate") as activate, \
             patch.object(self.bench.time, "sleep"):
            self.bench.ensure_focus("frontmost", 123)
        activate.assert_called_with(123)

    def test_focus_loss_discards_complete_cycle_without_repeating_mutations(self):
        editor = Mock(last_elapsed_ms=1.0)
        with patch.object(self.bench, "ensure_focus"), \
             patch.object(self.bench, "transition"), \
             patch.object(self.bench._focus, "frontmost_pid", side_effect=[123, 321] + [123] * 44):
            self.assertIsNone(self.bench.measure_cycle(editor, "frontmost", 123))
        self.assertEqual(editor.raw.call_count, 21)
        with patch.object(self.bench, "ensure_focus"), \
             patch.object(self.bench, "transition"), \
             patch.object(self.bench._focus, "frontmost_pid", return_value=123):
            self.assertEqual(len(self.bench.measure_cycle(editor, "frontmost", 123)), 23)

    def test_interrupt_records_incomplete_measurement(self):
        editor = Mock()
        editor.setup.return_value = {"unity": {"unityVersion": "6000.3.25f1"}}
        editor.raw.return_value = {"editor": {"enterPlayModeOptionsEnabled": True,
                                              "enterPlayModeOptions": "DisableDomainReload"}}
        editor.command.return_value = {"running": True, "connections": 1, "pid": 456}
        with tempfile.TemporaryDirectory() as directory, \
             patch.object(self.bench._focus, "listener_pid", return_value=123), \
             patch.object(self.bench, "measure_cycle", side_effect=KeyboardInterrupt):
            args = SimpleNamespace(port=6509, warmup=3, iterations=30,
                                   history=Path(directory) / "history.jsonl", regression_percent=20)
            try:
                report = self.bench.measure_focus(editor, "frontmost", args, {})
            except KeyboardInterrupt:
                self.fail("interrupt must be recorded before stopping further focus conditions")
            self.assertEqual(report["status"], "FAIL")
            self.assertTrue(report["interrupted"])
            self.assertFalse(report["measurements_complete"])
            self.assertEqual(report["passed"], 0)
            self.assertTrue(args.history.is_file())

    def test_background_screenshot_accepts_only_its_own_editor_activation(self):
        editor = Mock(last_elapsed_ms=1.0)
        screenshot_index = [op[0] for op in self.bench.operations()].index("screenshot")
        for after, accepted in [(123, True), (321, True), (456, False), (None, False)]:
            observations = [321, 321] * 23
            observations[screenshot_index * 2 + 1] = after
            with self.subTest(after=after), patch.object(self.bench, "ensure_focus"), \
                 patch.object(self.bench, "transition"), \
                 patch.object(self.bench._focus, "frontmost_pid", side_effect=observations):
                result = self.bench.measure_cycle(editor, "background", 123)
                self.assertEqual(result is not None, accepted)

    def test_background_other_operations_reject_any_frontmost_pid_change(self):
        editor = Mock(last_elapsed_ms=1.0)
        for after in (123, 456, None):
            with self.subTest(after=after), patch.object(self.bench, "ensure_focus"), \
                 patch.object(self.bench, "transition"), \
                 patch.object(self.bench._focus, "frontmost_pid", side_effect=[321, after] + [321] * 44):
                self.assertIsNone(self.bench.measure_cycle(editor, "background", 123))

    def test_game_view_focus_mode_is_scoped_and_restored(self):
        editor = self.bench.Editor(SimpleNamespace(unity_cli=Path("unused"), port=6509), {})
        original = {"123": "PlayFocused"}
        unfocused = {"123": "PlayUnfocused"}
        editor.game_view_modes = Mock(side_effect=[original, unfocused, original])
        editor.configure_play_focus()
        editor.configure_play_focus()
        editor.restore_play_focus()
        self.assertEqual(editor.game_view_modes.call_args_list,
                         [unittest.mock.call(), unittest.mock.call(unfocused), unittest.mock.call(original)])

    def test_game_view_mode_update_must_be_observed(self):
        editor = self.bench.Editor(SimpleNamespace(unity_cli=Path("unused"), port=6509), {})
        editor.command = Mock(return_value={"state": "completed", "value": {"123": "PlayFocused"}})
        with self.assertRaises(RuntimeError):
            editor.game_view_modes({"123": "PlayUnfocused"})


if __name__ == "__main__":
    unittest.main()
