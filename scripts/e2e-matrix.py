"""Real macOS Editor compatibility matrix; artifacts and failed fixtures are retained."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
VERSIONS = ("2022.3.62f3", "6000.0.84f1", "6000.3.25f1", "6000.4.11f1",
            "6000.5.3f1", "6000.6.3f1", "6000.7.0a2", "6000.7.0b2")


def package_manifest(version, catalog, modules):
    deps = {"com.akiojin.unity-cli-bridge": "file:unity-cli-bridge"}
    for name in ("com.unity.visualeffectgraph", "com.unity.render-pipelines.universal",
                 "com.unity.ugui", "com.unity.test-framework", "com.unity.timeline"):
        selected = catalog.get(name, {}).get("version")
        if not selected:
            raise ValueError("Editor package catalog missing " + name)
        deps[name] = selected
    legacy = version.startswith("2022.")
    deps.update({"com.unity.addressables": "1.22.3" if legacy else "2.9.1",
                 "com.unity.inputsystem": "1.14.2" if legacy else "1.19.0",
                 "com.unity.recorder": "4.0.3" if legacy else "5.1.6",
                 "com.unity.ai.navigation": "1.1.5" if legacy else "2.0.13"})
    deps.update({name: "1.0.0" for name in modules if name.startswith("com.unity.modules.")})
    return {"dependencies": deps, "testables": ["com.akiojin.unity-cli-bridge"]}


def ready(value):
    return (isinstance(value, dict) and not value.get("error")
            and value.get("success") is not False
            and value.get("isCompiling") is False and value.get("isUpdating") is False
            and value.get("errorCount", 0) == 0)


def all_passed(results):
    return bool(results) and all(row.get("status") == "PASS" for row in results)


DEFAULT_SUITES = ("input", "timeline", "vfx", "eval", "hot-reload", "reload", "all-tools")
REAL_HOT_RELOAD_SUITES = ("hot-reload-apply", "hot-reload-apply-x64")


def requires_gui(suites):
    return bool(suites and "perf" in suites.split(","))


def prepare_perf_fixture(project):
    # This copied cache belongs to the source Editor's URP version. Let the target
    # Editor regenerate it as a new project, without its existing-project upgrade dialog.
    (project / "ProjectSettings/URPProjectSettings.asset").unlink(missing_ok=True)
    (project / ".unity").mkdir(exist_ok=True)
    (project / ".unity/perf-owned-project").write_text("Created by e2e-matrix.py\n")


def hot_reload_apply_suites(editor, version, args, destination):
    """Real method replacement, each in its own isolated Editor with the optional backend installed.

    Empty without --fsr-path. The x64 suite exists only when an x64 build of this version is
    available, so an absent Editor is never recorded as a skipped or passing suite."""
    if not args.fsr_path:
        return {}

    def command(name, unity, arch=None):
        result = ["env", "UNITY_PATH=" + str(unity), "bash", str(ROOT / "scripts/e2e-hot-reload-batch-host.sh"),
                  "--port", str(args.port + 1), "--expect", "supported", "--fsr-path", str(args.fsr_path),
                  "--artifacts", str(destination / name)]
        return result + (["--require-arch", arch] if arch else [])

    suites = {"hot-reload-apply": command("hot-reload-apply", editor)}
    if args.x64_editor_root:
        x64 = Path(args.x64_editor_root) / version / "Unity.app/Contents/MacOS/Unity"
        if x64.is_file():
            suites["hot-reload-apply-x64"] = command("hot-reload-apply-x64", x64, "X64")
    return suites


def selected_suites(requested, real):
    if not requested:
        return list(DEFAULT_SUITES) + list(real)
    names = requested.split(",")
    for name in names:
        if name in REAL_HOT_RELOAD_SUITES and name not in real:
            raise ValueError(name + " needs --fsr-path" + (" and an x64 Editor under --x64-editor-root" if name.endswith("x64") else ""))
    return names


def prepare(editor, destination):
    resources = editor.parent.parent / "Resources/PackageManager"
    catalog = json.loads((resources / "Editor/manifest.json").read_text())["packages"]
    version = editor.parents[3].name
    if not re.fullmatch(r"\d+\.\d+\.\d+[abfp]\d+", version):
        raise ValueError("Expected Hub Editor path containing version: " + str(editor))
    project = destination / "project"
    project.mkdir()
    source = ROOT / "UnityCliBridge"
    for name in ("Assets", "ProjectSettings"):
        shutil.copytree(source / name, project / name, ignore=shutil.ignore_patterns("Generated"))
    (project / "Assets/Editor").mkdir(exist_ok=True)
    shutil.copy2(ROOT / "tests/fixtures/hot-reload/HotReloadProbe.cs", project / "Assets/HotReloadProbe.cs")
    shutil.copy2(ROOT / "tests/fixtures/hot-reload/HotReloadE2EFixture.cs", project / "Assets/Editor/HotReloadE2EFixture.cs")
    (project / "Packages").mkdir()
    shutil.copytree(source / "Packages/unity-cli-bridge", project / "Packages/unity-cli-bridge")
    modules = [p.name for p in (resources / "BuiltInPackages").iterdir() if p.is_dir()]
    manifest = package_manifest(version, catalog, modules)
    (project / "Packages/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + version + "\n")
    return version, project, manifest


def stop_owned(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=20)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)


def run_suite(command, env, stream, timeout):
    process = subprocess.Popen(command, env=env, stdout=stream, stderr=subprocess.STDOUT,
                               start_new_session=True)
    try:
        return process.wait(timeout=timeout)
    finally:
        if process.poll() is None:
            # Shell suites spawn CLI children. Stop their owned process group too.
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=5)


def run_editor(editor, args, output, base_env):
    destination = Path(tempfile.mkdtemp(prefix=editor.parents[3].name + "-", dir=output))
    row = {"editor": str(editor), "status": "FAIL", "suites": [], "artifacts": str(destination)}
    host = None
    try:
        version, project, manifest = prepare(editor, destination)
        row.update(version=version, packages=manifest["dependencies"])
        if requires_gui(args.suites):
            prepare_perf_fixture(project)
        env = dict(base_env, UNITY_PROJECT_ROOT=str(project), UNITY_CLI_PORT=str(args.port),
                   UNITY_CLI_ALLOW_BATCH_HOST="1", UNITY_CLI_PORT_OVERRIDE=str(args.port),
                   UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(destination / "stop"))
        with socket.socket() as probe:
            probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            probe.bind(("127.0.0.1", args.port))
        log = destination / "editor.log"
        command = [str(editor)] + ([] if requires_gui(args.suites) else ["-batchmode"]) + ["-projectPath", str(project),
                   "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
                   "-logFile", str(log)]
        row["launch"] = command
        host = subprocess.Popen(command, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        row["pid"] = host.pid
        cli = [str(args.unity_cli), "--host", "127.0.0.1", "--port", str(args.port),
               "--timeout-ms", "10000", "--output", "json"]

        def raw(tool, payload):
            result = subprocess.run(cli + ["raw", tool, "--json", json.dumps(payload)],
                                    env=env, text=True, capture_output=True, timeout=20)
            if result.returncode:
                raise RuntimeError(result.stdout + result.stderr)
            return json.loads(result.stdout)

        deadline = time.monotonic() + args.startup_timeout
        last_error = "Editor not ready"
        while time.monotonic() < deadline:
            if host.poll() is not None:
                raise RuntimeError("Editor exited during import; see " + str(log))
            if log.exists():
                text = log.read_text(errors="replace")
                if re.search(r"\berror CS\d+:|Project has invalid dependencies:", text):
                    raise RuntimeError("Compilation or package resolution failed; see " + str(log))
            try:
                owners = subprocess.run(["lsof", "-nP", f"-iTCP:{args.port}", "-sTCP:LISTEN", "-t"],
                                        capture_output=True, text=True, timeout=5)
                if set(owners.stdout.split()) == {str(host.pid)}:
                    state = raw("get_compilation_state", {})
                    if ready(state):
                        row["compilation"] = state
                        row["editor_info"] = raw("get_editor_info", {})
                        actual = row["editor_info"]["unity"]["unityVersion"]
                        if actual != version:
                            raise ValueError(f"Wrong Editor: {actual} != {version}")
                        break
            except (RuntimeError, subprocess.TimeoutExpired, json.JSONDecodeError) as error:
                last_error = str(error)
            time.sleep(2)
        else:
            raise RuntimeError("Startup timeout: " + last_error)
        lock = json.loads((project / "Packages/packages-lock.json").read_text())["dependencies"]
        row["resolved_vfx"] = lock["com.unity.visualeffectgraph"]["version"]
        if row["resolved_vfx"] != manifest["dependencies"]["com.unity.visualeffectgraph"]:
            raise RuntimeError("Resolved VFX version differs from Editor catalog")
        row["suites"].append({"name": "compile", "status": "PASS"})
        runtime = raw("eval_csharp", {"code": "Type.GetType(\"Mono.Runtime\") != null", "mode": "expression"})
        row["runtime_probe"] = runtime
        if runtime.get("state") != "completed" or runtime.get("value") is not True:
            raise RuntimeError("Could not prove Mono Editor runtime: " + json.dumps(runtime))
        row["runtime"] = "Mono"
        suites = {
            "perf": ["python3", str(ROOT / "scripts/bench-editor-ops.py"), "--unity-cli", str(args.unity_cli),
                     "--port", str(args.port), "--project", str(project), "--focus", args.perf_focus,
                     "--out", str(destination / "perf.json")],
            "perf-eval": ["python3", str(ROOT / "scripts/bench-eval.py"), "--unity-cli", str(args.unity_cli),
                          "--port", str(args.port), "--require-frontmost", "--activate", "--budget", "editor_eval",
                          "--history", str(ROOT / ".unity/perf/editor-ops-history.jsonl"),
                          "--out", str(destination / "perf-eval.json")],
            "input": ["bash", str(ROOT / "scripts/e2e-input-tools.sh"), "--unity-cli", str(args.unity_cli), "--port", str(args.port)],
            "timeline": ["python3", str(ROOT / "scripts/e2e-timeline.py"), "--unity-cli", str(args.unity_cli), "--port", str(args.port)],
            "vfx": ["bash", str(ROOT / "scripts/e2e-vfx.sh"), "--unity-cli", str(args.unity_cli), "--port", str(args.port), "--project", str(project), "--artifacts", str(destination / "vfx")],
            "eval": ["python3", str(ROOT / "scripts/e2e-eval.py"), "--unity-cli", str(args.unity_cli), "--port", str(args.port)],
            "reload": ["python3", str(ROOT / "scripts/e2e-test-domain-reload.py"), "--cli", str(args.unity_cli), "--port", str(args.port), "--output", str(destination / "reload.json")],
            "hot-reload": ["python3", str(ROOT / "scripts/e2e-hot-reload.py"), "--unity-cli", str(args.unity_cli), "--port", str(args.port), "--project", str(project), "--expect", "missing"],
            "all-tools": ["bash", str(ROOT / "scripts/e2e-all-tools.sh"), "--unity-cli", str(args.unity_cli), "--port", str(args.port), "--skip-quit"],
        }
        real = hot_reload_apply_suites(editor, version, args, destination)
        suites.update(real)
        selected = selected_suites(args.suites, real)
        if "perf" in selected:
            selected.insert(selected.index("perf") + 1, "perf-eval")
        for name in selected:
            if name == "compile":
                continue
            suite_log = destination / (name + ".log")
            print(version, name, str(suite_log), flush=True)
            result = {"name": name, "command": suites[name], "log": str(suite_log), "status": "FAIL"}
            row["suites"].append(result)
            with suite_log.open("w") as stream:
                returncode = run_suite(suites[name], env, stream, args.suite_timeout)
            result.update(returncode=returncode, status="PASS" if returncode == 0 else "FAIL")
            if returncode:
                raise RuntimeError(name + " failed; see " + str(suite_log))
            # Suites mutate scenes. Persist the owned fixture before the next suite's
            # clean-scene precondition; never apply this to a user's open project.
            if name != "all-tools":
                saved = raw("save_scene", {})
                if saved.get("error") or saved.get("success") is False:
                    raise RuntimeError("Could not save isolated suite fixture: " + json.dumps(saved))
        row["status"] = "PASS" if all_passed(row["suites"]) else "FAIL"
    except Exception as error:
        row["error"] = str(error)
        print("FAIL", str(editor), str(error), flush=True)
    finally:
        stop_owned(host)
        row["finished_at"] = datetime.now(timezone.utc).isoformat()
        (destination / "result.json").write_text(json.dumps(row, indent=2) + "\n")
    return row


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor", type=Path, action="append", help="Hub Unity executable; repeat to select multiple Editors")
    parser.add_argument("--unity-cli", type=Path, default=ROOT / "target/debug/unity-cli")
    parser.add_argument("--lsp-root", type=Path, help="Directory containing built csharp-lsp/<platform>/server; not needed for --suites perf")
    parser.add_argument("--output", type=Path, help="New evidence directory (must not exist)")
    parser.add_argument("--port", type=int, default=6508)
    parser.add_argument("--startup-timeout", type=int, default=900)
    parser.add_argument("--suite-timeout", type=int, default=1800)
    parser.add_argument("--fsr-path", type=Path, default=os.environ.get("UNITY_CLI_FSR_PATH"),
                        help="Unmodified FastScriptReload 1.8.0 Assets directory; adds the real hot-reload-apply suites")
    parser.add_argument("--x64-editor-root", type=Path, default=os.environ.get("UNITY_CLI_X64_EDITOR_ROOT"),
                        help="Directory holding <version>/Unity.app x64 Editors; adds hot-reload-apply-x64 where present")
    parser.add_argument("--suites", help="Focused comma-separated suites; omitted runs the complete matrix")
    parser.add_argument("--perf-focus", choices=["frontmost", "background", "both"], default="both",
                        help="Focus conditions for the GUI perf suite; release acceptance requires both")
    args = parser.parse_args()
    args.unity_cli = args.unity_cli.resolve()
    editors = args.editor or [Path(f"/Applications/Unity/Hub/Editor/{v}/Unity.app/Contents/MacOS/Unity") for v in VERSIONS]
    if not args.unity_cli.is_file() or any(not p.is_file() for p in editors):
        parser.error("Build the CLI and install every selected Editor first")
    needs_lsp = args.suites != "perf"
    if needs_lsp and (args.lsp_root is None or not (args.lsp_root / "csharp-lsp").is_dir()):
        parser.error("--lsp-root must contain a built csharp-lsp directory")
    if args.fsr_path and not (args.fsr_path / "package.json").is_file():
        parser.error("--fsr-path must be the FastScriptReload Assets directory containing package.json")
    allowed = {"compile", "perf", *DEFAULT_SUITES, *REAL_HOT_RELOAD_SUITES}
    if args.suites and not set(args.suites.split(",")) <= allowed:
        parser.error("Unknown suite; choose from " + ",".join(sorted(allowed)))
    output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="unity-cli-matrix-"))
    if args.output:
        output.mkdir(parents=True, exist_ok=False)
    print("Evidence:", output, flush=True)
    tools = output / "tools"
    if needs_lsp:
        shutil.copytree(args.lsp_root / "csharp-lsp", tools / "csharp-lsp")
    else:
        tools.mkdir()
    env = dict(os.environ, UNITY_CLI_TOOLS_ROOT=str(tools), UNITY_CLI_NO_AUTO_UPDATE="1", UNITY_CLI=str(args.unity_cli))
    report = {"started_at": datetime.now(timezone.utc).isoformat(), "full_matrix": not bool(args.suites), "editors": []}
    daemon = None
    lsp_daemon = None
    try:
        with (output / "unityd.log").open("w") as log, (output / "lspd.log").open("w") as lsp_log:
            daemon = subprocess.Popen([str(args.unity_cli), "unityd", "serve"], env=env, stdout=log, stderr=subprocess.STDOUT)
            if needs_lsp:
                lsp_daemon = subprocess.Popen([str(args.unity_cli), "lspd", "serve"], env=env, stdout=lsp_log, stderr=subprocess.STDOUT)
            time.sleep(1)
            if daemon.poll() is not None or (lsp_daemon is not None and lsp_daemon.poll() is not None):
                raise RuntimeError("Owned unityd/lspd failed to start")
            for editor in editors:
                report["editors"].append(run_editor(editor.resolve(), args, output, env))
                (output / "matrix.json").write_text(json.dumps(report, indent=2) + "\n")
    finally:
        stop_owned(lsp_daemon)
        stop_owned(daemon)
    report["status"] = "PASS" if all_passed(report["editors"]) else "FAIL"
    (output / "matrix.json").write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
