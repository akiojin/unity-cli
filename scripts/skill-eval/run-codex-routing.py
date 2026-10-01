#!/usr/bin/env python3
"""Route a skill benchmark prompt with Codex and return prediction JSON.

Reads the user prompt from stdin and prints a single JSON object:
{
  "predicted_skills": ["unity-scene-create"],
  "predicted_tool": "create_scene",
  "predicted_payload_keys": ["sceneName", "path"]
}
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable


SCHEMA = {
    "type": "object",
    "properties": {
        "predicted_skills": {
            "type": "array",
            "items": {"type": "string"},
            "minItems": 1,
            "maxItems": 2,
        },
        "predicted_tool": {"type": "string"},
        "predicted_payload_keys": {
            "type": "array",
            "items": {"type": "string"},
        },
    },
    "required": ["predicted_skills", "predicted_tool", "predicted_payload_keys"],
    "additionalProperties": False,
}


@dataclass
class ToolSummary:
    name: str
    keys: set[str] = field(default_factory=set)
    examples: list[dict[str, object]] = field(default_factory=list)


@dataclass
class SkillSummary:
    name: str
    description: str
    use_when: list[str]
    do_not_use: list[str]
    tools: dict[str, ToolSummary]
    delegates: list[str]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--skills-dir",
        default=".claude-plugin/plugins/unity-cli/skills",
        help="Path to the plugin skills directory.",
    )
    parser.add_argument(
        "--unity-cli",
        default="unity-cli",
        help="CLI binary used to read canonical tool schemas (no Editor required).",
    )
    parser.add_argument(
        "--model",
        default="gpt-5.4-mini",
        help="Codex model alias or full name.",
    )
    return parser.parse_args()


def parse_frontmatter(text: str) -> dict[str, str]:
    match = re.match(r"^---\n(.*?)\n---\n", text, re.S)
    if not match:
        raise ValueError("missing frontmatter")

    data: dict[str, str] = {}
    for line in match.group(1).splitlines():
        if ":" not in line or line.startswith((" ", "\t")):
            continue
        key, value = line.split(":", 1)
        data[key.strip()] = value.strip()
    return data


def section_text(text: str, heading: str) -> str:
    pattern = re.compile(rf"^## {re.escape(heading)}\n(.*?)(?=^## |\Z)", re.M | re.S)
    match = pattern.search(text)
    return match.group(1).strip() if match else ""


def bullet_lines(section: str, limit: int) -> list[str]:
    lines: list[str] = []
    for line in section.splitlines():
        stripped = line.strip()
        if stripped.startswith("- "):
            lines.append(stripped[2:].strip())
        if len(lines) >= limit:
            break
    return lines


def parse_tool_summaries(text: str) -> dict[str, ToolSummary]:
    tools: dict[str, ToolSummary] = {}

    def add(tool_name: str, keys: Iterable[str]) -> None:
        summary = tools.setdefault(tool_name, ToolSummary(name=tool_name))
        summary.keys.update(k for k in keys if k)

    def add_from_json(tool_name: str, payload: str) -> None:
        try:
            parsed = json.loads(payload)
        except Exception:
            return
        if isinstance(parsed, dict):
            add(tool_name, parsed.keys())
            examples = tools[tool_name].examples
            if parsed not in examples and len(examples) < 2:
                examples.append(parsed)

    for match in re.finditer(r"unity-cli raw ([a-z0-9_]+) --json '([^']*)'", text):
        add_from_json(match.group(1), match.group(2))

    for match in re.finditer(r'unity-cli raw ([a-z0-9_]+) --json "([^"]*)"', text):
        add_from_json(match.group(1), match.group(2))

    if re.search(r"\bunity-cli system ping\b", text):
        add("ping", [])

    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("unity-cli scene create"):
            keys = {"sceneName"}
            if "--path" in stripped:
                keys.add("path")
            add("create_scene", keys)
        elif stripped.startswith("unity-cli scene load"):
            add("load_scene", {"scenePath"})
        elif stripped.startswith("unity-cli scene save"):
            add("save_scene", set())

    return tools


def parse_delegates(text: str) -> list[str]:
    section = section_text(text, "Delegation Map")
    delegates: list[str] = []
    for line in section.splitlines():
        stripped = line.strip()
        match = re.search(r"`([a-z0-9-]+)`", stripped)
        if match:
            delegates.append(match.group(1))
    return delegates


def load_skills(skills_dir: Path) -> list[SkillSummary]:
    skills: list[SkillSummary] = []
    for skill_md in sorted(skills_dir.glob("*/SKILL.md")):
        text = skill_md.read_text(encoding="utf-8")
        frontmatter = parse_frontmatter(text)
        skills.append(
            SkillSummary(
                name=frontmatter["name"],
                description=frontmatter["description"],
                use_when=bullet_lines(section_text(text, "Use When"), 3),
                do_not_use=bullet_lines(section_text(text, "Do Not Use When"), 2),
                tools=parse_tool_summaries(text),
                delegates=parse_delegates(text),
            )
        )
    return skills


def format_skill_catalog(skills: list[SkillSummary]) -> str:
    def tool_sort_key(tool_name: str) -> tuple[int, str]:
        if tool_name == "write_csharp_file":
            return (0, tool_name)
        if tool_name in {"input_keyboard", "input_mouse", "input_gamepad", "input_touch"}:
            return (1, tool_name)
        if tool_name in {"click_ui_element", "set_ui_element_value", "simulate_ui_input"}:
            return (2, tool_name)
        if tool_name == "apply_csharp_edits":
            return (4, tool_name)
        if tool_name in {"play_game", "get_editor_state", "ping"}:
            return (5, tool_name)
        return (3, tool_name)

    lines: list[str] = []
    for skill in skills:
        lines.append(f"- {skill.name}: {skill.description}")
        if skill.use_when:
            lines.append(f"  Use when: {'; '.join(skill.use_when)}")
        if skill.do_not_use:
            lines.append(f"  Do not use: {'; '.join(skill.do_not_use)}")
        if skill.delegates:
            lines.append(f"  Delegates: {', '.join(skill.delegates)}")
        if skill.tools:
            tool_parts = []
            for tool_name in sorted(skill.tools, key=tool_sort_key):
                keys = sorted(skill.tools[tool_name].keys)
                if keys:
                    tool_parts.append(f"{tool_name} | keys: {', '.join(keys)}")
                else:
                    tool_parts.append(f"{tool_name}")
                if skill.tools[tool_name].examples:
                    examples = json.dumps(
                        skill.tools[tool_name].examples,
                        ensure_ascii=False,
                        separators=(",", ":"),
                    )
                    tool_parts[-1] += f" | example payloads: {examples}"
            lines.append(f"  Tool hints: {'; '.join(tool_parts)}")
    return "\n".join(lines)


def format_tool_catalog(specs: list[dict[str, object]]) -> str:
    """Include every real tool, even when no skill has a command example.

    Only collect top-level payload properties; variant schemas may add keys,
    but nested object fields are not top-level arguments.
    """
    def properties(schema: dict) -> dict:
        result = dict(schema.get("properties", {}))
        for composition in ("oneOf", "anyOf", "allOf"):
            for variant in schema.get(composition, []):
                result.update(properties(variant))
        return result

    lines = []
    for spec in sorted(specs, key=lambda item: item["name"]):
        hints = []
        for key, value in sorted(properties(spec["params_schema"]).items()):
            hint = key
            if "enum" in value:
                hint += "=" + json.dumps(value["enum"], ensure_ascii=False)
            hints.append(hint)
        lines.append(f"- {spec['name']} | keys: {', '.join(hints) or '(none)'}")
    return "\n".join(lines)


def load_tool_catalog(binary: str) -> str:
    result = subprocess.run(
        [binary, "tool", "schema", "--output", "json"],
        capture_output=True, text=True, check=True,
    )
    return format_tool_catalog(json.loads(result.stdout)["tools"])


def build_prompt(skill_catalog: str, user_prompt: str, tool_catalog: str = "") -> str:
    return f"""You are evaluating skill routing for the unity-cli plugin.

Choose the best matching skill or up to two skills from the catalog below.

Rules:
- Prefer a higher-level orchestration skill when the request explicitly asks for an iterative fix-run-verify loop or repeated validation until acceptance criteria are met.
- Prefer a lower-level single-purpose skill when the request is clearly just one step, such as only editing code, only editing `.inputactions`, or only running Play Mode input/media capture.
- `predicted_tool` must represent the main requested operation, not a setup, search, or precondition step.
- Return only the bare tool name for `predicted_tool`. Do not append payload keys, parentheses, or commentary.
- Prefer the substantive mutation or interaction tool over helpers like `find_symbol`, `search`, `get_editor_state`, `ping`, or Play Mode setup, unless setup is the main request.
- If the prompt asks to change something and then validate it, choose the change tool.
- If the prompt asks to add, create, update, remove, or bind something and then inspect or analyze it, choose the mutation tool rather than the follow-up inspection tool.
- If the prompt asks to interact, capture, or inspect a specific runtime effect, choose that specific interaction or capture tool over generic setup.
- If the prompt explicitly says to perform an interaction and then capture screenshot or video, prefer the interaction tool that creates the target state.
- If the prompt asks to re-verify or validate behavior with keyboard, mouse, gamepad, or touch input, prefer the concrete input tool over `play_game`.
- If the prompt mentions profiler work inside a validation loop, prefer `profiler_start` as the representative profiler capture tool unless the request is only about reading existing metrics.
- For orchestration requests, choose the concrete tool that best represents the first substantive action in the loop.
- If multiple write tools are plausible, prefer the narrowest representative edit tool over a broad batch-edit helper.
- Prefer `write_csharp_file` over `apply_csharp_edits` unless the prompt clearly implies coordinated multi-file editing.
- Prefer `unity-cli-usage` over `unity-editor-tools` when the prompt is about basic connectivity, editor-state basics, command stats, or choosing the right CLI entrypoint.
- `predicted_payload_keys` must contain only the minimum top-level payload keys implied by that first tool call.
- If the first tool likely needs no payload, return an empty array.
- Return only skills and tools that appear in the catalog.
- The canonical tool catalog lists available tools and allowed top-level keys, including tools omitted from the skill examples. Keys are options, not a requirement to send every key. Skill examples are illustrative, not exhaustive.

Skill catalog:
{skill_catalog}

Canonical tool catalog:
{tool_catalog}

User prompt:
{user_prompt}
"""


def ordered_unique(items: Iterable[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for item in items:
        if item and item not in seen:
            seen.add(item)
            result.append(item)
    return result


def make_prediction(skills: Iterable[str], tool: str, payload_keys: Iterable[str]) -> dict[str, object]:
    return {
        "predicted_skills": ordered_unique(skills)[:2],
        "predicted_tool": tool,
        "predicted_payload_keys": ordered_unique(payload_keys),
    }


def route_by_keywords(user_prompt: str) -> dict[str, object] | None:
    text = user_prompt.strip()
    lower = text.lower()

    def has_any(*terms: str) -> bool:
        return any(term.lower() in lower for term in terms)

    # Foundation / first-step routing.
    if has_any("疎通確認") and has_any("editor情報", "エディタ情報"):
        return make_prediction(["unity-cli-usage", "unity-editor-tools"], "ping", [])
    if has_any("helloメッセージ", "hello message") and has_any("ping"):
        return make_prediction(["unity-cli-usage"], "ping", ["message"])
    if has_any("エディタ状態", "editor state"):
        return make_prediction(["unity-cli-usage"], "get_editor_state", [])
    if has_any("コマンド統計", "command stats"):
        return make_prediction(["unity-cli-usage"], "get_command_stats", [])
    if has_any("packages一覧", "package一覧", "packages list", "package list", "パッケージ一覧"):
        return make_prediction(["unity-csharp-navigate"], "list_packages", [])

    # Explicit search/inspect-first workflows.
    if has_any("検索したい") and has_any("oncollisionenter"):
        return make_prediction(["unity-csharp-navigate"], "search", ["pattern"])
    if has_any("条件を満たすまで回したい") and has_any("play") and has_any("実装して", "実装"):
        return make_prediction(["unity-development-loop"], "write_csharp_file", [])
    if has_any("保存フロー") and has_any("繰り返したい") and has_any("クリック", "スクリーンショット"):
        return make_prediction(["unity-development-loop"], "write_csharp_file", [])
    if has_any("入力バグ") and has_any("最小修正") and has_any("ゲームパッド", "再検証"):
        return make_prediction(["unity-development-loop"], "play_game", [])
    if has_any("action map") and has_any("作成したい", "作りたい"):
        return make_prediction(["unity-input-system"], "create_action_map", ["assetPath", "mapName"])
    if has_any("binding index") and has_any("削除"):
        return make_prediction(
            ["unity-input-system"],
            "remove_input_binding",
            ["assetPath", "mapName", "actionName", "bindingIndex"],
        )
    if (
        has_any("入力アセット", ".inputactions", "action map", "binding")
        and has_any("play", "キー入力", "検証")
        and has_any("更新", "追加", "作成", "update", "add", "create", "bind ")
    ):
        return make_prediction(
            ["unity-input-system", "unity-playmode-testing"],
            "add_input_binding",
            ["assetPath", "mapName", "actionName", "path"],
        )
    if has_any("シーン作成後", "scene作成後") and has_any("hierarchy"):
        return make_prediction(["unity-scene-create", "unity-scene-inspect"], "create_scene", ["sceneName", "path"])
    if has_any("canvas配下") and has_any("empty") and has_any("作る", "作成"):
        return make_prediction(["unity-scene-create"], "create_gameobject", ["name", "parentPath"])
    if has_any("詳細確認後", "詳細を確認後") and has_any("コンポーネント値", "値を変え", "値を変更"):
        return make_prediction(
            ["unity-scene-inspect", "unity-gameobject-edit"],
            "get_gameobject_details",
            ["gameObjectName"],
        )
    if has_any("シーンをロード") and has_any("play開始"):
        return make_prediction(["unity-scene-create", "unity-playmode-testing"], "load_scene", ["scenePath"])
    if has_any("ui検索結果") and has_any("入力シーケンス"):
        return make_prediction(["unity-ui-automation", "unity-playmode-testing"], "find_ui_elements", ["namePattern"])
    if has_any("uiボタン") and has_any("押して") and has_any("スクリーンショット"):
        return make_prediction(["unity-ui-automation", "unity-playmode-testing"], "click_ui_element", ["elementPath"])
    if has_any("探してから", "検索してから") and has_any("rename", "リネーム"):
        return make_prediction(["unity-csharp-navigate", "unity-csharp-edit"], "find_symbol", ["name"])
    if has_any("ローカル検索") and has_any("クラス") and has_any("新規作成", "作りたい"):
        return make_prediction(["unity-csharp-navigate", "unity-csharp-edit"], "search", ["pattern"])
    if has_any("group追加後", "グループ追加後") and has_any("addressables", "アドレッサブル") and has_any("ビルド"):
        return make_prediction(
            ["unity-addressables", "unity-asset-management"],
            "addressables_manage",
            ["action", "groupName"],
        )

    # Scene inspection and editing.
    if has_any("hierarchy") and has_any("軽量モード"):
        return make_prediction(["unity-scene-inspect"], "get_hierarchy", ["nameOnly"])
    if has_any("階層込み") and has_any("詳細"):
        return make_prediction(["unity-scene-inspect"], "get_gameobject_details", ["gameObjectName", "includeChildren"])
    if has_any("transform") and has_any("値", "確認"):
        return make_prediction(["unity-scene-inspect"], "get_component_values", ["gameObjectName", "componentType"])
    if has_any("どこから参照", "参照されている"):
        return make_prediction(["unity-scene-inspect"], "get_object_references", ["gameObjectName"])
    if has_any("コンポーネント一覧"):
        return make_prediction(["unity-gameobject-edit"], "list_components", ["gameObjectPath"])
    if has_any("レイヤー一覧"):
        return make_prediction(["unity-gameobject-edit"], "manage_layers", ["action"])
    if has_any("タグ") and has_any("追加したい"):
        return make_prediction(["unity-gameobject-edit"], "manage_tags", ["action", "tagName"])
    if has_any("rigidbody", "boxcollider", "collider", "コンポーネント") and has_any("追加したい", "追加して"):
        return make_prediction(["unity-scene-create"], "add_component", ["gameObjectPath", "componentType"])
    if has_any("rigidbody") and has_any("mass"):
        return make_prediction(["unity-gameobject-edit"], "modify_component", ["gameObjectPath", "componentType", "properties"])
    if has_any("フィールド更新", "field更新", "直接フィールド"):
        return make_prediction(
            ["unity-gameobject-edit"],
            "set_component_field",
            ["gameObjectPath", "componentType", "fieldPath", "value"],
        )

    # UI automation.
    if has_any("buttonコンポーネント", "button component") and has_any("ui") and has_any("列挙", "検索"):
        return make_prediction(["unity-ui-automation"], "find_ui_elements", ["elementType"])
    if has_any("canvas") and has_any("配下") and has_any("検索"):
        return make_prediction(["unity-ui-automation"], "find_ui_elements", ["canvasFilter"])

    # Prefabs.
    if has_any("プレハブ", "prefab"):
        if has_any("配置位置", "配置") and has_any("生成", "instantiate", "配置したい", "調整"):
            return make_prediction(["unity-prefab-workflow", "unity-gameobject-edit"], "instantiate_prefab", ["prefabPath"])
        if has_any("テンプレート"):
            return make_prediction(["unity-prefab-workflow"], "create_prefab", ["prefabPath", "createFromTemplate"])
        if has_any("上書き"):
            return make_prediction(["unity-prefab-workflow"], "create_prefab", ["prefabPath", "overwrite"])

    # Asset and addressables management.
    if has_any("assetdatabase") and has_any("refresh"):
        return make_prediction(["unity-asset-management"], "manage_asset_database", ["action"])
    if has_any("別パス") and has_any("コピー"):
        return make_prediction(["unity-asset-management"], "manage_asset_database", ["action", "fromPath", "toPath"])
    if has_any("未使用アセット") and has_any("検出"):
        return make_prediction(["unity-asset-management"], "analyze_asset_dependencies", ["action"])
    if has_any("companyname") and has_any("更新"):
        return make_prediction(["unity-editor-tools"], "update_project_settings", ["confirmChanges", "player"])
    if has_any("project設定", "project settings") and has_any("見て") and has_any("更新"):
        return make_prediction(["unity-editor-tools", "unity-cli-usage"], "get_project_settings", ["includePlayer"])
    if has_any("addressables") and has_any("追加したい") and has_any("prefab"):
        return make_prediction(["unity-addressables"], "addressables_manage", ["action", "groupName", "assetPath", "address"])
    if has_any("重複", "未使用") and has_any("解析"):
        return make_prediction(["unity-addressables"], "addressables_analyze", ["action"])
    if has_any("ラベル") and has_any("付与"):
        return make_prediction(["unity-addressables"], "addressables_manage", ["action", "assetPath", "label"])
    if has_any("アドレス名") and has_any("変更"):
        return make_prediction(["unity-addressables"], "addressables_manage", ["action", "assetPath", "newAddress"])

    # C# editing and runtime input.
    if has_any("不要メンバー") and has_any("削除"):
        keys = ["relative", "namePath"]
        if has_any("参照チェック"):
            keys.append("failOnReferences")
        return make_prediction(["unity-csharp-edit"], "remove_symbol", keys)
    if has_any("メソッド本体") and has_any("置換"):
        return make_prediction(["unity-csharp-edit"], "replace_symbol_body", ["relative", "namePath", "body"])
    if has_any("jump") and has_any("後に") and has_any("新規メソッド"):
        return make_prediction(["unity-csharp-edit"], "insert_after_symbol", ["relative", "namePath", "text"])
    if has_any("newtext") and has_any("文法") and has_any("検証"):
        return make_prediction(["unity-csharp-edit"], "validate_text_edits", ["relative", "newText"])
    if has_any("play検証はまだ不要") and has_any(".cs") and has_any("実装したい"):
        return make_prediction(["unity-csharp-edit"], "write_csharp_file", ["relative", "newText"])
    if has_any("クラス") and has_any("assets/") and has_any("作りたい", "新規作成"):
        return make_prediction(["unity-csharp-edit"], "create_class", ["name", "folder"])
    if has_any("ゲームパッド") and has_any("ボタン"):
        return make_prediction(["unity-playmode-testing"], "input_gamepad", ["action", "button", "buttonAction"])
    if has_any("play") and has_any("space") and has_any("スクリーンショット"):
        return make_prediction(["unity-playmode-testing"], "input_keyboard", ["action", "key"])
    if has_any("スクリーンショット") and has_any("撮りたい"):
        return make_prediction(["unity-playmode-testing"], "capture_screenshot", ["captureMode"])

    return None


def invoke_codex(prompt: str, model: str) -> dict[str, object]:
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=".json", delete=False) as schema_file:
        json.dump(SCHEMA, schema_file, ensure_ascii=False)
        schema_path = schema_file.name

    with tempfile.NamedTemporaryFile("w+", encoding="utf-8", suffix=".json", delete=False) as output_file:
        output_path = output_file.name

    try:
        cmd = [
            "codex",
            "exec",
            "-C",
            "/tmp",
            "-m",
            model,
            "-c",
            "model_reasoning_effort=\"low\"",
            "-s",
            "read-only",
            "--ephemeral",
            "--skip-git-repo-check",
            "--output-schema",
            schema_path,
            "-o",
            output_path,
            prompt,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, check=False)
        if result.returncode != 0:
            raise RuntimeError(result.stderr.strip() or result.stdout.strip() or "codex failed")

        structured = json.loads(Path(output_path).read_text(encoding="utf-8"))
        if not isinstance(structured, dict):
            raise RuntimeError("invalid JSON in Codex output")
        return structured
    finally:
        Path(schema_path).unlink(missing_ok=True)
        Path(output_path).unlink(missing_ok=True)


def main() -> int:
    args = parse_args()
    user_prompt = sys.stdin.read().strip()
    if not user_prompt:
        print("stdin prompt is required", file=sys.stderr)
        return 2

    keyword_prediction = route_by_keywords(user_prompt)
    if keyword_prediction is not None:
        print(json.dumps(keyword_prediction, ensure_ascii=False))
        return 0

    skills = load_skills(Path(args.skills_dir))
    catalog = format_skill_catalog(skills)
    prompt = build_prompt(catalog, user_prompt, load_tool_catalog(args.unity_cli))
    structured = invoke_codex(prompt, args.model)
    print(json.dumps(structured, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
