#!/usr/bin/env python3
"""Verify composited Game capture on a running, isolated GUI Editor.

Requires Pillow, unity-cli and the fixture installed in Assets. Writes PNGs and
JSON comparisons for Edit/Play, UI Toolkit/uGUI, camera-only and focus failures.
Run separately for each supported Unity version. Never use a user's work scene.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import time

from PIL import Image, ImageChops


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--unity-cli", default="unity-cli")
    parser.add_argument("--activate", action="store_true", help="Activate the connected macOS Editor")
    parser.add_argument("--run-tests", action="store_true", help="Also run ScreenshotHandlerTests")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    def raw(tool, payload, expected_error=None):
        process = subprocess.run(
            [args.unity_cli, "--output", "json", "--port", str(args.port), "--timeout-ms", "15000",
             "raw", tool, "--json", json.dumps(payload)],
            text=True, capture_output=True, timeout=25)
        envelope = json.loads(process.stdout)
        with (args.output / "calls.jsonl").open("a") as transcript:
            transcript.write(json.dumps({"tool": tool, "params": payload,
                                         "exit": process.returncode, "result": envelope}) + "\n")
        if expected_error:
            assert process.returncode == 6 and envelope["success"] is False, envelope
            assert envelope["errors"][0]["code"] == expected_error, envelope
        else:
            assert process.returncode == 0, envelope
            assert envelope["success"] is True, envelope
        return envelope["data"]

    def evaluate(code):
        result = raw("eval_csharp", {"code": code, "mode": "statements"})
        if result.get("state") == "running":
            deadline = time.monotonic() + 30
            while result.get("state") == "running" and time.monotonic() < deadline:
                time.sleep(.2)
                result = raw("get_eval_status", {"requestId": result["requestId"]})
        assert result.get("state") == "completed", result
        return result

    def focus():
        if args.activate:
            pid = evaluate('return System.Diagnostics.Process.GetCurrentProcess().Id;')["value"]
            subprocess.run(["osascript", "-e", 'tell application "System Events" to set frontmost of '
                            f'(first process whose unix id is {int(pid)}) to true'], check=True)
        evaluate('EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Focus(); return true;')
        time.sleep(.3)

    def wait_state(playing):
        deadline = time.monotonic() + 90
        stable = 0
        while time.monotonic() < deadline:
            time.sleep(1)
            try:
                state = raw("get_editor_state", {})["state"]
                stable = stable + 1 if (state["isPlaying"] == playing
                                       and not state["isCompiling"] and not state["isUpdating"]) else 0
                if stable >= 2:
                    return
            except (subprocess.SubprocessError, json.JSONDecodeError, KeyError):
                # Domain Reload temporarily disconnects the bridge. Retry only this read.
                stable = 0
        raise AssertionError(f"Editor did not settle with isPlaying={playing}")

    assert Path(raw("ping", {})["projectPath"]).resolve() == args.project.resolve(), "Wrong Editor/project"
    raw("stop_game", {})
    wait_state(False)
    source_hashes = {}
    package = Path("UnityCliBridge/Packages/unity-cli-bridge")
    for relative in ["Editor/Handlers/ScreenshotHandler.cs", "Editor/Core/BridgeCommandRouter.cs",
                     "Tests/Editor/Handlers/ScreenshotHandlerTests.cs"]:
        source = (Path(__file__).resolve().parent.parent / package / relative).read_bytes()
        actual = (args.project / "Packages/unity-cli-bridge" / relative).read_bytes()
        assert source == actual, f"Stale bridge in verification project: {relative}"
        source_hashes[relative] = hashlib.sha256(source).hexdigest()
    fixture = Path(__file__).parent / "fixtures/ScreenshotCaptureFixture.cs"
    shutil.copyfile(fixture, args.project / "Assets/ScreenshotCaptureFixture.cs")
    raw("refresh_assets", {})
    deadline = time.monotonic() + 90
    while True:
        time.sleep(1)
        try:
            compilation = raw("get_compilation_state", {})
        except (subprocess.SubprocessError, json.JSONDecodeError):
            assert time.monotonic() < deadline, "Editor did not reconnect after compilation"
            continue
        if not compilation["isCompiling"] and not compilation["isUpdating"]:
            assert compilation["errorCount"] == 0, compilation
            break
        assert time.monotonic() < deadline, compilation
    if args.run_tests:
        focus()
        started = raw("run_tests", {"testMode": "EditMode", "filter": "ScreenshotHandlerTests"})
        assert started.get("status") == "running", started
        deadline = time.monotonic() + 120
        while True:
            time.sleep(1)
            result = raw("get_test_status", {"includeTestResults": True})
            if result.get("status") != "running":
                break
            assert time.monotonic() < deadline, result
        (args.output / "tests.json").write_text(json.dumps(result, indent=2))
        assert result.get("success") and result.get("passedTests") == 5 and result.get("skippedTests") == 0, result
        print("PASS ScreenshotHandlerTests: 5/5", flush=True)
    evaluate('''
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
var camera = new GameObject("Main Camera").AddComponent<Camera>();
camera.tag = "MainCamera";
camera.clearFlags = CameraClearFlags.SolidColor;
camera.backgroundColor = Color.black;
var panel = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/Scenes/Generated/E2E/CapturePanel.asset");
if (panel == null) {
panel = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
System.IO.Directory.CreateDirectory("Assets/Scenes/Generated/E2E");
AssetDatabase.CreateAsset(panel, "Assets/Scenes/Generated/E2E/CapturePanel.asset");
}
panel.scaleMode = UnityEngine.UIElements.PanelScaleMode.ConstantPixelSize;
var doc = new GameObject("Toolkit").AddComponent<UnityEngine.UIElements.UIDocument>();
doc.panelSettings = panel;
var canvas = new GameObject("Overlay").AddComponent<Canvas>();
canvas.renderMode = RenderMode.ScreenSpaceOverlay;
var marker = new GameObject("OverlayMarker", typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
marker.transform.SetParent(canvas.transform, false);
marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = new Vector2(1, 0);
marker.rectTransform.pivot = new Vector2(1, 0);
marker.rectTransform.anchoredPosition = new Vector2(-30, 30);
marker.rectTransform.sizeDelta = new Vector2(200, 140);
var fixture = new GameObject("Fixture").AddComponent<ScreenshotCaptureFixture>();
fixture.document = doc; fixture.overlay = marker;
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Generated/E2E/Capture.unity");
return Application.unityVersion;
''')
    summary = {"port": args.port, "cases": {}, "source_sha256": source_hashes,
               "unity_version": evaluate("return Application.unityVersion;")["value"],
               "User Verification Result": "n/a (autonomous)"}
    raw("clear_console", {})

    def capture(name, include_ui, **extra):
        result = raw("capture_screenshot", {"captureMode": "game", "includeUI": include_ui,
                                            "osFallback": False, **extra})
        assert "error" not in result, result
        path = args.output / (name + ".png")
        shutil.copyfile(result["path"], path)
        image = Image.open(path).convert("RGB")
        assert image.size == (result["width"], result["height"]), result
        if extra.get("encodeAsBase64"):
            assert base64.b64decode(result["base64Data"]) == path.read_bytes()
            del result["base64Data"]
        result["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        (args.output / (name + ".json")).write_text(json.dumps(result, indent=2))
        print(f"Captured {name}: {image.width}x{image.height}", flush=True)
        return image

    def colors(image):
        counts = dict(red=0, green=0, blue=0, yellow=0)
        for r, g, b in image.getdata():
            counts["red"] += r > 180 and g < 70 and b < 70
            counts["green"] += g > 180 and r < 70 and b < 70
            counts["blue"] += b > 180 and r < 70 and g < 70
            counts["yellow"] += r > 180 and g > 180 and b < 70
        return counts

    try:
        for mode in ["edit", "play"]:
            print(f"Verifying {mode}", flush=True)
            if mode == "play":
                raw("play_game", {})
                wait_state(True)
            focus()
            images = []
            camera_images = []
            for alternate in [False, True]:
                evaluate('GameObject.Find("Fixture").GetComponent<ScreenshotCaptureFixture>().alternate = '
                         + str(alternate).lower() + '; EditorApplication.QueuePlayerLoopUpdate(); return true;')
                time.sleep(.5)
                focus()
                suffix = "after" if alternate else "before"
                images.append(capture(mode + "-" + suffix, True, encodeAsBase64=True))
                camera_images.append(capture(mode + "-camera-" + suffix, False, width=320, height=180))
            counts = [colors(image) for image in images]
            assert counts[0]["green"] > 1000 and counts[0]["blue"] > 1000, counts
            assert counts[1]["red"] > 1000 and counts[1]["yellow"] > 1000, counts
            assert ImageChops.difference(*images).getbbox() is not None
            assert ImageChops.difference(*camera_images).getbbox() is None
            assert max(colors(camera_images[0]).values()) == 0
            focus()
            resized = capture(mode + "-resized", True, width=640, height=360, encodeAsBase64=True)
            assert resized.size == (640, 360)
            assert colors(resized)["red"] > 100 and colors(resized)["yellow"] > 100
            evaluate('EditorWindow.GetWindow<SceneView>().Focus(); return true;')
            error = raw("capture_screenshot", {"captureMode": "game", "includeUI": True, "osFallback": False},
                        expected_error="GAME_VIEW_NOT_FOCUSED")
            assert error.get("code") == "GAME_VIEW_NOT_FOCUSED" and "path" not in error, error
            summary["cases"][mode] = {"colors": counts, "ui_changed": True,
                                      "camera_unchanged": True, "focus_error": error,
                                      "resize_and_base64": "pass"}
    finally:
        raw("stop_game", {})
        wait_state(False)
    # The bridge's legacy LogEntry bit mapping misclassifies native Metal warnings
    # on 2022.3. Use Unity's own Console counters and retain both observations.
    diagnostics = raw("read_console", {"logTypes": ["Error", "Exception", "Assert"], "count": 100})
    counts = evaluate('''
var type = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
var method = type.GetMethod("GetCountsByType", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
object[] counts = {0, 0, 0}; method.Invoke(null, counts); return counts;
''')["value"]
    assert counts[0] == 0, {"unity_console_counts": counts, "diagnostics": diagnostics}
    summary["console_errors"] = counts[0]
    summary["console_warnings"] = counts[1]
    summary["console_tool_diagnostics"] = diagnostics
    (args.output / "comparison.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
