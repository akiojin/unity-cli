"""Regression checks for routing overrides and the LLM's example catalog."""
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "skill_routing", ROOT / "scripts/skill-eval/run-codex-routing.py"
)
routing = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = routing
SPEC.loader.exec_module(routing)


class SkillRoutingTests(unittest.TestCase):
    def test_built_cli_catalog_covers_benchmark_tools_and_keys(self):
        binary = next((p for p in (ROOT / "target/release/unity-cli",
                                  ROOT / "target/debug/unity-cli") if p.is_file()), None)
        if binary is None:
            self.skipTest("Build unity-cli to check its canonical schema catalog")
        catalog = routing.load_tool_catalog(str(binary))
        lines = {line.split(" | ", 1)[0][2:]: line for line in catalog.splitlines()}
        benchmark = ROOT / "tests/fixtures/skill-routing/benchmark.jsonl"
        for row in map(json.loads, benchmark.read_text().splitlines()):
            with self.subTest(case=row["id"]):
                self.assertIn(row["expected_tool"], lines)
                hints = lines[row["expected_tool"]]
                for key in row["expected_payload_keys"]:
                    self.assertRegex(hints, rf"\b{key}\b")

    def test_main_supplies_cli_schema_to_model(self):
        schema = [{"name": "clear_console", "params_schema": {"properties": {}}}]
        response = subprocess.CompletedProcess([], 0, json.dumps({"tools": schema}), "")
        with (patch.object(sys, "argv", ["routing", "--unity-cli", "/custom/unity-cli",
                                       "--skills-dir", str(ROOT / ".claude-plugin/plugins/unity-cli/skills")]),
              patch.object(sys, "stdin", io.StringIO("Clear the console")),
              patch.object(sys, "stdout", io.StringIO()) as output,
              patch.object(routing.subprocess, "run", return_value=response) as run,
              patch.object(routing, "invoke_codex", return_value={"predicted_tool": "clear_console"}) as model):
            self.assertEqual(routing.main(), 0)
        self.assertEqual(run.call_args.args[0], ["/custom/unity-cli", "tool", "schema", "--output", "json"])
        self.assertIn("- clear_console | keys: (none)", model.call_args.args[0])
        self.assertEqual(json.loads(output.getvalue())["predicted_tool"], "clear_console")

    def test_schema_failure_does_not_silently_use_incomplete_catalog(self):
        with patch.object(routing.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "unity-cli")):
            with self.assertRaises(subprocess.CalledProcessError):
                routing.load_tool_catalog("unity-cli")

    def test_catalog_includes_tools_without_skill_command_examples(self):
        specs = [
            {"name": "load_scene", "params_schema": {"properties": {"scenePath": {"type": "string"}}}},
            {"name": "list_scenes", "params_schema": {"properties": {}}},
            {"name": "clear_console", "params_schema": {"properties": {}}},
        ]
        catalog = routing.format_tool_catalog(specs)
        for name in ("load_scene", "list_scenes", "clear_console", "scenePath"):
            self.assertIn(name, catalog)
        prompt = routing.build_prompt("skills", "Load a scene", catalog)
        self.assertIn(catalog, prompt)

    def test_schema_catalog_keeps_top_level_keys_and_operation_enums(self):
        specs = [{"name": "operation", "params_schema": {
            "properties": {"action": {"enum": ["read", "write"]},
                           "properties": {"type": "object", "properties": {"nested": {}}}},
            "oneOf": [{"properties": {"scenePath": {"type": "string"}}}],
        }}]
        catalog = routing.format_tool_catalog(specs)
        self.assertIn("scenePath", catalog)
        self.assertIn("write", catalog)
        self.assertNotIn("nested", catalog)

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
