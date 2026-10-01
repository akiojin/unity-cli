"""Check the recorded URP evidence; this does not rerun the real Editors."""

import hashlib
import json
from pathlib import Path
from zipfile import ZipFile


evidence = Path(__file__).resolve().parent
repository = evidence.parents[2]


def read_json(path):
    return json.loads(path.read_text())


for path, expected in read_json(evidence / "skill-hashes.json").items():
    assert hashlib.sha256((repository / path).read_bytes()).hexdigest() == expected, path

for version in ("2022.3.62f3", "6000.3.25f1"):
    directory = evidence / version
    with ZipFile(directory / "assets.zip") as assets:
        for path, expected in read_json(directory / "asset-hashes.json").items():
            assert hashlib.sha256(assets.read(path)).hexdigest() == expected, path
        for name in ("independent-state.json", "restart-state.json"):
            response = read_json(directory / name)
            assert response["state"] == "completed", (version, name)
            state = response["value"]
            assert state["unityVersion"] == version
            assert not state["playing"] and not state["dirty"]
            assert state["graphics"] == state["effective"]
            assert len(state["qualities"]) == 6
            assert all(path == state["graphics"] for path in state["qualities"])
            for key in (
                "rendererResources", "hdr", "cameraPostProcessing", "cameraHDR",
                "cameraSeesVolume", "volumeGlobal", "bloomActive",
                "intensityOverride", "thresholdOverride", "bloomPersistent",
            ):
                assert state[key], (version, name, key)
            assert state["intensity"] == 2 and state["threshold"] == 1
            assert state["volumeWeight"] == 1
            for key in ("scene", "graphics", "renderer", "volumeProfile"):
                assert state[key] in assets.namelist(), (version, key)
    before = (directory / "before.png").read_bytes()
    after = (directory / "after.png").read_bytes()
    assert before.startswith(b"\x89PNG\r\n\x1a\n")
    assert after.startswith(b"\x89PNG\r\n\x1a\n") and before != after
    print(f"PASS {version}: asset hashes, saved/restarted state, distinct PNG pair")

print("PASS: recorded skill hashes match the checkout")
