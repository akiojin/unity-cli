#!/usr/bin/env bash
# Validate SKILL.md command examples against unity-cli tool catalog and parameter contracts.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SKILLS_DIR="${REPO_ROOT}/.claude-plugin/plugins/unity-cli/skills"
CATALOG_FILE="${REPO_ROOT}/src/tool_catalog.rs"
REPORT_PATH="${REPO_ROOT}/specs/perf/skill-static-report.json"
JSON_OUTPUT=0

usage() {
  cat <<USAGE
Usage: scripts/skill-eval/static-skill-contract-check.sh [options]

Options:
  --skills-dir <path>   Skills directory (default: .claude-plugin/plugins/unity-cli/skills)
  --catalog <path>      Rust tool catalog file (default: src/tool_catalog.rs)
  --report <path>       JSON report path (default: specs/perf/skill-static-report.json)
  --json                Print JSON report to stdout
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skills-dir)
      SKILLS_DIR="$2"
      shift 2
      ;;
    --catalog)
      CATALOG_FILE="$2"
      shift 2
      ;;
    --report)
      REPORT_PATH="$2"
      shift 2
      ;;
    --json)
      JSON_OUTPUT=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ ! -d "${SKILLS_DIR}" ]]; then
  echo "ERROR: Skills directory not found: ${SKILLS_DIR}" >&2
  exit 1
fi

if [[ ! -f "${CATALOG_FILE}" ]]; then
  echo "ERROR: Tool catalog not found: ${CATALOG_FILE}" >&2
  exit 1
fi

python3 - "${SKILLS_DIR}" "${CATALOG_FILE}" "${REPORT_PATH}" "${JSON_OUTPUT}" <<'PY'
import datetime as dt
import json
import pathlib
import re
import sys

skills_dir = pathlib.Path(sys.argv[1])
catalog_file = pathlib.Path(sys.argv[2])
report_path = pathlib.Path(sys.argv[3])
json_output = sys.argv[4] == "1"

catalog_text = catalog_file.read_text(encoding="utf-8")
known_tools = set(re.findall(r'"([a-z0-9_]+)"', catalog_text))

contracts = {
    "addressables_manage": {
        "required": ["action"],
        "allowed": [
            "action",
            "groupName",
            "assetPath",
            "address",
            "labels",
            "label",
            "targetGroupName",
            "newAddress",
            "offset",
            "pageSize",
        ],
    },
    "addressables_build": {"required": ["action"], "allowed": ["action", "buildTarget"]},
    "addressables_analyze": {
        "required": ["action"],
        "allowed": ["action", "assetPath", "offset", "pageSize"],
    },
    "manage_asset_database": {
        "required": ["action"],
        "allowed": ["action", "assetPath", "filter", "folderPath", "fromPath", "toPath", "searchInFolders"],
    },
    "refresh_assets": {"required": [], "allowed": []},
    "create_material": {
        "required": ["materialPath"],
        "allowed": ["materialPath", "shader", "properties", "copyFrom", "overwrite"],
    },
    "modify_material": {
        "required": ["materialPath", "properties"],
        "allowed": ["materialPath", "properties", "shader"],
    },
    "manage_asset_import_settings": {
        "required": ["action", "assetPath"],
        "allowed": ["action", "assetPath", "preset", "settings"],
    },
    "analyze_asset_dependencies": {
        "required": ["action"],
        "allowed": ["action", "assetPath", "recursive", "includeBuiltIn"],
    },
    "build_index": {"required": [], "allowed": ["scope", "retry", "throttleMs"]},
    "update_index": {"required": ["paths"], "allowed": ["paths", "concurrency", "retry"]},
    "get_symbols": {"required": ["path"], "allowed": ["path"]},
    "find_symbol": {"required": ["name"], "allowed": ["name", "kind", "exact", "scope"]},
    "find_refs": {
        "required": ["name"],
        "allowed": [
            "name",
            "scope",
            "kind",
            "path",
            "namespace",
            "container",
            "pageSize",
            "startAfter",
            "maxBytes",
            "maxMatchesPerFile",
            "snippetContext",
        ],
    },
    "rename_symbol": {
        "required": ["relative", "namePath", "newName"],
        "allowed": ["relative", "namePath", "newName", "apply"],
    },
    "replace_symbol_body": {
        "required": ["relative", "namePath", "body"],
        "allowed": ["relative", "namePath", "body", "apply"],
    },
    "insert_before_symbol": {
        "required": ["relative", "namePath", "text"],
        "allowed": ["relative", "namePath", "text", "apply"],
    },
    "insert_after_symbol": {
        "required": ["relative", "namePath", "text"],
        "allowed": ["relative", "namePath", "text", "apply"],
    },
    "remove_symbol": {
        "required": ["relative", "namePath"],
        "allowed": ["relative", "namePath", "apply", "failOnReferences", "removeEmptyFile"],
    },
    "validate_text_edits": {
        "required": ["relative", "newText"],
        "allowed": ["relative", "newText"],
    },
    "create_class": {
        "required": ["name"],
        "allowed": ["name", "namespace", "inherits", "folder", "path"],
    },
    "read": {"required": ["path"], "allowed": ["path", "startLine", "maxLines"]},
    "search": {"required": ["pattern"], "allowed": ["pattern", "path", "limit"]},
    "list_packages": {"required": [], "allowed": []},
    "get_compilation_state": {"required": [], "allowed": ["includeMessages", "maxMessages"]},
    "get_editor_info": {"required": [], "allowed": []},
    "get_editor_state": {"required": [], "allowed": []},
    "get_command_stats": {"required": [], "allowed": []},
    "get_project_settings": {
        "required": [],
        "allowed": [
            "includePlayer",
            "includeGraphics",
            "includeQuality",
            "includePhysics",
            "includePhysics2D",
            "includeTime",
            "includeTags",
            "includeInputManager",
            "includeAudio",
            "includeEditor",
            "includeBuild",
        ],
    },
    "update_project_settings": {
        "required": ["confirmChanges"],
        "allowed": ["confirmChanges", "player", "graphics", "quality", "physics", "physics2D", "time", "audio"],
    },
    "execute_menu_item": {
        "required": ["menuPath"],
        "allowed": ["menuPath", "action", "alias", "parameters", "safetyCheck"],
    },
    "manage_windows": {
        "required": ["action"],
        "allowed": ["action", "windowType", "includeHidden"],
    },
    "manage_selection": {
        "required": ["action"],
        "allowed": ["action", "objectPaths", "includeDetails"],
    },
    "manage_tools": {"required": ["action"], "allowed": ["action", "category", "toolName"]},
    "quit_editor": {"required": [], "allowed": []},
    "read_console": {
        "required": [],
        "allowed": [
            "count",
            "filterText",
            "format",
            "groupBy",
            "includeStackTrace",
            "logTypes",
            "raw",
            "sinceTimestamp",
            "untilTimestamp",
            "sortOrder",
        ],
    },
    "clear_console": {
        "required": [],
        "allowed": [
            "clearOnBuild",
            "clearOnPlay",
            "clearOnRecompile",
            "preserveErrors",
            "preserveWarnings",
        ],
    },
    "clear_logs": {"required": [], "allowed": []},
    "profiler_start": {
        "required": [],
        "allowed": ["recordToFile", "maxDurationSec", "mode", "metrics"],
    },
    "profiler_stop": {"required": [], "allowed": ["sessionId"]},
    "profiler_status": {"required": [], "allowed": []},
    "profiler_get_metrics": {"required": [], "allowed": ["listAvailable", "metrics"]},
    "package_manager": {
        "required": ["action"],
        "allowed": ["action", "keyword", "category", "limit", "packageId", "packageName", "version", "includeBuiltIn"],
    },
    "registry_config": {
        "required": ["action"],
        "allowed": ["action", "registry", "registryName", "scope", "scopes", "autoAddPopular"],
    },
    "modify_gameobject": {
        "required": ["path"],
        "allowed": ["path", "name", "position", "rotation", "scale", "active", "tag", "layer", "parentPath"],
    },
    "delete_gameobject": {"required": [], "allowed": ["path", "paths", "includeChildren"]},
    "modify_component": {
        "required": ["gameObjectPath", "componentType", "properties"],
        "allowed": ["gameObjectPath", "componentType", "properties", "componentIndex"],
    },
    "set_component_field": {
        "required": ["componentType", "fieldPath"],
        "allowed": [
            "componentType",
            "fieldPath",
            "gameObjectPath",
            "prefabAssetPath",
            "prefabObjectPath",
            "componentIndex",
            "value",
            "valueType",
            "enumValue",
            "objectReference",
            "scope",
            "runtime",
            "dryRun",
            "createUndo",
            "markSceneDirty",
            "applyPrefabChanges",
        ],
    },
    "remove_component": {
        "required": ["gameObjectPath", "componentType"],
        "allowed": ["gameObjectPath", "componentType", "componentIndex"],
    },
    "list_components": {"required": ["gameObjectPath"], "allowed": ["gameObjectPath", "includeInherited"]},
    "get_component_types": {"required": [], "allowed": ["category", "search", "onlyAddable"]},
    "manage_tags": {"required": ["action"], "allowed": ["action", "tagName"]},
    "manage_layers": {"required": ["action"], "allowed": ["action", "layerName", "layerIndex"]},
    "create_action_map": {
        "required": ["assetPath", "mapName"],
        "allowed": ["assetPath", "mapName", "actions"],
    },
    "remove_action_map": {"required": ["assetPath", "mapName"], "allowed": ["assetPath", "mapName"]},
    "add_input_action": {
        "required": ["assetPath", "mapName", "actionName"],
        "allowed": ["assetPath", "mapName", "actionName", "actionType"],
    },
    "remove_input_action": {
        "required": ["assetPath", "mapName", "actionName"],
        "allowed": ["assetPath", "mapName", "actionName"],
    },
    "add_input_binding": {
        "required": ["assetPath", "mapName", "actionName", "path"],
        "allowed": ["assetPath", "mapName", "actionName", "path", "groups", "processors", "interactions"],
    },
    "create_composite_binding": {
        "required": ["assetPath", "mapName", "actionName", "bindings"],
        "allowed": ["assetPath", "mapName", "actionName", "bindings", "compositeType", "groups", "name"],
    },
    "remove_input_binding": {
        "required": ["assetPath", "mapName", "actionName"],
        "allowed": ["assetPath", "mapName", "actionName", "bindingIndex", "bindingPath"],
    },
    "remove_all_bindings": {
        "required": ["assetPath", "mapName", "actionName"],
        "allowed": ["assetPath", "mapName", "actionName"],
    },
    "analyze_input_actions_asset": {
        "required": ["assetPath"],
        "allowed": ["assetPath", "includeStatistics", "includeJsonStructure"],
    },
    "get_input_actions_state": {
        "required": [],
        "allowed": ["assetName", "assetPath", "includeBindings", "includeControlSchemes", "includeJsonStructure"],
    },
    "manage_control_schemes": {
        "required": ["assetPath", "operation"],
        "allowed": ["assetPath", "operation", "schemeName", "devices"],
    },
    "play_game": {"required": [], "allowed": ["delayMs"]},
    "pause_game": {"required": [], "allowed": []},
    "stop_game": {"required": [], "allowed": []},
    "input_keyboard": {
        "required": ["action"],
        "allowed": ["action", "key", "keys", "text", "typingSpeed", "holdSeconds", "actions"],
    },
    "input_mouse": {
        "required": ["action"],
        "allowed": [
            "action",
            "x",
            "y",
            "button",
            "clickCount",
            "deltaX",
            "deltaY",
            "startX",
            "startY",
            "endX",
            "endY",
            "buttonAction",
            "absolute",
            "holdSeconds",
            "actions",
        ],
    },
    "input_gamepad": {
        "required": ["action"],
        "allowed": [
            "action",
            "button",
            "buttonAction",
            "stick",
            "trigger",
            "direction",
            "x",
            "y",
            "value",
            "holdSeconds",
            "actions",
        ],
    },
    "input_touch": {
        "required": ["action"],
        "allowed": [
            "action",
            "x",
            "y",
            "startX",
            "startY",
            "endX",
            "endY",
            "centerX",
            "centerY",
            "startDistance",
            "endDistance",
            "duration",
            "touchId",
            "touches",
            "actions",
        ],
    },
    "create_input_sequence": {"required": ["sequence"], "allowed": ["sequence", "delayBetween"]},
    "get_current_input_state": {"required": [], "allowed": []},
    "capture_screenshot": {
        "required": [],
        "allowed": ["captureMode", "windowName", "width", "height", "includeUI", "encodeAsBase64", "explorerSettings"],
    },
    "analyze_screenshot": {
        "required": [],
        "allowed": ["imagePath", "base64Data", "analysisType", "prompt"],
    },
    "capture_video_start": {
        "required": [],
        "allowed": ["captureMode", "fps", "maxDurationSec", "width", "height"],
    },
    "capture_video_stop": {"required": [], "allowed": ["recordingId"]},
    "capture_video_status": {"required": [], "allowed": []},
    "run_tests": {
        "required": [],
        "allowed": ["testMode", "filter", "namespace", "category", "includeDetails", "exportPath"],
    },
    "get_test_status": {"required": [], "allowed": ["includeTestResults", "includeFileContent"]},
    "create_prefab": {
        "required": ["prefabPath"],
        "allowed": ["prefabPath", "gameObjectPath", "createFromTemplate", "overwrite"],
    },
    "open_prefab": {
        "required": ["prefabPath"],
        "allowed": ["prefabPath", "focusObject", "isolateObject"],
    },
    "save_prefab": {"required": [], "allowed": ["gameObjectPath", "includeChildren"]},
    "exit_prefab_mode": {"required": [], "allowed": ["saveChanges"]},
    "instantiate_prefab": {
        "required": ["prefabPath"],
        "allowed": ["prefabPath", "position", "rotation", "parent", "name"],
    },
    "modify_prefab": {
        "required": ["prefabPath", "modifications"],
        "allowed": ["prefabPath", "modifications", "applyToInstances"],
    },
    "create_scene": {
        "required": ["sceneName"],
        "allowed": ["sceneName", "path", "loadScene", "addToBuildSettings"],
    },
    "load_scene": {"required": [], "allowed": ["scenePath", "sceneName", "loadMode"]},
    "save_scene": {"required": [], "allowed": ["scenePath", "saveAs"]},
    "create_gameobject": {
        "required": [],
        "allowed": ["name", "primitiveType", "parentPath", "position", "rotation", "scale", "tag", "layer"],
    },
    "add_component": {
        "required": ["gameObjectPath", "componentType"],
        "allowed": ["gameObjectPath", "componentType", "properties"],
    },
    "get_hierarchy": {
        "required": [],
        "allowed": [
            "rootPath",
            "maxDepth",
            "includeInactive",
            "includeComponents",
            "includeTransform",
            "includeTags",
            "includeLayers",
            "nameOnly",
            "maxObjects",
        ],
    },
    "get_scene_info": {"required": [], "allowed": ["scenePath", "sceneName", "includeGameObjects"]},
    "list_scenes": {"required": [], "allowed": ["includeLoadedOnly", "includeBuildScenesOnly", "includePath"]},
    "find_gameobject": {"required": [], "allowed": ["name", "tag", "layer", "exactMatch"]},
    "find_by_component": {
        "required": ["componentType"],
        "allowed": ["componentType", "searchScope", "includeInactive", "matchExactType"],
    },
    "get_gameobject_details": {
        "required": [],
        "allowed": ["gameObjectName", "path", "includeChildren", "includeComponents", "includeMaterials", "maxDepth"],
    },
    "get_component_values": {
        "required": ["gameObjectName", "componentType"],
        "allowed": ["gameObjectName", "componentType", "componentIndex", "includePrivateFields", "includeInherited"],
    },
    "get_object_references": {
        "required": ["gameObjectName"],
        "allowed": ["gameObjectName", "includeAssetReferences", "includeHierarchyReferences", "searchInPrefabs"],
    },
    "analyze_scene_contents": {
        "required": [],
        "allowed": ["includeInactive", "includePrefabInfo", "includeMemoryInfo", "groupByType"],
    },
    "get_animator_state": {
        "required": ["gameObjectName"],
        "allowed": ["gameObjectName", "layerIndex", "includeParameters", "includeStates", "includeTransitions", "includeClips"],
    },
    "get_animator_runtime_info": {
        "required": ["gameObjectName"],
        "allowed": ["gameObjectName", "includeIK", "includeRootMotion", "includeBehaviours"],
    },
    "find_ui_elements": {
        "required": [],
        "allowed": ["elementType", "tagFilter", "namePattern", "includeInactive", "canvasFilter", "uiDocumentFilter", "uiSystem"],
    },
    "get_ui_element_state": {
        "required": ["elementPath"],
        "allowed": ["elementPath", "includeChildren", "includeInteractableInfo"],
    },
    "click_ui_element": {
        "required": ["elementPath"],
        "allowed": ["elementPath", "clickType", "holdDuration", "position"],
    },
    "set_ui_element_value": {
        "required": ["elementPath", "value"],
        "allowed": ["elementPath", "value", "triggerEvents"],
    },
    "simulate_ui_input": {
        "required": [],
        "allowed": ["elementPath", "inputType", "inputData", "inputSequence", "waitBetween", "validateState"],
    },
}

cmd_re = re.compile(
    r"^\s*unity-cli\s+(?:raw|tool\s+call)\s+([a-z0-9_]+)\s+--json\s+([\"'])(.+?)\2\s*$"
)

errors = []
entries = []
repo_root = skills_dir.parent.parent.parent.parent

for skill_file in sorted(skills_dir.glob("*/SKILL.md")):
    text = skill_file.read_text(encoding="utf-8")
    lines = text.splitlines()
    for idx, line in enumerate(lines, start=1):
        m = cmd_re.match(line)
        if not m:
            continue

        tool = m.group(1)
        payload_raw = m.group(3)
        try:
            payload = json.loads(payload_raw)
        except Exception as exc:
            errors.append(
                {
                    "type": "invalid_json",
                    "skill": str(skill_file.relative_to(repo_root)),
                    "line": idx,
                    "tool": tool,
                    "message": f"Invalid JSON payload: {exc}",
                }
            )
            continue

        if not isinstance(payload, dict):
            errors.append(
                {
                    "type": "invalid_payload_type",
                    "skill": str(skill_file.relative_to(repo_root)),
                    "line": idx,
                    "tool": tool,
                    "message": "Payload must be a JSON object",
                }
            )
            continue

        entry = {
            "skill": str(skill_file.relative_to(repo_root)),
            "line": idx,
            "tool": tool,
            "payloadKeys": sorted(payload.keys()),
        }
        entries.append(entry)

        if tool not in known_tools:
            errors.append(
                {
                    "type": "unknown_tool",
                    "skill": entry["skill"],
                    "line": idx,
                    "tool": tool,
                    "message": "Tool not found in src/tool_catalog.rs",
                }
            )
            continue

        contract = contracts.get(tool)
        if contract is None:
            errors.append(
                {
                    "type": "missing_contract",
                    "skill": entry["skill"],
                    "line": idx,
                    "tool": tool,
                    "message": "No contract defined for this tool in static checker",
                }
            )
            continue

        required = set(contract["required"])
        allowed = set(contract["allowed"])
        keys = set(payload.keys())

        missing = sorted(required - keys)
        if missing:
            errors.append(
                {
                    "type": "missing_required_keys",
                    "skill": entry["skill"],
                    "line": idx,
                    "tool": tool,
                    "missing": missing,
                    "message": f"Missing required keys: {', '.join(missing)}",
                }
            )

        extra = sorted(keys - allowed)
        if extra:
            errors.append(
                {
                    "type": "unknown_payload_keys",
                    "skill": entry["skill"],
                    "line": idx,
                    "tool": tool,
                    "unknown": extra,
                    "message": f"Unknown keys for {tool}: {', '.join(extra)}",
                }
            )

report = {
    "timestamp": dt.datetime.now(dt.timezone.utc).isoformat(),
    "skillsDir": str(skills_dir),
    "catalogFile": str(catalog_file),
    "commandsScanned": len(entries),
    "errorsCount": len(errors),
    "passed": len(errors) == 0,
    "errors": errors,
}

report_path.parent.mkdir(parents=True, exist_ok=True)
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

if json_output:
    print(json.dumps(report, ensure_ascii=False, indent=2))
else:
    status = "PASS" if report["passed"] else "FAIL"
    print(f"[{status}] static skill contract check")
    print(f"  commands scanned: {report['commandsScanned']}")
    print(f"  errors: {report['errorsCount']}")
    print(f"  report: {report_path}")
    if errors:
        print("  first errors:")
        for item in errors[:10]:
            print(f"    - {item['skill']}:{item['line']} {item['tool']} -> {item['message']}")

sys.exit(0 if report["passed"] else 1)
PY
