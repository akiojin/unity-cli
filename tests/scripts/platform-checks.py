#!/usr/bin/env python3
"""Editor-less platform checks for Windows / Linux CI runners (#459).

Drives a built unity-cli binary; no Unity Editor is needed:

  discovery   fake lockfiles under the home directory (#362): a live PID is
              listed as discovered, an exited PID is listed as unreachable and
              excluded from target selection
  setup       `setup --dry-run` prints the Bridge install plan and writes
              nothing (#363)
  screenshot  a silent fake Editor makes `capture_screenshot` time out and fall
              back to an OS screenshot that must be a real PNG (#369)

Unix isolates the home directory through $HOME. Windows resolves the profile
through the shell API instead of %USERPROFILE%, so the lockfiles go into the
real profile; pass --real-home (CI runners only) to allow that.

Usage:
  cargo build --bin unity-cli
  python3 tests/scripts/platform-checks.py --unity-cli target/debug/unity-cli
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

CHECKS = ("discovery", "setup", "screenshot")
IS_WINDOWS = os.name == "nt"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
# X11 tools the CLI tries on Linux, in order (src/tooling/os_capture.rs).
# The Wayland tools (grim, gnome-screenshot, spectacle) need a compositor that
# CI runners do not have, so they stay with the real-machine checks in #386.
X11_TOOLS = ("import", "scrot", "maim")

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    results.append((name, ok, detail))
    print(f"[{'PASS' if ok else 'FAIL'}] {name}{': ' + detail if detail else ''}", flush=True)


class Runner:
    def __init__(self, unity_cli: Path, work: Path, home: Path | None):
        self.unity_cli = str(unity_cli)
        self.work = work
        env = {
            key: value
            for key, value in os.environ.items()
            if not key.upper().startswith(("UNITY_CLI_", "UNITY_PROJECT_ROOT", "UNITY_EDITOR_PATH"))
        }
        if home is not None:
            env["HOME"] = str(home)
            env["XDG_CONFIG_HOME"] = str(home / ".config")
        env["UNITY_CLI_NO_AUTO_UPDATE"] = "1"
        env["UNITY_CLI_REGISTRY_PATH"] = str(work / "registry" / "instances.json")
        env["UNITY_CLI_TOOLS_ROOT"] = str(work / "tools")
        self.env = env

    def run(self, *args: str, path: str | None = None, timeout: int = 60) -> subprocess.CompletedProcess:
        env = dict(self.env)
        if path is not None:
            env["PATH"] = path
        return subprocess.run(
            [self.unity_cli, *args], cwd=self.work, env=env, text=True, capture_output=True, timeout=timeout
        )


def envelope(process: subprocess.CompletedProcess) -> dict:
    try:
        return json.loads(process.stdout)
    except json.JSONDecodeError:
        return {"unparsed": process.stdout, "stderr": process.stderr}


def make_project(root: Path) -> Path:
    (root / "Assets").mkdir(parents=True)
    (root / "Packages").mkdir()
    (root / "ProjectSettings").mkdir()
    (root / "Packages" / "manifest.json").write_text('{"dependencies":{}}')
    (root / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.25f1\n")
    (root / "ProjectSettings" / "ProjectSettings.asset").write_text("PlayerSettings:\n  activeInputHandler: 0\n")
    return root


def free_port() -> int:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def write_lock(editors_dir: Path, pid: int, project: Path, port: int) -> Path:
    """Same shape as the Bridge's EditorLockfile.BuildContent, without a token."""
    lockfile = editors_dir / f"{pid}.json"
    lockfile.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "pid": pid,
                "projectPath": str(project),
                "host": "127.0.0.1",
                "port": port,
                "configuredPort": 6400,
                "unityVersion": "6000.3.25f1",
                "bridgeVersion": "0.0.0-ci",
                "state": "ready",
                "startedAt": time.time(),
                "heartbeatAt": time.time(),
            }
        )
    )
    return lockfile


def check_discovery(runner: Runner, home: Path) -> None:
    editors_dir = home / ".unity-cli" / "editors"
    editors_dir.mkdir(parents=True, exist_ok=True)
    live_project = make_project(runner.work / "LiveProject")
    dead_project = make_project(runner.work / "DeadProject")

    live = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(300)"])
    exited = subprocess.Popen([sys.executable, "-c", "pass"])
    exited.wait()
    lockfiles = [
        write_lock(editors_dir, live.pid, live_project, free_port()),
        write_lock(editors_dir, exited.pid, dead_project, free_port()),
    ]
    try:
        listed = runner.run("--output", "json", "instances", "list")
        report = envelope(listed)
        entries = report.get("data") if isinstance(report.get("data"), list) else []
        by_pid = {entry.get("pid"): entry for entry in entries}
        check("discovery: instances list succeeds", listed.returncode == 0 and report.get("success") is True,
              f"exit={listed.returncode} {listed.stderr.strip()}")

        found = by_pid.get(live.pid, {})
        check(
            "discovery: live PID is discovered from its lockfile",
            found.get("source") == "lockfile"
            and "stale_reason" not in found
            and found.get("status") != "unreachable"
            and same_path(Path(found.get("lockfile", "")), lockfiles[0]),
            json.dumps(found),
        )
        stale = by_pid.get(exited.pid, {})
        check(
            "discovery: exited PID is listed as unreachable",
            stale.get("status") == "unreachable" and "not running" in stale.get("stale_reason", ""),
            json.dumps(stale),
        )

        routed = runner.run(
            "--output", "json", "--project-path", str(dead_project), "raw", "get_editor_state", "--json", "{}"
        )
        failure = envelope(routed)
        codes = [error.get("code") for error in failure.get("errors", [])]
        candidates = [item.get("pid") for item in (failure.get("data") or {}).get("candidates", [])]
        check(
            "discovery: exited PID is excluded from target selection",
            routed.returncode != 0 and codes == ["EDITOR_NOT_FOUND"] and candidates == [live.pid],
            f"exit={routed.returncode} codes={codes} candidates={candidates} live={live.pid}",
        )
    finally:
        live.kill()
        live.wait()
        for lockfile in lockfiles:
            lockfile.unlink(missing_ok=True)


def check_setup(runner: Runner) -> None:
    project = make_project(runner.work / "Setup Project")
    manifest = project / "Packages" / "manifest.json"
    before = manifest.read_bytes()
    # Nothing listens here, so a plan that tried to reach an Editor would fail.
    process = runner.run(
        "setup", "--dry-run", "--json", "--project-path", str(project), "--host", "127.0.0.1", "--port", str(free_port())
    )
    report = envelope(process)
    data = report.get("data") or {}
    steps = {step.get("step"): step for step in data.get("steps", [])}
    check("setup: --dry-run succeeds", process.returncode == 0 and report.get("success") is True,
          f"exit={process.returncode} {process.stderr.strip()}")
    check("setup: plan lists binary, bridge, launch and wait steps",
          data.get("dryRun") is True and list(steps) == ["binary", "bridge", "launch", "wait"], json.dumps(list(steps)))
    bridge = steps.get("bridge", {}).get("details", {})
    check(
        "setup: plan installs the Bridge into the project manifest",
        bridge.get("action") == "install"
        and bridge.get("package") == "com.akiojin.unity-cli-bridge"
        and bridge.get("manifestChanged") is True
        and same_path(Path(bridge.get("manifestPath", "")), manifest),
        json.dumps(bridge),
    )
    check("setup: no Editor is contacted", (data.get("editor") or {}).get("checked") is False, json.dumps(data.get("editor")))
    check("setup: the manifest is left untouched", manifest.read_bytes() == before)


class SilentEditor:
    """Accepts Bridge connections and never answers, like a blocked main thread."""

    def __init__(self) -> None:
        self.server = socket.socket()
        self.server.bind(("127.0.0.1", 0))
        self.server.listen()
        self.port = self.server.getsockname()[1]
        self.connections: list[socket.socket] = []
        threading.Thread(target=self._accept, daemon=True).start()

    def _accept(self) -> None:
        while True:
            try:
                connection, _ = self.server.accept()
            except OSError:
                return
            self.connections.append(connection)

    def close(self) -> None:
        self.server.close()
        for connection in self.connections:
            connection.close()


def same_path(left: Path, right: Path) -> bool:
    """True for one file or directory; the CLI reports extended-length paths on Windows."""
    try:
        return os.path.samefile(left, right)
    except OSError:
        return False


def png_size(path: Path) -> tuple[int, int] | None:
    data = path.read_bytes()
    if len(data) < 24 or data[:8] != PNG_SIGNATURE or data[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", data[16:24])


def check_screenshot(runner: Runner) -> None:
    if IS_WINDOWS:
        tools = {"powershell": None}
    elif sys.platform.startswith("linux"):
        tools = {}
        for tool in X11_TOOLS:
            resolved = shutil.which(tool)
            if resolved is None:
                check(f"screenshot: {tool} is installed", False, "not on PATH")
                continue
            # A PATH holding only this tool pins the fallback chain to it.
            bin_dir = runner.work / f"only-{tool}"
            bin_dir.mkdir()
            (bin_dir / tool).symlink_to(resolved)
            tools[tool] = str(bin_dir)
    else:
        check("screenshot: platform is supported", False, f"{sys.platform} is outside #459 (Windows / Linux only)")
        return

    project = make_project(runner.work / "CaptureProject")
    editor = SilentEditor()
    try:
        for tool, path in tools.items():
            process = runner.run(
                "--output", "json", "--host", "127.0.0.1", "--port", str(editor.port),
                "--project-path", str(project), "--timeout-ms", "3000",
                "raw", "capture_screenshot", "--json", '{"captureMode":"game"}',
                path=path, timeout=120,
            )
            report = envelope(process)
            data = report.get("data") or {}
            errors = "; ".join(error.get("message", "") for error in report.get("errors", []))
            image = Path(data.get("path") or project / "missing.png")
            size = png_size(image) if image.is_file() else None
            check(
                f"screenshot: {tool} writes a PNG after the Editor times out",
                process.returncode == 0
                and data.get("fallback") == "os"
                and data.get("fallbackTool") == tool
                and same_path(image.parent, project / ".unity" / "capture")
                and size is not None
                and min(size) > 0
                and [data.get("width"), data.get("height")] == list(size),
                f"exit={process.returncode} size={size} bytes={data.get('fileSize')} path={image} {errors}",
            )
    finally:
        editor.close()
        runner.run("unityd", "stop")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--unity-cli", required=True, type=Path, help="built unity-cli binary")
    parser.add_argument("--checks", default=",".join(CHECKS), help=f"comma separated subset of {','.join(CHECKS)}")
    parser.add_argument("--real-home", action="store_true",
                        help="Windows only: allow writing fake lockfiles into the real user profile")
    args = parser.parse_args()

    selected = [name.strip() for name in args.checks.split(",") if name.strip()]
    unknown = sorted(set(selected) - set(CHECKS))
    if unknown:
        parser.error(f"unknown checks: {', '.join(unknown)}")
    unity_cli = args.unity_cli.resolve()
    if not unity_cli.is_file():
        parser.error(f"unity-cli binary not found: {unity_cli}")
    if IS_WINDOWS and "discovery" in selected and not args.real_home:
        parser.error("the discovery check writes into the real user profile on Windows; pass --real-home on a CI runner")

    with tempfile.TemporaryDirectory(prefix="unity-cli-platform-checks-") as raw:
        # Resolve symlinked temp dirs so reported paths compare equal.
        work = Path(raw).resolve()
        if IS_WINDOWS:
            home, isolated_home = Path.home(), None
        else:
            home = isolated_home = work / "home"
            home.mkdir()
        runner = Runner(unity_cli, work, isolated_home)
        version = runner.run("--version")
        print(f"unity-cli: {version.stdout.strip()} ({sys.platform}, home={home})", flush=True)
        if "discovery" in selected:
            check_discovery(runner, home)
        if "setup" in selected:
            check_setup(runner)
        if "screenshot" in selected:
            check_screenshot(runner)

    failed = [name for name, ok, _ in results if not ok]
    print(f"{len(results) - len(failed)}/{len(results)} checks passed")
    return 1 if failed or not results else 0


if __name__ == "__main__":
    sys.exit(main())
