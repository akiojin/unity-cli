"""Private authentication fields for localhost E2E wire requests; never log them."""
import json
import os
from pathlib import Path


def process_alive(pid):
    """Observe a process without sending Windows console-control events."""
    if pid <= 0 or pid > 0xFFFFFFFF:
        return False
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        kernel32.WaitForSingleObject.restype = wintypes.DWORD
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        handle = kernel32.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE only
        if not handle:
            return ctypes.get_last_error() == 5  # Access denied to an existing process
        try:
            state = kernel32.WaitForSingleObject(handle, 0)
            if state == 0xFFFFFFFF:  # WAIT_FAILED
                raise ctypes.WinError(ctypes.get_last_error())
            return state == 258  # WAIT_TIMEOUT: the process has not exited
        finally:
            kernel32.CloseHandle(handle)
    try:
        os.kill(pid, 0)
        return True
    except (ProcessLookupError, OverflowError):
        return False


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
            if not process_alive(editor_pid):
                continue
            matches.append(token)
        except (OSError, ValueError, TypeError):
            continue
    if len(matches) > 1:
        raise RuntimeError("Multiple live Editor authentication files match the endpoint")
    return {"authToken": matches[0]} if matches else {}
