#!/usr/bin/env python3
"""Validate recorded real-Editor acceptance artifacts; does not rerun Unity."""

import hashlib
import json
from pathlib import Path


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PACKAGE = "com.cysharp.unitask"
PHASES = ("baseline", "registry", "install", "update", "remove")
EXPECTED = (None, None, "2.5.4", "2.5.5", None)
REGISTRY = {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": ["com.cysharp"],
}


def read(path):
    return json.loads(path.read_text())


def check_version(version):
    base = HERE / version
    for name, digest in read(base / "checksums.json").items():
        assert hashlib.sha256((base / name).read_bytes()).hexdigest() == digest, name
    transcript = read(base / "claude-tool-calls.json")
    assert not transcript["result"]["is_error"]
    assert not transcript["result"]["permission_denials"]
    assert any(
        call["name"] == "Skill"
        and call["input"].get("skill") == "unity-package-management"
        for call in transcript["calls"]
    ), "Missing actual Skill invocation"
    baseline = read(base / "baseline/manifest.json")
    assert not baseline.get("scopedRegistries")
    assert PACKAGE not in baseline["dependencies"]
    for phase, expected in zip(PHASES, EXPECTED):
        directory = base / phase
        manifest = read(directory / "manifest.json")
        wanted = json.loads(json.dumps(baseline))
        if phase != "baseline":
            wanted["scopedRegistries"] = [REGISTRY]
        if expected:
            wanted["dependencies"][PACKAGE] = expected
        assert manifest == wanted, (version, phase, "unexpected manifest change")
        packages = read(directory / "package-list.json")
        assert packages["success"]
        found = [p for p in packages["packages"] if p["name"] == PACKAGE]
        locked = read(directory / "packages-lock.json")["dependencies"].get(PACKAGE)
        if expected:
            assert len(found) == 1 and found[0]["version"] == expected
            assert found[0]["isDirectDependency"] and found[0]["source"] == "Registry"
            assert locked["version"] == expected and locked["depth"] == 0
            assert locked["url"] == REGISTRY["url"]
            response = read(directory / ("install.json" if phase == "install" else "install-2.5.5.json"))
            assert response["success"] and response["version"] == expected
        else:
            assert not found and locked is None
        state = read(directory / "editor-state-final.json")["state"]
        assert not state["isCompiling"] and not state["isUpdating"] and not state["isPlaying"]
        console = read(directory / "console-errors.json")
        assert "logs" in console
        # Console severity labels include pre-existing compiler warnings and
        # transport warnings; the raw messages remain available for review.
        assert not any("error CS" in entry.get("message", "") for entry in console["logs"])
    compilation = read(base / "independent-get_compilation_state.json")
    assert compilation["success"] and compilation["errorCount"] == 0
    assert not compilation["isCompiling"] and not compilation["isUpdating"]
    assert not read(base / "provenance.json")["compiler_error_lines"]
    assert read(base / "independent-registry_config.json")["registries"] == [REGISTRY]
    assert not any(
        p["name"] == PACKAGE
        for p in read(base / "independent-package_manager.json")["packages"]
    )
    assert read(base / "remove/remove.json")["success"]
    assert (base / "manifest.diff").stat().st_size > 0
    print(f"PASS {version}: Skill invocation, five manifest/lock/package states, idle state, compiler evidence")


if __name__ == "__main__":
    for name, digest in read(HERE / "skill-hashes.json").items():
        assert hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == digest, name
    for editor in ("6000.3.25f1", "2022.3.62f3"):
        check_version(editor)
