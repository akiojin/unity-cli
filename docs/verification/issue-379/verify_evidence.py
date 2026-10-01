"""Check recorded evidence integrity; this does not rerun the Unity scenarios."""

import hashlib
import json
import struct
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
SKILL = REPO / ".claude-plugin/plugins/unity-cli/skills/unity-2d-sprite-tilemap"


def require(condition, message):
    if not condition:
        raise SystemExit(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


expected = json.loads((ROOT / "skill-hashes.json").read_text())
require(len(expected) == 3, "Expected all three skill files")
for name, sha in expected.items():
    require(digest(SKILL / name) == sha, f"Stale skill evidence: {name}")

manifest = json.loads((ROOT / "artifact-hashes.json").read_text())
tracked = set(subprocess.check_output(
    ["git", "ls-files", "--", str(ROOT)], cwd=REPO, text=True
).splitlines())
for name, sha in manifest.items():
    require(digest(ROOT / name) == sha, f"Artifact changed: {name}")
    require(str((ROOT / name).relative_to(REPO)) in tracked,
            f"Artifact omitted by Git: {name}")

for version in ["2022.3.62f3", "6000.3.25f1"]:
    directory = ROOT / version
    result = json.loads((directory / "result.json").read_text())
    require(result["overall"]["status"] == "passed", f"Scenario failed: {version}")
    require(result["skillUnderTest"]["sha256"] == expected, f"Wrong skill: {version}")
    invocations = json.loads((directory / "skill-invocation.json").read_text())
    require(any("unity-2d-sprite-tilemap" in json.dumps(x) for x in invocations),
            f"Missing Skill tool invocation: {version}")
    screenshot = directory / "screenshots/game.png"
    data = screenshot.read_bytes()
    require(data[:8] == b"\x89PNG\r\n\x1a\n", f"Not PNG: {version}")
    require(struct.unpack(">II", data[16:24]) == (1280, 720), f"Wrong size: {version}")
    require(digest(screenshot) == result["capture"]["sha256"], f"Wrong capture: {version}")
    require(list((directory / "project-snapshot").rglob("*.unity")), f"No scene: {version}")
    require(list((directory / "project-snapshot").rglob("*.spriteatlas")), f"No atlas: {version}")
    require(list((directory / "project-snapshot").rglob("*.asset")), f"No Tile: {version}")
    print(f"PASS {version}: final skill hashes, Skill invocation, saved assets and PNG")

print(f"PASS {len(manifest)} recorded artifact checksums (offline integrity check)")
