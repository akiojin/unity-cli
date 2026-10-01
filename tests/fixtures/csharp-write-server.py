#!/usr/bin/env python3
"""LSP protocol fixture using full-file replacement for CLI boundary tests."""
import json
import os
from pathlib import Path
import sys


def receive():
    headers = {}
    while True:
        line = sys.stdin.buffer.readline()
        if not line:
            return None
        if line in (b"\r\n", b"\n"):
            break
        key, value = line.decode().split(":", 1)
        headers[key.lower()] = value.strip()
    return json.loads(sys.stdin.buffer.read(int(headers["content-length"])))


while (request := receive()) is not None:
    if "id" not in request:
        continue
    if request["method"] == "initialize":
        result = {"capabilities": {}}
    else:
        params = request["params"]
        files = params.get("files", [{"relative": params["relative"]}] if "relative" in params else [])
        applied = params.get("apply", True)
        if applied:
            for item in files:
                path = Path(os.environ["UNITY_PROJECT_ROOT"]) / item["relative"]
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(item.get("newText", params.get("newText", params.get("text", ""))))
        result = {
            "success": True,
            "applied": applied,
            "changedFiles": [item["relative"] for item in files] if applied else [],
            "diagnostics": [],
        }
    data = json.dumps({"jsonrpc": "2.0", "id": request["id"], "result": result}).encode()
    sys.stdout.buffer.write(f"Content-Length: {len(data)}\r\n\r\n".encode() + data)
    sys.stdout.buffer.flush()
