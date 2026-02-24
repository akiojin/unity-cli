#!/usr/bin/env bash
# scripts/e2e-all-tools.sh
# Full E2E runner that executes every tool returned by `unity-cli tool list`.

set -u -o pipefail

HOST="127.0.0.1"
PORT="6400"
TIMEOUT_MS="120000"
SKIP_QUIT=0
SKIP_LSP_PERF=0
LSP_PERF_RUNS=5
UNITY_CLI=""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"

RUN_ID="$(date +%Y%m%d-%H%M%S)"
LOG="/tmp/unity-cli-e2e-all-tools-${RUN_ID}.log"

GEN_DIR="Assets/Scenes/Generated/E2E"
SCENE_DIR="${GEN_DIR}/"
SCENE_NAME="E2E_AllTools_${RUN_ID}"
SCENE_PATH="${SCENE_DIR}${SCENE_NAME}.unity"
UI_SCENE_PATH="Assets/Scenes/Generated/UI/UnityCli_UI_UGUI_TestScene.unity"
MATERIAL_PATH="${GEN_DIR}/Materials/E2E_AllTools_${RUN_ID}.mat"
PREFAB_PATH="${GEN_DIR}/Prefabs/E2E_AllTools_${RUN_ID}.prefab"
INPUT_ACTIONS_COPY="${GEN_DIR}/E2E_InputActions_${RUN_ID}.inputactions"
ACTION_MAP_NAME="E2E_Map_${RUN_ID}"
CONTROL_SCHEME_NAME="E2E_Scheme_${RUN_ID}"

PASSED=0
FAILED=0
LAST_OUTPUT=""

TOOL_LIST=()
CALLED_TOOLS=()
PASSED_TOOLS=()
FAILED_TOOLS=()
FAIL_LINES=()

array_contains() {
  local arr_name="$1"
  local needle="$2"
  local item
  local rc=1
  set +u
  eval "for item in \"\${${arr_name}[@]}\"; do
    if [[ \"\$item\" == \"\$needle\" ]]; then
      rc=0
      break
    fi
  done"
  set -u
  return "${rc}"
}

array_add_unique() {
  local arr_name="$1"
  local value="$2"
  if ! array_contains "${arr_name}" "${value}"; then
    set +u
    eval "${arr_name}+=(\"\$value\")"
    set -u
  fi
}

usage() {
  cat <<EOF
Usage: scripts/e2e-all-tools.sh [options]

Options:
  --host <host>          Unity host (default: 127.0.0.1)
  --port <port>          Unity port (default: 6400)
  --timeout-ms <ms>      Per-command timeout in ms (default: 120000)
  --unity-cli <path>     unity-cli binary path
  --lsp-perf-runs <n>    LSP perf measurement runs (default: 5)
  --skip-lsp-perf        Skip LSP performance check
  --skip-quit            Do not execute quit_editor
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host)
      HOST="$2"
      shift 2
      ;;
    --port)
      PORT="$2"
      shift 2
      ;;
    --timeout-ms)
      TIMEOUT_MS="$2"
      shift 2
      ;;
    --unity-cli)
      UNITY_CLI="$2"
      shift 2
      ;;
    --lsp-perf-runs)
      LSP_PERF_RUNS="$2"
      shift 2
      ;;
    --skip-lsp-perf)
      SKIP_LSP_PERF=1
      shift
      ;;
    --skip-quit)
      SKIP_QUIT=1
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

if [[ -z "${UNITY_CLI}" ]]; then
  if [[ -x "${REPO_ROOT}/target/release/unity-cli" ]]; then
    UNITY_CLI="${REPO_ROOT}/target/release/unity-cli"
  else
    UNITY_CLI="$(command -v unity-cli 2>/dev/null || true)"
  fi
fi

if [[ -z "${UNITY_CLI}" || ! -x "${UNITY_CLI}" ]]; then
  echo "ERROR: unity-cli not found. Build with 'cargo build --release' or install from crates.io." >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "ERROR: jq is required for this script." >&2
  exit 1
fi

if ! [[ "${LSP_PERF_RUNS}" =~ ^[0-9]+$ ]] || [[ "${LSP_PERF_RUNS}" -lt 1 ]]; then
  echo "ERROR: --lsp-perf-runs must be a positive integer." >&2
  exit 1
fi

if [[ ! -d "${PROJECT_ROOT}/Assets" || ! -d "${PROJECT_ROOT}/Packages" ]]; then
  echo "ERROR: Unity project not found at ${PROJECT_ROOT}" >&2
  exit 1
fi

export UNITY_PROJECT_ROOT="${PROJECT_ROOT}"

mkdir -p "${PROJECT_ROOT}/${GEN_DIR}/Materials" "${PROJECT_ROOT}/${GEN_DIR}/Prefabs"

echo "E2E All-Tools Test Suite" | tee "${LOG}"
echo "  Run ID: ${RUN_ID}" | tee -a "${LOG}"
echo "  Host: ${HOST}" | tee -a "${LOG}"
echo "  Port: ${PORT}" | tee -a "${LOG}"
echo "  CLI: ${UNITY_CLI}" | tee -a "${LOG}"
echo "  UNITY_PROJECT_ROOT: ${UNITY_PROJECT_ROOT}" | tee -a "${LOG}"
echo "  Log: ${LOG}" | tee -a "${LOG}"
echo "" | tee -a "${LOG}"

invoke_tool() {
  local tool="$1"
  local payload="$2"
  "${UNITY_CLI}" tool call "${tool}" \
    --json "${payload}" \
    --host "${HOST}" \
    --port "${PORT}" \
    --timeout-ms "${TIMEOUT_MS}" \
    --output json
}

record_failure() {
  local key="$1"
  local reason="$2"
  FAILED=$((FAILED + 1))
  array_add_unique "FAILED_TOOLS" "${key}"
  FAIL_LINES+=("${key}: ${reason}")
}

run_tool() {
  local tool="$1"
  local payload="${2-}"
  local output rc failure_reason
  rc=0
  failure_reason=""

  if [[ -z "${payload}" ]]; then
    payload='{}'
  fi

  array_add_unique "CALLED_TOOLS" "${tool}"

  printf "  %-30s ... " "${tool}"
  output="$(invoke_tool "${tool}" "${payload}" 2>&1)" || rc=$?
  LAST_OUTPUT="${output}"

  {
    echo "===== ${tool} ====="
    echo "payload: ${payload}"
    echo "${output}"
    echo
  } >> "${LOG}"

  if [[ ${rc} -ne 0 ]]; then
    failure_reason="exit code ${rc}"
  elif ! jq -e . >/dev/null 2>&1 <<<"${output}"; then
    failure_reason="non-JSON response"
  elif jq -e 'type=="object" and ((has("error") and .error != null and (.error|tostring|length)>0) or (has("success") and .success == false) or (has("status") and ((.status|tostring|ascii_downcase)=="error")))' >/dev/null 2>&1 <<<"${output}"; then
    failure_reason="$(jq -r '.error // .message // .status // "unknown error"' <<<"${output}" 2>/dev/null)"
  fi

  if [[ -n "${failure_reason}" ]]; then
    echo "FAIL"
    record_failure "${tool}" "${failure_reason}"
  else
    echo "PASS"
    PASSED=$((PASSED + 1))
    array_add_unique "PASSED_TOOLS" "${tool}"
  fi
}

query_is_playing() {
  local output
  output="$(invoke_tool "get_editor_state" "{}" 2>/dev/null || true)"
  jq -r '.state.isPlaying // false' <<<"${output}" 2>/dev/null || echo "false"
}

wait_for_play_state() {
  local target="$1"
  local max_tries=120
  local i state
  for ((i = 1; i <= max_tries; i++)); do
    state="$(query_is_playing)"
    if [[ "${state}" == "${target}" ]]; then
      return 0
    fi
    sleep 0.25
  done
  return 1
}

if ! "${UNITY_CLI}" system ping --host "${HOST}" --port "${PORT}" --timeout-ms "${TIMEOUT_MS}" --output json >/dev/null 2>&1; then
  echo "ERROR: Unity connection failed (system ping)." >&2
  exit 1
fi

if [[ ${SKIP_LSP_PERF} -eq 0 ]]; then
  echo "--- LSP Performance Check ---" | tee -a "${LOG}"
  if "${REPO_ROOT}/scripts/lsp-perf-check.sh" \
    --unity-cli "${UNITY_CLI}" \
    --project-root "${PROJECT_ROOT}" \
    --runs "${LSP_PERF_RUNS}" >> "${LOG}" 2>&1; then
    echo "  lsp_performance_check           ... PASS"
  else
    echo "  lsp_performance_check           ... FAIL"
    record_failure "lsp_performance_check" "See ${LOG} for details"
  fi
  echo "" | tee -a "${LOG}"
fi

tool_list_raw="$("${UNITY_CLI}" tool list --host "${HOST}" --port "${PORT}" --timeout-ms "${TIMEOUT_MS}" --output json | jq -r '.[]')"
while IFS= read -r line; do
  [[ -z "${line}" ]] && continue
  TOOL_LIST+=("${line}")
done <<< "${tool_list_raw}"
if [[ ${#TOOL_LIST[@]} -eq 0 ]]; then
  echo "ERROR: Failed to read tool list from Unity." >&2
  exit 1
fi

echo "Detected ${#TOOL_LIST[@]} tools." | tee -a "${LOG}"
echo "" | tee -a "${LOG}"

json_create_scene="$(jq -nc --arg sceneName "${SCENE_NAME}" --arg path "${SCENE_DIR}" '{sceneName:$sceneName,path:$path}')"
json_load_scene_e2e="$(jq -nc --arg scenePath "${SCENE_PATH}" '{scenePath:$scenePath,loadMode:"Single"}')"
json_copy_input_actions="$(jq -nc --arg fromPath "Assets/InputSystem_Actions.inputactions" --arg toPath "${INPUT_ACTIONS_COPY}" '{action:"copy_asset",fromPath:$fromPath,toPath:$toPath}')"
json_create_material="$(jq -nc --arg materialPath "${MATERIAL_PATH}" '{materialPath:$materialPath,shader:"Standard",overwrite:true}')"
json_modify_material="$(jq -nc --arg materialPath "${MATERIAL_PATH}" '{materialPath:$materialPath,properties:{_Color:[1,0,0,1]}}')"
json_asset_import_get="$(jq -nc --arg assetPath "${MATERIAL_PATH}" '{action:"get",assetPath:$assetPath}')"
json_create_prefab="$(jq -nc --arg prefabPath "${PREFAB_PATH}" '{gameObjectPath:"/E2ECube",prefabPath:$prefabPath,overwrite:true}')"
json_instantiate_prefab="$(jq -nc --arg prefabPath "${PREFAB_PATH}" '{prefabPath:$prefabPath,name:"E2EInstance"}')"
json_modify_prefab="$(jq -nc --arg prefabPath "${PREFAB_PATH}" '{prefabPath:$prefabPath,modifications:{name:"E2EPrefabRoot"},applyToInstances:true}')"
json_open_prefab="$(jq -nc --arg prefabPath "${PREFAB_PATH}" '{prefabPath:$prefabPath}')"
json_input_asset_base="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" '{assetPath:$assetPath}')"
json_create_action_map="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName}')"
json_add_input_action="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName,actionName:"Jump",actionType:"Button"}')"
json_add_input_binding="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName,actionName:"Jump",path:"<Keyboard>/space"}')"
json_create_composite_binding="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName,actionName:"Jump",bindings:{up:"<Keyboard>/w",down:"<Keyboard>/s",left:"<Keyboard>/a",right:"<Keyboard>/d"}}')"
json_remove_input_binding="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName,actionName:"Jump",bindingIndex:0}')"
json_remove_all_bindings="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName,actionName:"Jump"}')"
json_remove_input_action="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName,actionName:"Jump"}')"
json_manage_control_schemes="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg schemeName "${CONTROL_SCHEME_NAME}" '{assetPath:$assetPath,operation:"add",schemeName:$schemeName,devices:["Keyboard","Mouse"]}')"
json_remove_action_map="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" --arg mapName "${ACTION_MAP_NAME}" '{assetPath:$assetPath,mapName:$mapName}')"
json_analyze_input_asset="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" '{assetPath:$assetPath}')"
json_get_input_state="$(jq -nc --arg assetPath "${INPUT_ACTIONS_COPY}" '{assetPath:$assetPath,includeBindings:true,includeControlSchemes:true}')"
json_update_index="$(jq -nc '{paths:["Assets/Scripts/ButtonHandler.cs"]}')"
json_find_symbol="$(jq -nc '{name:"ButtonHandler",kind:"class",exact:true,scope:"assets"}')"
json_get_symbols="$(jq -nc '{path:"Assets/Scripts/ButtonHandler.cs"}')"
json_find_refs="$(jq -nc '{name:"ButtonHandler",scope:"assets",pageSize:20}')"
json_read="$(jq -nc '{path:"Assets/Scripts/ButtonHandler.cs",startLine:1,maxLines:30}')"
json_search="$(jq -nc '{pattern:"ButtonHandler",path:"Assets/Scripts",limit:20}')"
json_load_ui_scene="$(jq -nc --arg scenePath "${UI_SCENE_PATH}" '{scenePath:$scenePath,loadMode:"Single"}')"

echo "--- Running tools ---"

run_tool "create_scene" "${json_create_scene}"
run_tool "load_scene" "${json_load_scene_e2e}"
run_tool "create_gameobject" '{"name":"E2ECube","primitiveType":"Cube"}'
run_tool "add_component" '{"gameObjectPath":"/E2ECube","componentType":"Animator"}'
run_tool "add_component" '{"gameObjectPath":"/E2ECube","componentType":"Rigidbody"}'
run_tool "modify_component" '{"gameObjectPath":"/E2ECube","componentType":"Rigidbody","properties":{"mass":1.75}}'
run_tool "set_component_field" '{"gameObjectPath":"/E2ECube","componentType":"Rigidbody","fieldPath":"m_Mass","value":2.5}'
run_tool "get_component_types" '{}'
run_tool "list_components" '{"gameObjectPath":"/E2ECube"}'
run_tool "get_component_values" '{"gameObjectName":"E2ECube","componentType":"Transform"}'
run_tool "find_by_component" '{"componentType":"Animator"}'
run_tool "get_gameobject_details" '{"gameObjectName":"E2ECube"}'
run_tool "get_object_references" '{"gameObjectName":"E2ECube"}'
run_tool "analyze_scene_contents" '{}'
run_tool "get_animator_state" '{"gameObjectName":"E2ECube"}'
run_tool "play_game" '{"delayMs":100}'
if ! wait_for_play_state "true"; then
  record_failure "play_game" "Play mode did not become true within timeout (animator runtime phase)"
fi
run_tool "get_animator_runtime_info" '{"gameObjectName":"E2ECube"}'
run_tool "stop_game" '{}'
if ! wait_for_play_state "false"; then
  record_failure "stop_game" "Play mode did not become false within timeout (animator runtime phase)"
fi
run_tool "find_gameobject" '{"name":"E2ECube","exactMatch":true}'
run_tool "get_hierarchy" '{"nameOnly":true,"maxObjects":200}'
run_tool "modify_gameobject" '{"path":"/E2ECube","position":{"x":1,"y":1,"z":1}}'
run_tool "create_material" "${json_create_material}"
run_tool "modify_material" "${json_modify_material}"
run_tool "manage_asset_import_settings" "${json_asset_import_get}"
run_tool "create_prefab" "${json_create_prefab}"
run_tool "instantiate_prefab" "${json_instantiate_prefab}"
run_tool "modify_prefab" "${json_modify_prefab}"
run_tool "open_prefab" "${json_open_prefab}"
run_tool "save_prefab" '{}'
run_tool "exit_prefab_mode" '{"saveChanges":true}'
run_tool "remove_component" '{"gameObjectPath":"/E2ECube","componentType":"Rigidbody"}'
run_tool "delete_gameobject" '{"path":"/E2EInstance"}'
run_tool "manage_asset_database" "${json_copy_input_actions}"
run_tool "refresh_assets" '{}'
run_tool "create_action_map" "${json_create_action_map}"
run_tool "add_input_action" "${json_add_input_action}"
run_tool "add_input_binding" "${json_add_input_binding}"
run_tool "create_composite_binding" "${json_create_composite_binding}"
run_tool "remove_input_binding" "${json_remove_input_binding}"
run_tool "remove_all_bindings" "${json_remove_all_bindings}"
run_tool "remove_input_action" "${json_remove_input_action}"
run_tool "manage_control_schemes" "${json_manage_control_schemes}"
run_tool "analyze_input_actions_asset" "${json_analyze_input_asset}"
run_tool "get_input_actions_state" "${json_get_input_state}"
run_tool "remove_action_map" "${json_remove_action_map}"
run_tool "analyze_asset_dependencies" '{"action":"analyze_circular"}'
run_tool "addressables_manage" '{"action":"list_groups"}'
run_tool "addressables_build" '{"action":"clean_build"}'
run_tool "addressables_analyze" '{"action":"analyze_unused"}'
run_tool "execute_menu_item" '{"action":"execute","menuPath":"Tools/Unity CLI/UI Tests/Generate UGUI Test Scene","safetyCheck":true}'
run_tool "load_scene" "${json_load_ui_scene}"
run_tool "list_scenes" '{"includeLoadedOnly":true}'
run_tool "get_scene_info" '{}'
run_tool "save_scene" '{}'
run_tool "clear_logs" '{}'
run_tool "clear_console" '{}'
run_tool "read_console" '{"count":20}'
run_tool "manage_layers" '{"action":"get"}'
run_tool "manage_tags" '{"action":"get"}'
run_tool "manage_selection" '{"action":"get"}'
run_tool "manage_tools" '{"action":"get"}'
run_tool "manage_windows" '{"action":"get"}'
run_tool "package_manager" '{"action":"list","includeBuiltIn":false}'
run_tool "registry_config" '{"action":"list"}'
run_tool "get_editor_info" '{}'
run_tool "get_project_settings" '{"includePlayer":true}'
run_tool "update_project_settings" '{"confirmChanges":true}'
run_tool "get_compilation_state" '{"includeMessages":false}'
run_tool "build_index" '{"scope":"assets"}'
run_tool "update_index" "${json_update_index}"
run_tool "find_symbol" "${json_find_symbol}"
run_tool "get_symbols" "${json_get_symbols}"
run_tool "find_refs" "${json_find_refs}"
run_tool "read" "${json_read}"
run_tool "search" "${json_search}"
run_tool "list_packages" '{}'
run_tool "get_command_stats" '{}'
run_tool "ping" '{"message":"e2e-all-tools"}'
run_tool "get_editor_state" '{}'
run_tool "profiler_get_metrics" '{"listAvailable":true}'
run_tool "profiler_start" '{"recordToFile":false,"maxDurationSec":0}'
sleep 1
run_tool "profiler_status" '{}'
run_tool "profiler_stop" '{}'
run_tool "run_tests" '{"testMode":"EditMode","filter":"InputSystemHandlerTests"}'
run_tool "get_test_status" '{"includeTestResults":false}'
run_tool "execute_menu_item" '{"action":"execute","menuPath":"Tools/Unity CLI/UI Tests/Generate UGUI Test Scene","safetyCheck":true}'
run_tool "load_scene" "${json_load_ui_scene}"

run_tool "play_game" '{"delayMs":100}'
if ! wait_for_play_state "true"; then
  record_failure "play_game" "Play mode did not become true within timeout"
fi

run_tool "input_keyboard" '{"action":"press","key":"space"}'
run_tool "input_mouse" '{"action":"move","x":120,"y":120}'
run_tool "input_gamepad" '{"action":"button","button":"a","buttonAction":"press"}'
run_tool "input_touch" '{"action":"tap","x":120,"y":120}'
run_tool "create_input_sequence" '{"sequence":[{"type":"keyboard","params":{"action":"press","key":"enter"}}],"delayBetween":50}'
run_tool "get_current_input_state" '{}'
run_tool "find_ui_elements" '{"includeInactive":true}'
UI_BUTTON_PATH="$(jq -r '([.elements[]? | select(.elementType=="Button" and .name=="UGUI_Button") | .path] + [.elements[]? | select(.elementType=="Button") | .path])[0] // empty' <<<"${LAST_OUTPUT}" 2>/dev/null || true)"
UI_TOGGLE_PATH="$(jq -r '([.elements[]? | select(.elementType=="Toggle" and .name=="UGUI_Toggle") | .path] + [.elements[]? | select(.elementType=="Toggle") | .path])[0] // empty' <<<"${LAST_OUTPUT}" 2>/dev/null || true)"
if [[ -z "${UI_BUTTON_PATH}" ]]; then
  UI_BUTTON_PATH="/Canvas/UGUI_Panel/UGUI_Button"
fi
if [[ -z "${UI_TOGGLE_PATH}" ]]; then
  UI_TOGGLE_PATH="/Canvas/UGUI_Panel/UGUI_Toggle"
fi
run_tool "get_ui_element_state" "$(jq -nc --arg elementPath "${UI_BUTTON_PATH}" '{elementPath:$elementPath}')"
run_tool "click_ui_element" "$(jq -nc --arg elementPath "${UI_BUTTON_PATH}" '{elementPath:$elementPath,clickType:"left"}')"
run_tool "set_ui_element_value" "$(jq -nc --arg elementPath "${UI_TOGGLE_PATH}" '{elementPath:$elementPath,value:true}')"
run_tool "simulate_ui_input" "$(jq -nc --arg elementPath "${UI_BUTTON_PATH}" '{inputSequence:[{type:"click",params:{elementPath:$elementPath}}],waitBetween:50}')"
run_tool "capture_video_start" '{"captureMode":"game","fps":10,"maxDurationSec":5}'
sleep 1
run_tool "capture_video_status" '{}'
run_tool "capture_video_stop" '{}'
run_tool "pause_game" '{}'
run_tool "pause_game" '{}'
run_tool "stop_game" '{}'
if ! wait_for_play_state "false"; then
  record_failure "stop_game" "Play mode did not become false within timeout"
fi

run_tool "capture_screenshot" '{"captureMode":"scene"}'
SCREENSHOT_PATH="$(jq -r '.path // empty' <<<"${LAST_OUTPUT}" 2>/dev/null || true)"
if [[ -n "${SCREENSHOT_PATH}" ]]; then
  run_tool "analyze_screenshot" "$(jq -nc --arg imagePath "${SCREENSHOT_PATH}" '{imagePath:$imagePath,analysisType:"basic"}')"
else
  record_failure "analyze_screenshot" "capture_screenshot did not return path"
fi

if [[ ${SKIP_QUIT} -eq 0 ]]; then
  run_tool "quit_editor" '{}'
else
  echo "  quit_editor                     ... SKIP"
fi

MISSING_TOOLS=()
for tool_name in "${TOOL_LIST[@]}"; do
  if [[ ${SKIP_QUIT} -eq 1 && "${tool_name}" == "quit_editor" ]]; then
    continue
  fi
  if ! array_contains "CALLED_TOOLS" "${tool_name}"; then
    MISSING_TOOLS+=("${tool_name}")
  fi
done

if [[ ${#MISSING_TOOLS[@]} -gt 0 ]]; then
  for tool_name in "${MISSING_TOOLS[@]}"; do
    record_failure "${tool_name}" "tool was not invoked"
  done
fi

echo ""
echo "--- Summary ---"
echo "  Invocations PASSED: ${PASSED}"
echo "  Invocations FAILED: ${FAILED}"
echo "  Unique tools expected: ${#TOOL_LIST[@]}"
echo "  Unique tools called:   ${#CALLED_TOOLS[@]}"
echo "  Unique tools passed:   ${#PASSED_TOOLS[@]}"
echo "  Unique tools failed:   ${#FAILED_TOOLS[@]}"

if [[ ${#FAIL_LINES[@]} -gt 0 ]]; then
  echo ""
  echo "Failures:"
  for line in "${FAIL_LINES[@]}"; do
    echo "  - ${line}"
  done
fi

echo ""
echo "Log file: ${LOG}"

if [[ ${FAILED} -gt 0 ]]; then
  exit 1
fi

exit 0
