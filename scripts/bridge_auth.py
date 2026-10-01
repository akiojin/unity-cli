"""Private authentication fields for localhost E2E wire requests; never log them."""
import json
import os
from pathlib import Path


def auth_fields(port, editors_dir=None, pid=None):
    folder = Path(editors_dir or os.environ.get("UNITY_CLI_EDITORS_DIR", Path.home() / ".unity-cli/editors"))
    matches = []
    for path in folder.glob("*.json"):
        try:
            lock = json.loads(path.read_text())
            if not isinstance(lock, dict):
                continue
            editor_pid = int(lock.get("pid", 0))
            token = lock.get("authToken")
            if (editor_pid <= 0 or (pid is not None and editor_pid != pid)
                    or lock.get("host") not in ("127.0.0.1", "localhost")
                    or int(lock.get("port", 0)) != int(port)
                    or not isinstance(token, str) or not token):
                continue
            os.kill(editor_pid, 0)
            matches.append(token)
        except (OSError, ValueError, TypeError):
            continue
    if len(matches) > 1:
        raise RuntimeError("Multiple live Editor authentication files match the endpoint")
    return {"authToken": matches[0]} if matches else {}
