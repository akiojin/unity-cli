"""Check retained audio acceptance artifacts; does not launch or mutate Unity."""
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SKILL = ROOT / ".claude-plugin/plugins/unity-cli/skills/unity-audio-setup"


def read(path):
    return json.loads(path.read_text())


hashes = {
    str(path.relative_to(SKILL)): hashlib.sha256(path.read_bytes()).hexdigest()
    for path in SKILL.rglob("*") if path.is_file()
}
for version in ("2022.3.62f3", "6000.3.25f1"):
    folder = HERE / version
    assert read(folder / "skill-hashes.json") == hashes, "stale skill evidence"
    result = read(folder / "result.json")
    assert result.get("status", result.get("result")) == "PASS"
    assert result["assertions"] and all(result["assertions"].values())
    assert not result.get("failures")
    session = read(folder / "claude-session.json")
    assert not session["result"].get("is_error")
    assert any(
        "unity-audio-setup" in json.dumps(call)
        for call in session["skill_invocations"]
    ), "missing actual Skill invocation"
    parent = read(folder / "parent-verification.json")
    assert parent["status"] == "PASS" and parent["version"] == version
    first, second = parent["playback"]
    assert first["isPlaying"] and second["isPlaying"]
    assert (second["timeSamples"] - first["timeSamples"]) % first["samples"] > 0
    assert first["mixer"] == second["mixer"] == "Assets/Audio/Final2.mixer"
    assert first["clip"] == second["clip"] == "Assets/Audio/tone.wav"
    assert parent["before_reload"]["instance"] != parent["after_reload"]["instance"]
    assert parent["console"]["count"] == 0
    assert parent["background_before"] == parent["background_restored"]
    assert not parent["after_stop"]["isPlaying"]
    values = []
    for record in read(folder / "parent-transcript.json"):
        if record["exit_code"] == 0:
            data = json.loads(record["stdout"])
            if data.get("state") == "completed":
                values.append(data["value"])
    assert first in values and second in values, "samples must match raw eval output"
    for name, expected in parent["asset_sha256"].items():
        assert hashlib.sha256((folder / "assets" / name).read_bytes()).hexdigest() == expected
    print(f"PASS {version}: exact skill hashes, Claude invocation, persisted assets, live samples")

assert not read(HERE / "routing/red-final-summary.json")["all_passed"]
for filename in ("green2-summary.json", "canonical-summary.json"):
    report = read(HERE / "routing" / filename)
    assert report["all_passed"]
    audio = [case for case in report["results"] if case["id"].startswith("AUD380-")]
    assert len(audio) == 8
    assert all(case[key] for case in audio for key in ("top1", "tool_correct", "payload_valid"))
print("PASS routing: measured RED, eight exact GREEN cases, full aggregate gates")
