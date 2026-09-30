"""Regression checks for routing overrides and the LLM's example catalog."""
import importlib.util
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "skill_routing", ROOT / "scripts/skill-eval/run-codex-routing.py"
)
routing = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = routing
SPEC.loader.exec_module(routing)


class SkillRoutingTests(unittest.TestCase):
    def test_catalog_preserves_action_discriminators_from_examples(self):
        text = (
            "unity-cli raw input_gamepad --json "
            "'{\"action\":\"button\",\"button\":\"a\",\"buttonAction\":\"press\",\"holdSeconds\":0.2}'"
        )
        skill = routing.SkillSummary(
            "runtime", "Runtime input", [], [], routing.parse_tool_summaries(text), []
        )
        catalog = routing.format_skill_catalog([skill])
        self.assertIn('"buttonAction":"press"', catalog)
        self.assertIn('"action":"button"', catalog)

    def test_runtime_inspection_is_not_forced_to_binding_mutation(self):
        prompt = "Inspect the runtime InputAction state of Assets/Input/Player.inputactions during Play Mode."
        self.assertIsNone(routing.route_by_keywords(prompt))

    def test_asset_update_then_play_keeps_existing_route(self):
        prediction = routing.route_by_keywords("入力アセット更新後にPlayでキー入力検証したい")
        self.assertEqual(prediction["predicted_tool"], "add_input_binding")
        self.assertEqual(prediction["predicted_skills"][0], "unity-input-system")


if __name__ == "__main__":
    unittest.main()
