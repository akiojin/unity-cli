#!/usr/bin/env python3
"""Editor discovery E2E (#362) against two real Unity Editors.

Creates two throwaway projects that reference the local unity-cli-bridge package,
opens them as batch hosts configured to the SAME port, and verifies:

  AC-1 --project-path routes each command to the right Editor
  AC-2 the second Bridge falls back to another port; `instances list` (no --ports)
       lists both with project paths from the lockfiles
  AC-3 cwd inside a project selects that Editor without --project-path
  AC-4 cwd outside every project -> AMBIGUOUS_EDITOR (exit 6) with candidates, and
       the mutating command reaches neither Editor
  AC-5 after SIGKILL the killed Editor is listed as `unreachable`
  plus: `--ports` / `set-active` keep working, and a graceful quit deletes the lockfile

Usage:
  cargo build --bin unity-cli
  python3 scripts/e2e-editor-discovery.py \
      --unity-a 6000.3.25f1 --unity-b 2022.3.62f3 --port 6470
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
PACKAGE_DIR = REPO_ROOT / "UnityCliBridge" / "Packages" / "unity-cli-bridge"

KEEP_ALIVE = r"""
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DiscoveryE2EHost
{
    private static string stopFile;

    public static void Run()
    {
        var project = Path.GetFileName(Path.GetDirectoryName(Application.dataPath));
        new GameObject("Marker_" + project);
        stopFile = Environment.GetEnvironmentVariable("DISCOVERY_E2E_STOP_FILE");
        EditorApplication.update += Tick;
        Debug.Log("DiscoveryE2EHost ready for " + project);
    }

    private static void Tick()
    {
        if (!string.IsNullOrEmpty(stopFile) && File.Exists(stopFile))
        {
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}
"""

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    results.append((name, ok, detail))
    print(f"[{'PASS' if ok else 'FAIL'}] {name}{': ' + detail if detail else ''}", flush=True)


def unity_binary(version: str) -> str:
    path = f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
    if not os.access(path, os.X_OK):
        sys.exit(f"Unity {version} not found at {path}")
    return path


def make_project(root: Path, name: str) -> Path:
    project = root / name
    (project / "Assets" / "Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    # Embed a copy: Unity rewrites .meta files of a package referenced in place.
    shutil.copytree(PACKAGE_DIR, project / "Packages" / "com.akiojin.unity-cli-bridge")
    (project / "Packages" / "manifest.json").write_text(json.dumps({"dependencies": {}}, indent=2))
    (project / "Assets" / "Editor" / "DiscoveryE2EHost.cs").write_text(KEEP_ALIVE)
    return project


def cli(args: list[str], cwd: Path, env: dict[str, str]) -> tuple[int, str, str]:
    proc = subprocess.run(
        [str(args[0]), *args[1:]], cwd=cwd, env=env, capture_output=True, text=True, timeout=180
    )
    return proc.returncode, proc.stdout, proc.stderr


def lockfile_for(editors_dir: Path, pid: int) -> dict | None:
    path = editors_dir / f"{pid}.json"
    try:
        return json.loads(path.read_text())
    except (OSError, json.JSONDecodeError):
        return None


def wait_for(predicate, timeout: float, what: str, interval: float = 2.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(interval)
    raise TimeoutError(f"timed out waiting for {what}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity-a", default="6000.3.25f1")
    parser.add_argument("--unity-b", default="2022.3.62f3")
    parser.add_argument("--port", type=int, default=6470)
    parser.add_argument("--unity-cli", default=str(REPO_ROOT / "target" / "debug" / "unity-cli"))
    parser.add_argument("--keep", action="store_true", help="keep the temp projects")
    args = parser.parse_args()

    unity_cli = Path(args.unity_cli)
    if not os.access(unity_cli, os.X_OK):
        sys.exit(f"unity-cli not found: {unity_cli} (run cargo build first)")

    work = Path(tempfile.mkdtemp(prefix="unity-cli-discovery-e2e-")).resolve()
    editors_dir = Path(os.environ.get("UNITY_CLI_EDITORS_DIR", Path.home() / ".unity-cli" / "editors"))
    print(f"work dir: {work}\nlockfile dir: {editors_dir}", flush=True)
    project_a = make_project(work, "ProjectA")
    project_b = make_project(work, "ProjectB")
    stop_file = work / "stop"

    cli_env = {k: v for k, v in os.environ.items() if not k.startswith("UNITY_CLI_") and k != "UNITY_PROJECT_ROOT"}
    cli_env["UNITY_CLI_REGISTRY_PATH"] = str(work / "instances.json")
    cli_env["UNITY_CLI_NO_AUTO_UPDATE"] = "1"
    if "UNITY_CLI_EDITORS_DIR" in os.environ:
        cli_env["UNITY_CLI_EDITORS_DIR"] = os.environ["UNITY_CLI_EDITORS_DIR"]

    hosts: dict[str, subprocess.Popen] = {}

    def launch(name: str, project: Path, version: str) -> subprocess.Popen:
        env = dict(cli_env)
        env.update(
            UNITY_CLI_ALLOW_BATCH_HOST="1",
            UNITY_CLI_PORT_OVERRIDE=str(args.port),
            DISCOVERY_E2E_STOP_FILE=str(stop_file),
        )
        log = work / f"{name}.log"
        proc = subprocess.Popen(
            [
                unity_binary(version), "-batchmode", "-nographics", "-projectPath", str(project),
                "-executeMethod", "DiscoveryE2EHost.Run", "-logFile", str(log),
            ],
            env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
        hosts[name] = proc
        print(f"launched {name} ({version}) pid={proc.pid} log={log}", flush=True)

        def ready():
            if proc.poll() is not None:
                raise RuntimeError(f"{name} exited early; see {log}")
            lock = lockfile_for(editors_dir, proc.pid)
            if not lock or lock.get("state") != "ready":
                return None
            code, out, _ = cli(
                [unity_cli, "--output", "json", "--port", str(lock["port"]), "raw", "get_hierarchy",
                 "--json", "{}"], work, cli_env)
            return lock if code == 0 and f"Marker_{project.name}" in out else None

        return wait_for(ready, 900, f"{name} to publish its lockfile and answer")

    try:
        lock_a = launch("ProjectA", project_a, args.unity_a)
        lock_b = launch("ProjectB", project_b, args.unity_b)

        # AC-2: same configured port, second Bridge falls back.
        check("AC-2 A listens on the configured port", lock_a["port"] == args.port, json.dumps(lock_a))
        check(
            "AC-2 B falls back to another port",
            lock_b["port"] != args.port and lock_b["configuredPort"] == args.port,
            json.dumps(lock_b),
        )
        check("lockfile records Unity versions",
              lock_a["unityVersion"] == args.unity_a and lock_b["unityVersion"] == args.unity_b)
        time.sleep(7)
        refreshed = lockfile_for(editors_dir, hosts["ProjectA"].pid) or {}
        check("heartbeat is refreshed in the background",
              refreshed.get("heartbeatAt", 0) > lock_a["heartbeatAt"], json.dumps(refreshed))

        code, out, err = cli([unity_cli, "--output", "json", "instances", "list"], work, cli_env)
        listed = json.loads(out)["data"] if code == 0 else []
        by_project = {Path(entry.get("project_path", "")).name: entry for entry in listed}
        check(
            "AC-2 instances list (no --ports) lists both Editors with project paths",
            code == 0
            and by_project.get("ProjectA", {}).get("status") == "up"
            and by_project.get("ProjectB", {}).get("status") == "up"
            and by_project["ProjectA"]["port"] == lock_a["port"]
            and by_project["ProjectB"]["port"] == lock_b["port"],
            out.strip() or err.strip(),
        )

        # AC-1: --project-path routing.
        for project, other in ((project_a, project_b), (project_b, project_a)):
            code, out, err = cli(
                [unity_cli, "--output", "json", "--project-path", str(project), "raw", "get_hierarchy",
                 "--json", "{}"], work, cli_env)
            check(
                f"AC-1 --project-path {project.name} returns its own scene",
                code == 0 and f"Marker_{project.name}" in out and f"Marker_{other.name}" not in out,
                err.strip(),
            )

        # AC-3: cwd inside a project.
        for project, other in ((project_a, project_b), (project_b, project_a)):
            code, out, err = cli(
                [unity_cli, "--output", "json", "raw", "get_hierarchy", "--json", "{}"],
                project / "Assets", cli_env)
            check(
                f"AC-3 cwd {project.name}/Assets selects its Editor",
                code == 0 and f"Marker_{project.name}" in out and f"Marker_{other.name}" not in out,
                err.strip(),
            )

        # AC-4: ambiguous outside every project; nothing is executed.
        code, out, err = cli(
            [unity_cli, "--output", "json", "raw", "create_gameobject", "--json",
             '{"name":"ShouldNotExist"}'], work, cli_env)
        payload = json.loads(out) if out.strip().startswith("{") else {}
        candidates = payload.get("data", {}).get("candidates", [])
        check(
            "AC-4 AMBIGUOUS_EDITOR with exit 6 and candidates (projectPath, port, pid)",
            code == 6
            and (payload.get("errors") or [{}])[0].get("code") == "AMBIGUOUS_EDITOR"
            and {Path(c["projectPath"]).name for c in candidates} == {"ProjectA", "ProjectB"}
            and all(c.get("port") and c.get("pid") for c in candidates),
            out.strip(),
        )
        for project in (project_a, project_b):
            code, out, _ = cli(
                [unity_cli, "--output", "json", "--project-path", str(project), "raw", "get_hierarchy",
                 "--json", "{}"], work, cli_env)
            check(f"AC-4 nothing executed in {project.name}", code == 0 and "ShouldNotExist" not in out)

        # Backward compatibility: --ports and set-active host:port.
        code, out, _ = cli(
            [unity_cli, "--output", "json", "instances", "list", "--ports",
             f"{lock_a['port']},{lock_b['port']}"], work, cli_env)
        check("compat instances list --ports", code == 0 and len(json.loads(out)["data"]) == 2, out.strip())
        code, out, err = cli(
            [unity_cli, "--output", "json", "instances", "set-active", f"127.0.0.1:{lock_b['port']}"],
            work, cli_env)
        check("compat set-active host:port", code == 0, (out + err).strip())
        code, out, err = cli(
            [unity_cli, "--output", "json", "raw", "get_hierarchy", "--json", "{}"], work, cli_env)
        check("compat active instance is used outside projects", code == 0 and "Marker_ProjectB" in out,
              err.strip())
        (work / "instances.json").unlink(missing_ok=True)

        # AC-5: force-kill A.
        hosts["ProjectA"].send_signal(signal.SIGKILL)
        hosts["ProjectA"].wait(timeout=30)
        code, out, err = cli([unity_cli, "--output", "json", "instances", "list"], work, cli_env)
        listed = json.loads(out)["data"] if code == 0 else []
        by_project = {Path(entry.get("project_path", "")).name: entry for entry in listed}
        check(
            "AC-5 force-killed Editor is listed as unreachable",
            by_project.get("ProjectA", {}).get("status") == "unreachable"
            and by_project.get("ProjectB", {}).get("status") == "up",
            out.strip() or err.strip(),
        )
        code, out, err = cli(
            [unity_cli, "--output", "json", "raw", "get_hierarchy", "--json", "{}"], work, cli_env)
        check("stale lockfile does not cause ambiguity", code == 0 and "Marker_ProjectB" in out, err.strip())

        # Domain reload keeps the lockfile and the fallback port even though the
        # configured port is free again (A is gone).
        pid_b = hosts["ProjectB"].pid
        (project_b / "Assets" / "Editor" / "ForceReload.cs").write_text(
            "public static class ForceReload { public const int Value = 1; }\n")
        cli([unity_cli, "--output", "json", "--project-path", str(project_b), "raw", "refresh_assets",
             "--json", "{}"], work, cli_env)
        states: set[str] = set()

        def reloaded():
            lock = lockfile_for(editors_dir, pid_b)
            if lock:
                states.add(lock.get("state", ""))
            if not lock or lock.get("state") != "ready" or "reloading" not in states:
                return None
            code, out, _ = cli(
                [unity_cli, "--output", "json", "--project-path", str(project_b), "raw",
                 "get_hierarchy", "--json", "{}"], work, cli_env)
            return lock if code == 0 and "Marker_ProjectB" in out else None

        try:
            after = wait_for(reloaded, 300, "ProjectB domain reload", interval=0.1)
            check("domain reload marks the lockfile reloading, then ready on the same port",
                  after["port"] == lock_b["port"], f"states={sorted(states)} port={after['port']}")
        except TimeoutError as error:
            check("domain reload marks the lockfile reloading, then ready on the same port", False,
                  f"{error}; states={sorted(states)}")

        # Graceful quit removes the lockfile.
        cli([unity_cli, "--output", "json", "--project-path", str(project_b), "raw", "quit_editor",
             "--json", "{}"], work, cli_env)
        hosts["ProjectB"].wait(timeout=180)
        check("graceful quit deletes the lockfile", lockfile_for(editors_dir, pid_b) is None)
    finally:
        stop_file.touch()
        for proc in hosts.values():
            if proc.poll() is None:
                try:
                    proc.wait(timeout=60)
                except subprocess.TimeoutExpired:
                    proc.kill()
        # The killed Editor's lockfile is expected to remain; remove it so it does not linger.
        for proc in hosts.values():
            (editors_dir / f"{proc.pid}.json").unlink(missing_ok=True)
        if not args.keep:
            shutil.rmtree(work, ignore_errors=True)

    failed = [name for name, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} checks passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
