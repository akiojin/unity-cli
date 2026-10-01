#!/usr/bin/env python3
"""Small framed TCP Editor stand-in for process lifecycle integration tests."""
import json
import os
from pathlib import Path
import socket
import struct
import sys
import time

root = Path(sys.argv[sys.argv.index("-projectPath") + 1]).resolve()
listener = socket.socket()
listener.bind(("127.0.0.1", 0))
listener.listen()
listener.settimeout(30)
port = listener.getsockname()[1]
directory = Path(os.environ["UNITY_CLI_EDITORS_DIR"])
directory.mkdir(exist_ok=True)
lock = directory / (str(os.getpid()) + ".json")
lock.write_text(json.dumps({"pid": os.getpid(), "projectPath": str(root),
                           "host": "127.0.0.1", "port": port,
                           "state": "ready", "heartbeatAt": time.time()}))
mode = os.environ.get("LIFECYCLE_MODE", "ready")


def read_exact(connection, size):
    data = b""
    while len(data) < size:
        block = connection.recv(size - len(data))
        if not block:
            raise EOFError()
        data += block
    return data


try:
    while True:
        connection, _ = listener.accept()
        with connection:
            connection.settimeout(5)
            while True:
                try:
                    length = struct.unpack(">I", read_exact(connection, 4))[0]
                    request = json.loads(read_exact(connection, length))
                    tool = request["type"]
                    if mode == "silent":
                        continue
                    response = {"id": request["id"], "status": "success"}
                    if tool == "ping":
                        response["result"] = {"projectPath": str(root), "supportsSafeClose": True}
                    elif tool == "get_editor_state":
                        response["result"] = {"state": {"isCompiling": False, "isUpdating": False}}
                    elif tool == "quit_editor":
                        response["result"] = {"message": "quitting"}
                    else:
                        raise AssertionError(tool)
                    payload = json.dumps(response).encode()
                    connection.sendall(struct.pack(">I", len(payload)) + payload)
                    if tool == "quit_editor":
                        # Prove the CLI waits for the process, beyond the TCP response.
                        time.sleep(0.3)
                        sys.exit(0)
                except (EOFError, socket.timeout, ConnectionError):
                    break
finally:
    lock.unlink(missing_ok=True)
