import importlib.util
from pathlib import Path
import sys
import unittest

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
                              ("find_gameobject", {"count": 0, "objects": []})]:
            with self.subTest(tool=tool), self.assertRaises(ValueError):
                self.bench.validate_result(tool, payload)
        self.bench.validate_result("modify_material", {"success": True, "propertiesModified": ["_Color"]})
        self.bench.validate_result("find_gameobject", {"count": 1, "objects": [{"name": "Perf394Target"}]})

    def test_background_requires_a_known_different_frontmost_pid(self):
        self.assertTrue(self.bench.focus_matches("frontmost", 123, 123))
        self.assertFalse(self.bench.focus_matches("frontmost", 123, 321))
        self.assertTrue(self.bench.focus_matches("background", 123, 321))
        self.assertFalse(self.bench.focus_matches("background", 123, None))
        self.assertFalse(self.bench.focus_matches("background", 123, 123))


if __name__ == "__main__":
    unittest.main()
