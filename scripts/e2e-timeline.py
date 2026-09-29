"""Timeline CLI acceptance tests; requires the project's real Editor listener."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--host", default="127.0.0.1")
parser.add_argument("--port", default="6474")
parser.add_argument("--unity-cli", default=str(ROOT / "target/debug/unity-cli"))
parser.add_argument("--without-timeline", action="store_true")
args = parser.parse_args()
passed = 0


def call(tool, payload, error=None):
    global passed
    result = subprocess.run(
        [args.unity_cli, "raw", tool, "--json", json.dumps(payload),
         "--host", args.host, "--port", args.port, "--timeout-ms", "120000",
         "--output", "json"], text=True, capture_output=True,
        env={**os.environ, "UNITY_PROJECT_ROOT": os.getenv("UNITY_PROJECT_ROOT", str(ROOT / "UnityCliBridge"))},
        timeout=150)
    if error:
        assert error in result.stdout + result.stderr, (tool, error, result.stdout, result.stderr)
        passed += 1
        print("PASS", tool, error, flush=True)
        return None
    assert result.returncode == 0, (tool, result.stdout, result.stderr)
    data = json.loads(result.stdout)
    assert not data.get("error") and data.get("success") is not False, (tool, data)
    passed += 1
    print("PASS", tool, json.dumps(data), flush=True)
    return data


def check(condition, description):
    global passed
    assert condition, description
    passed += 1
    print("PASS", description, flush=True)


def run():
    info = call("get_editor_info", {})
    print("Unity version:", info["unity"]["unityVersion"], flush=True)
    if args.without_timeline:
        call("get_timeline", {"assetPath": "Assets/Absent.playable"}, "TIMELINE_PACKAGE_MISSING")
        call("manage_timeline", {"action": "create_asset", "assetPath": "Assets/Absent.playable"},
             "TIMELINE_PACKAGE_MISSING")
        call("capture_video_status", {}, "RECORDER_PACKAGE_MISSING")
        return
    call("capture_video_status", {})
    fixture = call("execute_menu_item", {"menuPath": "Tools/Unity CLI/Timeline/Generate E2E Fixture"})
    check(fixture.get("executed") is True, "fixture menu executed")
    root = "Assets/Scenes/Generated/E2E/Timeline/"
    asset = root + "Sequence-" + uuid.uuid4().hex + ".playable"
    director = "/TimelineDirector"
    actor = "/TimelineActor"

    def manage(action, **params):
        return call("manage_timeline", {"action": action, **params})

    manage("create_asset", assetPath=asset)
    manage("assign_director", assetPath=asset, directorPath=director)
    created = manage("create_track", assetPath=asset, trackName="Motion")
    track = created["tracks"][0]["trackId"]
    manage("add_clip", assetPath=asset, trackId=track, animationClipPath=root + "Motion.anim",
           start=1, duration=2)
    manage("set_binding", directorPath=director, trackId=track, animatorPath=actor)
    snapshot = call("get_timeline", {"directorPath": director})
    t = snapshot["tracks"][0]
    clip = t["clips"][0]
    check(clip["start"] == 1 and clip["duration"] == 2 and t["binding"] == actor,
          "AC-1 timing and Animator binding readback")
    expected = {key: clip[key] for key in ("animationClipPath", "start", "duration")}
    call("manage_timeline", {"action": "add_clip", "assetPath": asset, "trackId": track,
         "animationClipPath": root + "Motion.anim", "start": 0, "duration": -1}, "INVALID")
    call("manage_timeline", {"action": "create_track", "assetPath": asset,
         "trackName": "Unsupported", "trackType": "AudioTrack"}, "$.trackType must be one of")
    unsupported = call("execute_menu_item", {"menuPath": "Tools/Unity CLI/Timeline/Generate Unsupported Track"})
    check(unsupported.get("executed") is True, "unsupported fixture menu executed")
    unsupported_asset = root + "Unsupported.playable"
    audio = call("get_timeline", {"assetPath": unsupported_asset})["tracks"][0]
    call("manage_timeline", {"action": "delete_track", "assetPath": unsupported_asset,
         "trackId": audio["trackId"]}, "UNSUPPORTED_TRACK")
    call("manage_timeline", {"action": "update_clip", "assetPath": asset, "trackId": track,
         "clipIndex": 0, "expectedClip": {**expected, "start": 99}, "start": 3}, "STALE_CLIP")
    unchanged = call("get_timeline", {"directorPath": director})
    check(unchanged["tracks"] == snapshot["tracks"], "invalid operations leave Timeline unchanged")
    call("save_scene", {})
    reload_result = call("execute_menu_item", {"menuPath": "Tools/Unity CLI/Timeline/Reload E2E Assets"})
    check(reload_result.get("executed") is True, "asset unloaded, reimported and scene reopened")
    reloaded = call("get_timeline", {"directorPath": director})
    check(reloaded["tracks"] == snapshot["tracks"], "AC-2 saved scene/asset reload roundtrip")
    manage("evaluate", directorPath=director, time=2)
    details = call("get_gameobject_details", {"path": actor})
    check(abs(details["transform"]["position"]["x"] - 5) < 0.05,
          "AC-3 PlayableDirector evaluation animates Transform.x=5")
    manage("update_clip", assetPath=asset, trackId=track, clipIndex=0,
           expectedClip=expected, start=2, duration=3)
    updated = call("get_timeline", {"assetPath": asset})["tracks"][0]["clips"][0]
    check(updated["start"] == 2 and updated["duration"] == 3, "clip timing update")
    manage("remove_clip", assetPath=asset, trackId=track, clipIndex=0,
           expectedClip={key: updated[key] for key in expected})
    check(not call("get_timeline", {"assetPath": asset})["tracks"][0]["clips"], "clip removal")
    manage("clear_binding", directorPath=director, trackId=track)
    check(call("get_timeline", {"directorPath": director})["tracks"][0]["binding"] is None,
          "binding clear")
    manage("delete_track", assetPath=asset, directorPath=director, trackId=track)
    check(not call("get_timeline", {"assetPath": asset})["tracks"], "track deletion")


try:
    run()
except Exception as exc:
    print("FAIL:", repr(exc), file=sys.stderr)
    print("Timeline E2E: passed=%d failed=1" % passed, flush=True)
    sys.exit(1)
print("Timeline E2E: passed=%d failed=0" % passed, flush=True)
