"""Real Editor audio authoring assertions. Invoked by e2e-audio-batch-host.sh."""
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import wave

cli, port, project, output = sys.argv[1:]
transcript = []


def call(tool, payload):
    result = subprocess.run(
        [cli, "raw", tool, "--json", json.dumps(payload), "--host", "127.0.0.1",
         "--port", port, "--timeout-ms", "120000", "--output", "json"],
        capture_output=True, text=True, timeout=150,
        env={**os.environ, "UNITY_PROJECT_ROOT": project})
    transcript.append({"tool": tool, "params": payload, "stdout": result.stdout,
                       "stderr": result.stderr, "exitCode": result.returncode})
    Path(output, "transcript.json").write_text(json.dumps(transcript, indent=2))
    assert result.returncode == 0, transcript[-1]
    data = json.loads(result.stdout)
    assert not data.get("error") and data.get("success") is not False, data
    return data


info = call("get_editor_info", {})
print("Editor", info["unity"]["unityVersion"], flush=True)
started = call("run_tests", {"testMode": "EditMode", "filter": "AudioAuthoringTests", "includeDetails": True})
for _ in range(180):
    status = call("get_test_status", {"includeTestResults": True, "includeFileContent": True})
    if status.get("status") in ("completed", "failed"):
        break
    time.sleep(2)
Path(output, "tests.json").write_text(json.dumps(status, indent=2))
assert status.get("status") == "completed", status
assert status.get("runId") == started.get("runId"), status
assert status.get("failedTests") == 0 and status.get("totalTests", 0) == 16, status
assert status.get("passedTests") == 16 and status.get("skippedTests") == 0, status
print("PASS AudioAuthoringTests: 16/16", flush=True)

folder = Path(project, "Assets/AudioE2E")
folder.mkdir(exist_ok=True)
with wave.open(str(folder / "tone.wav"), "wb") as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(44100)
    wav.writeframes(b"\0\0" * 4410)
call("refresh_assets", {})
mixer = "Assets/AudioE2E/Test.mixer"
call("manage_audio_mixer", {"action": "create", "assetPath": mixer})
call("manage_audio_mixer", {"action": "add_group", "assetPath": mixer, "parentGroup": "Master", "name": "Music"})
call("manage_audio_mixer", {"action": "add_group", "assetPath": mixer, "parentGroup": "Master/Music", "name": "Ambient"})
call("manage_audio_mixer", {"action": "expose_parameter", "assetPath": mixer, "groupPath": "Master/Music/Ambient", "parameter": "Volume", "parameterName": "AmbientVolume"})
settings = {"loadType": "Streaming", "compressionFormat": "Vorbis", "quality": 0.42,
            "forceToMono": True, "loadInBackground": True}
call("manage_asset_import_settings", {"action": "modify", "assetPath": "Assets/AudioE2E/tone.wav", "settings": settings})
call("manage_asset_import_settings", {"action": "reimport", "assetPath": mixer})
structure = call("manage_audio_mixer", {"action": "get", "assetPath": mixer})
assert [g["path"] for g in structure["groups"]] == ["Master", "Master/Music", "Master/Music/Ambient"], structure
assert structure["exposedParameters"][0]["name"] == "AmbientVolume", structure
actual = call("manage_asset_import_settings", {"action": "get", "assetPath": "Assets/AudioE2E/tone.wav"})["settings"]
for key, expected in settings.items():
    assert abs(actual[key] - expected) < 0.001 if key == "quality" else actual[key] == expected, (key, actual)
print("PASS create -> nested group -> expose Volume -> AudioImporter -> persisted readback", flush=True)
