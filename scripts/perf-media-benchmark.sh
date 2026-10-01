#!/usr/bin/env bash

set -euo pipefail

HOST="${UNITY_CLI_HOST:-127.0.0.1}"
PORT="${UNITY_CLI_PORT:-6400}"
TIMEOUT_MS="${UNITY_CLI_TIMEOUT_MS:-120000}"
UNITY_CLI=""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
RUN_ID="$(date +%Y%m%d-%H%M%S)"
ARTIFACT_DIR="${PROJECT_ROOT}/.unity/perf-media/${RUN_ID}"
LATENCY_JSONL="${ARTIFACT_DIR}/cli-latencies.jsonl"
RESULT_JSON="${ARTIFACT_DIR}/result.json"
SUMMARY_MD="${ARTIFACT_DIR}/summary.md"

SCENE_PATH="Assets/Scenes/Generated/E2E/Performance/UnityCli_PerfBenchmark.unity"
MENU_PATH="Tools/Unity CLI/Performance/Generate Media Perf Scene"
CONTROLLER_PATH="/PerfScenarioController"
CONTROLLER_TYPE="UnityCliBridge.TestScenes.UnityCliPerfScenarioController"
PROFILER_METRICS='["Scripts Update Time","GC Allocated In Frame","Total Used Memory","Draw Calls Count","SetPass Calls Count","Batches Count","Triangles Count"]'
PERF_SCENE_DIR="Assets/Scenes/Generated/E2E/Performance"
PERF_ASSET_DIR="${PERF_SCENE_DIR}"
PERF_TEXTURE_PATH="${PERF_ASSET_DIR}/UnityCli_PerfTexture.png"
PERF_BASE_MATERIAL_PATH="${PERF_ASSET_DIR}/UnityCli_PerfBase.mat"
PERF_OVERLAY_MATERIAL_PATH="${PERF_ASSET_DIR}/UnityCli_PerfOverlay.mat"

LAST_OUTPUT=""
LAST_DURATION_MS=0
CURRENT_CASE="bootstrap"

usage() {
  cat <<EOF
Usage: scripts/perf-media-benchmark.sh [options]

Options:
  --host <host>          Unity host (default: 127.0.0.1)
  --port <port>          Unity port (default: 6400)
  --timeout-ms <ms>      Per-command timeout in ms (default: 120000)
  --unity-cli <path>     unity-cli binary path
  -h, --help             Show this help
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

require_tool() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "ERROR: required tool not found: $1" >&2
    exit 1
  fi
}

now_ms() {
  python3 - <<'PY'
import time
print(time.time_ns() // 1_000_000)
PY
}

append_latency() {
  local label="$1"
  local tool="$2"

  jq -nc \
    --arg case "${CURRENT_CASE}" \
    --arg label "${label}" \
    --arg tool "${tool}" \
    --argjson cliRoundtripMs "${LAST_DURATION_MS}" \
    '{case:$case,label:$label,tool:$tool,cliRoundtripMs:$cliRoundtripMs}' >> "${LATENCY_JSONL}"
}

ensure_cli() {
  if [[ -n "${UNITY_CLI}" && -x "${UNITY_CLI}" ]]; then
    return
  fi

  if [[ -x "${REPO_ROOT}/target/release/unity-cli" ]]; then
    UNITY_CLI="${REPO_ROOT}/target/release/unity-cli"
    return
  fi

  UNITY_CLI="$(command -v unity-cli 2>/dev/null || true)"
  if [[ -z "${UNITY_CLI}" ]]; then
    echo "ERROR: unity-cli not found. Build with 'cargo build --release' or pass --unity-cli." >&2
    exit 1
  fi
}

invoke_tool() {
  local tool="$1"
  local payload="$2"

  UNITY_PROJECT_ROOT="${PROJECT_ROOT}" \
  "${UNITY_CLI}" tool call "${tool}" \
    --json "${payload}" \
    --host "${HOST}" \
    --port "${PORT}" \
    --timeout-ms "${TIMEOUT_MS}" \
    --output json | jq -c '.data'
}

run_tool() {
  local label="$1"
  local tool="$2"
  local payload="${3:-\{\}}"
  local started ended output rc

  started="$(now_ms)"
  set +e
  output="$(invoke_tool "${tool}" "${payload}" 2>&1)"
  rc=$?
  set -e
  ended="$(now_ms)"

  LAST_DURATION_MS="$((ended - started))"
  LAST_OUTPUT="${output}"
  append_latency "${label}" "${tool}"

  if [[ ${rc} -ne 0 ]]; then
    echo "ERROR: ${label} (${tool}) failed" >&2
    echo "${output}" >&2
    exit ${rc}
  fi

  if ! jq -e . >/dev/null 2>&1 <<<"${output}"; then
    echo "ERROR: ${label} (${tool}) returned non-JSON output" >&2
    echo "${output}" >&2
    exit 1
  fi

  if jq -e 'type == "object" and has("error") and (.error | tostring | length) > 0' >/dev/null 2>&1 <<<"${output}"; then
    echo "ERROR: ${label} (${tool}) returned error payload" >&2
    echo "${output}" >&2
    exit 1
  fi
}

scene_file_path() {
  printf '%s/%s\n' "${PROJECT_ROOT}" "${SCENE_PATH}"
}

wait_for_play_state() {
  local expected="$1"
  local attempts="${2:-30}"
  local state

  for _ in $(seq 1 "${attempts}"); do
    run_tool "get_editor_state" "get_editor_state" '{}'
    state="$(jq -r '.state.isPlaying // false' <<<"${LAST_OUTPUT}")"
    if [[ "${state}" == "${expected}" ]]; then
      return 0
    fi
    sleep 1
  done

  echo "ERROR: timed out waiting for isPlaying=${expected}" >&2
  exit 1
}

ensure_not_playing() {
  run_tool "get_editor_state_preflight" "get_editor_state" '{}'
  local state
  state="$(jq -r '.state.isPlaying // false' <<<"${LAST_OUTPUT}")"
  if [[ "${state}" == "true" ]]; then
    run_tool "stop_game_preflight" "stop_game" '{}'
    wait_for_play_state false
  fi
}

wait_for_file() {
  local file_path="$1"
  local attempts="${2:-20}"

  if [[ -z "${file_path}" ]]; then
    echo "ERROR: expected file path is empty" >&2
    exit 1
  fi

  for _ in $(seq 1 "${attempts}"); do
    if [[ -f "${file_path}" ]]; then
      return 0
    fi
    sleep 1
  done

  echo "ERROR: timed out waiting for file: ${file_path}" >&2
  exit 1
}

set_controller_field() {
  local field_path="$1"
  local value="$2"

  run_tool \
    "set_${field_path}" \
    "set_component_field" \
    "$(jq -nc \
      --arg gameObjectPath "${CONTROLLER_PATH}" \
      --arg componentType "${CONTROLLER_TYPE}" \
      --arg fieldPath "${field_path}" \
      --arg value "${value}" \
      '{gameObjectPath:$gameObjectPath,componentType:$componentType,fieldPath:$fieldPath,value:$value}')"
}

assign_material() {
  local game_object_path="$1"
  local material_path="$2"

  run_tool \
    "assign_material_${game_object_path##*/}" \
    "set_component_field" \
    "$(jq -nc \
      --arg gameObjectPath "${game_object_path}" \
      --arg componentType "MeshRenderer" \
      --arg fieldPath "m_Materials.Array.data[0]" \
      --arg serializedPropertyPath "m_Materials.Array.data[0]" \
      --arg valueType "objectReference" \
      --arg value "${material_path}" \
      --arg assetPath "${material_path}" \
      '{
        gameObjectPath:$gameObjectPath,
        componentType:$componentType,
        fieldPath:$fieldPath,
        serializedPropertyPath:$serializedPropertyPath,
        valueType:$valueType,
        value:$value,
        objectReference:{assetPath:$assetPath}
      }')"
}

create_perf_primitive() {
  local name="$1"
  local primitive_type="$2"
  local parent_path="$3"
  local px="$4"
  local py="$5"
  local pz="$6"
  local sx="$7"
  local sy="$8"
  local sz="$9"

  run_tool \
    "create_${name}" \
    "create_gameobject" \
    "$(jq -nc \
      --arg name "${name}" \
      --arg primitiveType "${primitive_type}" \
      --arg parentPath "${parent_path}" \
      --argjson px "${px}" \
      --argjson py "${py}" \
      --argjson pz "${pz}" \
      --argjson sx "${sx}" \
      --argjson sy "${sy}" \
      --argjson sz "${sz}" \
      '{
        name:$name,
        primitiveType:$primitiveType,
        parentPath:$parentPath,
        position:{x:$px,y:$py,z:$pz},
        scale:{x:$sx,y:$sy,z:$sz}
      }')"
}

build_perf_scene_fallback() {
  echo "Falling back to direct CLI scene construction..."

  ensure_not_playing
  if [[ -f "$(scene_file_path)" ]]; then
    load_perf_scene
  else
    run_tool \
      "create_perf_scene" \
      "create_scene" \
      "$(jq -nc \
        --arg sceneName "UnityCli_PerfBenchmark" \
        --arg path "${PERF_SCENE_DIR}" \
        '{sceneName:$sceneName,path:$path,loadScene:true}')"
  fi

  run_tool \
    "copy_perf_texture" \
    "manage_asset_database" \
    "$(jq -nc \
      --arg fromPath "Assets/Materials/Dice/DiceTexture.png" \
      --arg toPath "${PERF_TEXTURE_PATH}" \
      '{action:"copy_asset",fromPath:$fromPath,toPath:$toPath}')"

  run_tool \
    "create_perf_base_material" \
    "create_material" \
    "$(jq -nc \
      --arg materialPath "${PERF_BASE_MATERIAL_PATH}" \
      '{materialPath:$materialPath,shader:"Unlit/Texture",overwrite:true}')"

  run_tool \
    "create_perf_overlay_material" \
    "create_material" \
    "$(jq -nc \
      --arg materialPath "${PERF_OVERLAY_MATERIAL_PATH}" \
      '{materialPath:$materialPath,shader:"Unlit/Texture",overwrite:true}')"

  run_tool \
    "modify_perf_base_material" \
    "modify_material" \
    "$(jq -nc \
      --arg materialPath "${PERF_BASE_MATERIAL_PATH}" \
      --arg texturePath "${PERF_TEXTURE_PATH}" \
      '{materialPath:$materialPath,properties:{_MainTex:$texturePath}}')"

  run_tool \
    "modify_perf_overlay_material" \
    "modify_material" \
    "$(jq -nc \
      --arg materialPath "${PERF_OVERLAY_MATERIAL_PATH}" \
      --arg texturePath "${PERF_TEXTURE_PATH}" \
      '{materialPath:$materialPath,properties:{_MainTex:$texturePath}}')"

  run_tool "create_perf_controller" "create_gameobject" '{"name":"PerfScenarioController"}'
  run_tool \
    "add_perf_controller_component" \
    "add_component" \
    "$(jq -nc \
      --arg gameObjectPath "${CONTROLLER_PATH}" \
      --arg componentType "${CONTROLLER_TYPE}" \
      '{gameObjectPath:$gameObjectPath,componentType:$componentType}')"

  run_tool "create_static_root" "create_gameobject" '{"name":"StaticImageRoot","parentPath":"/PerfScenarioController"}'
  run_tool "create_overlay_root" "create_gameobject" '{"name":"OverlayRoot","parentPath":"/PerfScenarioController"}'
  run_tool "create_motion_root" "create_gameobject" '{"name":"MotionRoot","parentPath":"/PerfScenarioController"}'

  local row column index x y z angle
  index=0
  for row in 0 1 2; do
    for column in 0 1 2 3; do
      x=$(( (column - 1) * 3 ))
      y=$(( (1 - row) * 2 ))
      z=$(( 12 - index ))
      create_perf_primitive "StaticQuad_${row}_${column}" "quad" "/PerfScenarioController/StaticImageRoot" "${x}" "${y}" "${z}" 2.8 1.6 1
      assign_material "/PerfScenarioController/StaticImageRoot/StaticQuad_${row}_${column}" "${PERF_BASE_MATERIAL_PATH}"
      index=$((index + 1))
    done
  done

  for row in 0 1; do
    for column in 0 1; do
      x=$(( column * 3 - 1 ))
      y=$(( row * -2 ))
      z=$(( 8 - row - column ))
      create_perf_primitive "OverlayQuad_${row}_${column}" "quad" "/PerfScenarioController/OverlayRoot" "${x}" "${y}" "${z}" 2.2 1.2 1
      assign_material "/PerfScenarioController/OverlayRoot/OverlayQuad_${row}_${column}" "${PERF_OVERLAY_MATERIAL_PATH}"
    done
  done

  for index in 0 1 2 3 4 5 6 7; do
    angle=$(( index * 45 ))
    x="$(python3 - <<PY
import math
print(round(math.cos(math.radians(${angle})) * 4.5, 3))
PY
)"
    y="$(python3 - <<PY
import math
print(round(math.sin(math.radians(${angle})) * 2.0, 3))
PY
)"
    z="$(python3 - <<PY
import math
print(round(10.0 + math.sin(math.radians(${angle})) * 1.2, 3))
PY
)"
    create_perf_primitive "MotionCube_${index}" "cube" "/PerfScenarioController/MotionRoot" "${x}" "${y}" "${z}" 1.1 1.1 1.1
    assign_material "/PerfScenarioController/MotionRoot/MotionCube_${index}" "${PERF_BASE_MATERIAL_PATH}"
  done

  run_tool "save_perf_scene" "save_scene" '{}'
}

ensure_perf_scene() {
  local scene_file
  scene_file="$(scene_file_path)"

  run_tool "generate_media_perf_scene" "execute_menu_item" "$(jq -nc --arg menuPath "${MENU_PATH}" '{menuPath:$menuPath}')"
  printf '%s\n' "${LAST_OUTPUT}" > "${ARTIFACT_DIR}/generate_scene.json"

  if [[ -f "${scene_file}" ]]; then
    return
  fi

  build_perf_scene_fallback
}

load_perf_scene() {
  run_tool \
    "load_perf_scene" \
    "load_scene" \
    "$(jq -nc --arg scenePath "${SCENE_PATH}" '{scenePath:$scenePath,loadMode:"Single"}')"
}

start_profiler() {
  run_tool \
    "profiler_start" \
    "profiler_start" \
    "$(jq -nc --argjson metrics "${PROFILER_METRICS}" '{recordToFile:false,maxDurationSec:0,metrics:$metrics}')"
}

build_source_video() {
  CURRENT_CASE="source_motion"
  load_perf_scene
  set_controller_field "scenarioName" "SourceMotion"
  set_controller_field "videoUrl" ""

  run_tool "play_source_motion" "play_game" '{}'
  wait_for_play_state true
  sleep 2

  run_tool \
    "capture_video_start_source" \
    "capture_video_start" \
    '{"captureMode":"game","fps":24,"width":1280,"height":720,"maxDurationSec":0}'
  sleep 3
  run_tool "capture_video_stop_source" "capture_video_stop" '{}'
  SOURCE_VIDEO_STOP_JSON="${LAST_OUTPUT}"
  SOURCE_VIDEO_STOP_MS="${LAST_DURATION_MS}"
  SOURCE_VIDEO_PATH="$(jq -r '.outputPath // empty' <<<"${LAST_OUTPUT}")"
  wait_for_file "${SOURCE_VIDEO_PATH}"

  run_tool "stop_source_motion" "stop_game" '{}'
  wait_for_play_state false

  jq -nc \
    --arg case "${CURRENT_CASE}" \
    --arg sourceVideoPath "${SOURCE_VIDEO_PATH}" \
    --argjson captureStop "${SOURCE_VIDEO_STOP_JSON}" \
    --argjson captureStopCliMs "${SOURCE_VIDEO_STOP_MS}" \
    '{case:$case,sourceVideoPath:$sourceVideoPath,captureStop:$captureStop,cliRoundtripMs:{captureVideoStop:$captureStopCliMs}}' \
    > "${ARTIFACT_DIR}/source_motion.json"
}

run_case() {
  local case_name="$1"
  local scenario_name="$2"
  local video_url="$3"
  local with_runtime_capture="$4"
  local screenshot_json profiler_json command_stats_json runtime_video_json runtime_video_ms

  CURRENT_CASE="${case_name}"
  runtime_video_json='null'
  runtime_video_ms='null'

  load_perf_scene
  set_controller_field "scenarioName" "${scenario_name}"
  set_controller_field "videoUrl" "${video_url}"

  run_tool "play_${case_name}" "play_game" '{}'
  wait_for_play_state true
  sleep 2

  start_profiler
  local profiler_start_ms="${LAST_DURATION_MS}"
  sleep 1

  run_tool \
    "capture_screenshot_${case_name}" \
    "capture_screenshot" \
    '{"captureMode":"game","width":1280,"height":720}'
  screenshot_json="${LAST_OUTPUT}"
  local screenshot_ms="${LAST_DURATION_MS}"

  if [[ "${with_runtime_capture}" == "true" ]]; then
    run_tool \
      "capture_video_start_${case_name}" \
      "capture_video_start" \
      '{"captureMode":"game","fps":15,"width":1280,"height":720,"maxDurationSec":0}'
    sleep 2
    run_tool "capture_video_stop_${case_name}" "capture_video_stop" '{}'
    runtime_video_json="${LAST_OUTPUT}"
    runtime_video_ms="${LAST_DURATION_MS}"
  fi

  sleep 1
  run_tool "profiler_stop_${case_name}" "profiler_stop" '{}'
  profiler_json="${LAST_OUTPUT}"
  local profiler_stop_ms="${LAST_DURATION_MS}"

  run_tool "get_command_stats_${case_name}" "get_command_stats" '{}'
  command_stats_json="${LAST_OUTPUT}"
  local command_stats_ms="${LAST_DURATION_MS}"

  run_tool "stop_${case_name}" "stop_game" '{}'
  wait_for_play_state false

  jq -nc \
    --arg name "${case_name}" \
    --arg scenario "${scenario_name}" \
    --arg videoUrl "${video_url}" \
    --argjson screenshot "${screenshot_json}" \
    --argjson profiler "${profiler_json}" \
    --argjson commandStats "${command_stats_json}" \
    --argjson runtimeVideo "${runtime_video_json}" \
    --argjson screenshotCliMs "${screenshot_ms}" \
    --argjson profilerStartCliMs "${profiler_start_ms}" \
    --argjson profilerStopCliMs "${profiler_stop_ms}" \
    --argjson commandStatsCliMs "${command_stats_ms}" \
    --argjson runtimeVideoCliMs "${runtime_video_ms}" \
    '{
      name:$name,
      scenario:$scenario,
      videoUrl:(if ($videoUrl | length) > 0 then $videoUrl else null end),
      screenshot:$screenshot,
      profiler:$profiler,
      commandStats:$commandStats,
      runtimeVideoCapture:$runtimeVideo,
      cliRoundtripMs:{
        screenshot:$screenshotCliMs,
        profilerStart:$profilerStartCliMs,
        profilerStop:$profilerStopCliMs,
        getCommandStats:$commandStatsCliMs,
        runtimeVideoCapture:$runtimeVideoCliMs
      }
    }' > "${ARTIFACT_DIR}/${case_name}.json"
}

write_summary() {
  local static_path playback_path mixed_path
  static_path="$(jq -r '.screenshot.path // "-" ' "${ARTIFACT_DIR}/static_image.json")"
  playback_path="$(jq -r '.screenshot.path // "-" ' "${ARTIFACT_DIR}/video_playback.json")"
  mixed_path="$(jq -r '.screenshot.path // "-" ' "${ARTIFACT_DIR}/mixed_overlay.json")"

  {
    echo "# Media Perf Benchmark"
    echo
    echo "- Run ID: ${RUN_ID}"
    echo "- Scene: ${SCENE_PATH}"
    echo "- Source video: ${SOURCE_VIDEO_PATH}"
    echo
    echo "## Cases"
    echo
    echo "### static_image"
    echo "- Screenshot: ${static_path}"
    echo "- Profiler duration: $(jq -r '.profiler.duration // .profiler.lastResult.duration // "-" ' "${ARTIFACT_DIR}/static_image.json")"
    echo "- capture_screenshot CLI ms: $(jq -r '.cliRoundtripMs.screenshot' "${ARTIFACT_DIR}/static_image.json")"
    echo
    echo "### video_playback"
    echo "- Screenshot: ${playback_path}"
    echo "- Profiler duration: $(jq -r '.profiler.duration // .profiler.lastResult.duration // "-" ' "${ARTIFACT_DIR}/video_playback.json")"
    echo "- capture_screenshot CLI ms: $(jq -r '.cliRoundtripMs.screenshot' "${ARTIFACT_DIR}/video_playback.json")"
    echo
    echo "### mixed_overlay"
    echo "- Screenshot: ${mixed_path}"
    echo "- Runtime video capture path: $(jq -r '.runtimeVideoCapture.outputPath // "-" ' "${ARTIFACT_DIR}/mixed_overlay.json")"
    echo "- capture_video_stop CLI ms: $(jq -r '.cliRoundtripMs.runtimeVideoCapture // "-" ' "${ARTIFACT_DIR}/mixed_overlay.json")"
    echo
    echo "## Focus Tool Stats"
    echo
    for tool_name in capture_screenshot capture_video_start capture_video_stop profiler_start profiler_stop get_command_stats; do
      echo "### ${tool_name}"
      jq -r --arg tool "${tool_name}" '
        .commandStats.timings[$tool] // empty
        | "- count=\(.count) avgMs=\(.avgMs) maxMs=\(.maxMs)"
      ' "${ARTIFACT_DIR}/mixed_overlay.json"
      jq -r --arg tool "${tool_name}" '
        .commandStats.timings[$tool].stages // {}
        | to_entries[]
        | "- \(.key): avgMs=\(.value.avgMs) maxMs=\(.value.maxMs)"
      ' "${ARTIFACT_DIR}/mixed_overlay.json" 2>/dev/null || true
      echo
    done
  } > "${SUMMARY_MD}"
}

require_tool jq
require_tool python3
ensure_cli

mkdir -p "${ARTIFACT_DIR}"

echo "Artifacts: ${ARTIFACT_DIR}"
echo "Unity: ${HOST}:${PORT}"
echo "Scene: ${SCENE_PATH}"

CURRENT_CASE="bootstrap"
run_tool "system_ping" "ping" '{}'
ensure_not_playing
ensure_perf_scene

build_source_video
run_case "static_image" "StaticImage" "" "false"
run_case "video_playback" "VideoPlayback" "${SOURCE_VIDEO_PATH}" "false"
run_case "mixed_overlay" "MixedOverlay" "${SOURCE_VIDEO_PATH}" "true"

jq -n \
  --arg runId "${RUN_ID}" \
  --arg scenePath "${SCENE_PATH}" \
  --arg artifactsDir "${ARTIFACT_DIR}" \
  --arg sourceVideoPath "${SOURCE_VIDEO_PATH}" \
  --slurpfile sourceMotion "${ARTIFACT_DIR}/source_motion.json" \
  --slurpfile staticCase "${ARTIFACT_DIR}/static_image.json" \
  --slurpfile playbackCase "${ARTIFACT_DIR}/video_playback.json" \
  --slurpfile mixedCase "${ARTIFACT_DIR}/mixed_overlay.json" \
  '{
    runId:$runId,
    scenePath:$scenePath,
    artifactsDir:$artifactsDir,
    sourceVideo:{
      path:$sourceVideoPath,
      buildCase:$sourceMotion[0]
    },
    cases:[
      $staticCase[0],
      $playbackCase[0],
      $mixedCase[0]
    ]
  }' > "${RESULT_JSON}"

write_summary

echo "Result JSON: ${RESULT_JSON}"
echo "Summary MD: ${SUMMARY_MD}"
