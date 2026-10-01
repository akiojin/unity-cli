#!/usr/bin/env python3
"""Check recorded #377 artifacts; this does not replace the live Editor run."""

import hashlib
import json
from pathlib import Path
import struct


def check():
    root = Path(__file__).resolve().parent
    for version in ("6000.3.25f1", "2022.3.62f3"):
        folder = root / version
        evidence = json.loads((folder / "evidence.json").read_text())
        assert evidence["editor"] == version
        for name, expected in evidence["files"].items():
            data = (folder / name).read_bytes()
            assert len(data) == expected["bytes"], name
            assert hashlib.sha256(data).hexdigest() == expected["sha256"], name
        frames = [(folder / f"game-{phase}-accepted.png").read_bytes()
                  for phase in ("before", "after")]
        for data in frames:
            assert data[:8] == b"\x89PNG\r\n\x1a\n"
            assert struct.unpack(">II", data[16:24]) == (1280, 720)
        assert frames[0] != frames[1], "No visual state change"
        log = (folder / "accepted-ui.log").read_text()
        for required in ("set_ui_element_value", "click_ui_element",
                         "get_ui_element_state", '"text": "Welcome"',
                         '"text": "Hello, Ada!"', '"value": "Ada"',
                         "ScreenCapture.CaptureScreenshot", '"isPlaying": false',
                         '"isDirty": false'):
            assert required in log, (version, required)
        for phase in ("construction", "verification"):
            events = json.loads((folder / f"{phase}.json").read_text())
            result = next(event for event in events if event["type"] == "result")
            assert not result["is_error"] and not result["permission_denials"]
        uss = (folder / "Screen.uss").read_text()
        assert "#player-name .unity-base-text-field__input" in uss
        assert "color: rgb(28, 35, 48)" in uss
        state = json.loads((folder / "independent-state.json").read_text())
        assert state["get_compilation_state"]["errorCount"] == 0
        assert state["get_compilation_state"]["isCompiling"] is False
        assert state["get_scene_info"]["scenePath"] == "Assets/Scenes/Generated/E2E/ToolkitDemo.unity"
        assert state["get_scene_info"]["isDirty"] is False
        assert state["get_editor_state"]["state"]["isPlaying"] is False
        print(f"PASS {version}: hashes, 2 PNGs, UI state/input/click, clean exit, source assets")


if __name__ == "__main__":
    check()
