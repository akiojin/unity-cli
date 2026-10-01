#!/usr/bin/env python3
"""Check recorded real-Editor evidence; this does not re-run Unity or OCR images."""

import hashlib
import importlib.util
import json
import struct
import sys
from pathlib import Path

BUNDLE = Path(__file__).resolve().parent
REPO = BUNDLE.parents[2]
SKILLS = REPO / ".claude-plugin/plugins/unity-cli/skills"
EXPECTED = {"en": "Hello, world!", "ja": "こんにちは、世界！"}
RUNS = {
    "2022.3.62f3": {
        "states": ["en-state.json", "ja-state.json", "en-return-state.json"],
        "images": ["en.png", "ja.png", "en-return.png"],
        "init": "init.json",
    },
    "6000.3.25f1": {
        "states": ["en-state.json", "ja-state.json", "en-return-state.json"],
        "images": ["en.png", "ja.png", "en-return.png"],
        "init": "init.json",
    },
}


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


for version, paths in RUNS.items():
    root = BUNDLE / version
    manifest = read(root / "evidence.json")
    report = read(root / "final/report.json")
    for name, expected_hash in manifest["sha256"].items():
        assert sha(root / name) == expected_hash, (version, "artifact hash", name)
    for name, expected_hash in manifest["skill_sha256"].items():
        assert sha(SKILLS / "unity-localization" / name) == expected_hash, (version, "stale skill", name)
    bridge = REPO / "UnityCliBridge/Packages/unity-cli-bridge"
    for name, expected_hash in manifest["bridge_sha256"].items():
        assert sha(bridge / name) == expected_hash, (version, "changed bridge source", name)
    assert report["skill"]["sha256"] == manifest["skill_sha256"], (version, "client skill snapshot")
    assert report["startedAt"] < report["completedAt"], (version, "execution interval")
    assert manifest["sessions"]["initial"]["session_id"] != manifest["sessions"]["final"]["session_id"]
    assert "status=Succeeded" in read(root / "final" / paths["init"])["value"]

    image_hashes = []
    for code, state_file, image_file in zip(("en", "ja", "en"), paths["states"], paths["images"]):
        response = read(root / "final" / state_file)
        state = response.get("uiElementState", response)
        assert state["path"] == "/LocalizationCanvas/Greeting"
        assert state["text"] == EXPECTED[code], (version, code, "actual UI text")
        assert "LocalizeStringEvent" in state["components"]
        png = root / "final" / image_file
        data = png.read_bytes()
        assert data[:8] == b"\x89PNG\r\n\x1a\n", png
        assert struct.unpack(">II", data[16:24]) == (1280, 720), png
        image_hashes.append(sha(png))
    assert image_hashes[0] != image_hashes[1], (version, "locale images must differ")

    saved = read(root / "independent-state.json")
    assert saved["unity"] == version
    assert not saved["isPlaying"] and not saved["sceneDirty"]
    assert saved["listenerCount"] == 1 and saved["listenerTargetsLabel"]
    assert saved["listenerMethod"] == "set_text" and saved["listenerState"] == "EditorAndRuntime"
    assert saved["savedLabelText"] not in EXPECTED.values(), (version, "no baked translated label")
    assert {item["code"] for item in saved["locales"]} == {"en", "ja"}
    assert {item["locale"]: item["text"] for item in saved["tables"]} == EXPECTED
    assert all(item["keyId"] == saved["keyId"] for item in saved["tables"])
    assets = [saved["scene"], saved["settingsPath"], saved["collectionPath"], saved["sharedDataPath"]]
    assets += [item["path"] for item in saved["locales"] + saved["tables"]]
    for asset in assets:
        assert (root / "assets" / asset).is_file(), (version, "missing saved asset", asset)
    assert not list((root / "assets").rglob("*.ttf")), "Do not redistribute local fonts"
    assert manifest["font"]["binary_included"] is False
    print(f"PASS {version}: saved Locale/tables/binding, en → ja → en, PNGs and source hashes")

benchmark = REPO / "tests/fixtures/skill-routing/benchmark.jsonl"
assert benchmark.read_bytes() == (BUNDLE / "routing/cases.jsonl").read_bytes(), "stale routing cases"
spec = importlib.util.spec_from_file_location("routing_evidence", REPO / "scripts/skill-eval/run-codex-routing.py")
routing = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = routing
spec.loader.exec_module(routing)
catalog = routing.format_skill_catalog(routing.load_skills(SKILLS))
assert catalog in (BUNDLE / "routing/prompt.txt").read_text(), "stale routing skill catalog"
print("PASS routing: recorded cases and skill catalog match this checkout")
