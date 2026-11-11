#!/usr/bin/env python3
"""Verify package imports and domain reload against an isolated real Editor."""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import threading
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("matrix", ROOT / "scripts/e2e-matrix.py")
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)


def prepare(editor, destination):
    version, data = matrix.editor_layout(editor)
    resources = data / "Resources/PackageManager"
    catalog = json.loads((resources / "Editor/manifest.json").read_text(encoding="utf-8"))["packages"]
    modules = [p.name for p in (resources / "BuiltInPackages").iterdir() if p.is_dir()]
    manifest = matrix.package_manifest(version, catalog, modules)
    # Package import needs neither sample scenes nor graphics/recording packages.
    for package in ("com.unity.visualeffectgraph", "com.unity.render-pipelines.universal",
                    "com.unity.recorder", "com.unity.timeline", "com.unity.ai.navigation"):
        manifest["dependencies"].pop(package, None)
    project = destination / "project"
    (project / "Assets/Editor").mkdir(parents=True)
    shutil.copy2(ROOT / "UnityCliBridge/Assets/Editor/UnityCliInputBatchHost.cs", project / "Assets/Editor")
    (project / "ProjectSettings").mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + version + "\n", encoding="utf-8")
    # Headed Editors otherwise show an Input System restart confirmation that
    # suspends Editor.update (and therefore the Bridge command queue).
    (project / "ProjectSettings/ProjectSettings.asset").write_text(
        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  activeInputHandler: 1\n",
        encoding="utf-8")
    (project / "Packages").mkdir()
    shutil.copytree(ROOT / "UnityCliBridge/Packages/unity-cli-bridge", project / "Packages/unity-cli-bridge")
    (project / "Packages/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return version, project


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor", type=Path, required=True)
    parser.add_argument("--cli", type=Path, default=ROOT / "target/debug/unity-cli")
    parser.add_argument("--output", type=Path, default=ROOT / ".unity/package-import")
    parser.add_argument("--port", type=int, default=6591)
    parser.add_argument("--asset-store", action="store_true",
                        help="Use a headed Editor and test manual-download orchestration with a synthetic cache arrival (not AC-6)")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    out = Path(tempfile.mkdtemp(prefix="run-", dir=args.output.resolve()))
    # Windows holds running executables open. Keep Cargo's output replaceable.
    isolated_cli = out / args.cli.name
    shutil.copy2(args.cli.resolve(), isolated_cli)
    for library in args.cli.resolve().parent.glob("*.dll"):
        shutil.copy2(library, out / library.name)
    version, project = prepare(args.editor.resolve(), out)
    env = dict(matrix.runtime_environment(out, isolated_cli), UNITY_CLI_NO_AUTO_UPDATE="1",
               UNITY_PROJECT_ROOT=str(project), UNITY_CLI_PORT=str(args.port),
               UNITY_CLI_HOST="127.0.0.1", UNITY_CLI_PORT_OVERRIDE=str(args.port),
               UNITY_CLI_EDITORS_DIR=str(out / "editors"),
               UNITY_CLI_TOOLS_ROOT=str(out / "tools"),
               UNITY_CLI_REGISTRY_PATH=str(out / "instances.json"),
               UNITY_CLI_CACHE_ROOT=str(out / "cache"),
               UNITY_CLI_ALLOW_BATCH_HOST="1",
               UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(out / "stop"))
    for key in ("UNITY_CLI_ALLOW_UNAUTHENTICATED", "UNITY_CLI_AUTH_TOKEN_FILE",
                "UNITY_CLI_ASSET_STORE_CACHE_PATH", "ASSETSTORE_CACHE_PATH"):
        env.pop(key, None)
    checks = []
    summary = {"started_at": datetime.now(timezone.utc).isoformat(), "platform": os.name,
               "unity": version, "artifact_directory": str(out), "checks": checks,
               "User Verification Result": "n/a (autonomous)",
               "Agent Visual Check": "n/a (no UI surface)", "status": "FAIL"}
    host = None

    def save():
        (out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

    def check(name, condition):
        checks.append({"name": name, "passed": bool(condition)})
        save()
        print(name, "PASS" if condition else "FAIL", flush=True)
        if not condition:
            raise AssertionError(name)

    cli = [str(isolated_cli), "--project-path", str(project),
           "--host", "127.0.0.1", "--port", str(args.port), "--output", "json"]

    def call(name, words, error=None, timeout_ms=180000, extra_env=None):
        result = subprocess.run(cli + ["--timeout-ms", str(timeout_ms)] + words,
                                env=dict(env, **(extra_env or {})), text=True, encoding="utf-8", errors="replace",
                                capture_output=True, timeout=timeout_ms / 1000 + 20)
        (out / f"{name}.stdout.json").write_text(result.stdout, encoding="utf-8")
        (out / f"{name}.stderr.log").write_text(result.stderr, encoding="utf-8")
        value = json.loads(result.stdout)
        if error:
            check(name + ": error code " + error,
                  result.returncode != 0 and value["errors"][0]["code"] == error)
        elif result.returncode:
            raise RuntimeError(name + ": " + result.stdout + result.stderr)
        return value["data"]

    def evaluate(name, code):
        return call(name, ["editor", "eval", "--mode", "statements", code])

    try:
        # Same source snapshot as the live imports below, with structured NUnit evidence.
        command = [str(args.editor.resolve()), "-batchmode", "-nographics", "-projectPath", str(project),
                   "-runTests", "-testPlatform", "EditMode", "-testFilter", "UnityPackageHandlerTests",
                   "-testResults", str(out / "editmode.xml"), "-logFile", str(out / "editmode.log")]
        tests = subprocess.run(command, env=env, timeout=900)
        check("EditMode process", tests.returncode == 0)
        report = ET.parse(out / "editmode.xml").getroot()
        summary["editmode_tests"] = [case.attrib for case in report.findall(".//test-case")]
        check("EditMode package tests", int(report.get("failed", "1")) == 0 and int(report.get("passed", "0")) >= 7)
        log = out / "editor.log"
        host = subprocess.Popen([str(args.editor.resolve())] + ([] if args.asset_store else ["-batchmode", "-nographics"]) + ["-projectPath", str(project),
                                 "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
                                 "-logFile", str(log)], env=env, stdout=subprocess.DEVNULL,
                                stderr=subprocess.STDOUT)
        env["UNITY_CLI_AUTH_TOKEN_FILE"] = str(out / "editors" / f"{host.pid}.json")
        deadline = time.monotonic() + 600
        while time.monotonic() < deadline:
            if host.poll() is not None:
                raise RuntimeError("Editor exited; inspect " + str(log))
            try:
                state = call("startup", ["raw", "get_compilation_state", "--json", "{}"], timeout_ms=2000)
                if not state["isCompiling"] and not state["isUpdating"]:
                    break
            except (RuntimeError, ValueError, subprocess.TimeoutExpired):
                pass
            time.sleep(2)
        else:
            raise TimeoutError("Editor startup")
        cache = out / "asset-store" / "Publisher" / "Tools"
        cache.mkdir(parents=True)
        package = cache / "Small.v1.unitypackage"
        asset = "Assets/Scenes/Generated/E2E/PackageE2E/actual.txt"
        code = (f'System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName({json.dumps(asset)})); '
                f'System.IO.File.WriteAllText({json.dumps(asset)}, "real Unity export"); '
                f'UnityEditor.AssetDatabase.ImportAsset({json.dumps(asset)}); '
                f'UnityEditor.AssetDatabase.ExportPackage({json.dumps(asset)}, {json.dumps(str(package).replace(chr(92), "/"))}); '
                f'UnityEditor.AssetDatabase.DeleteAsset({json.dumps(asset)});')
        evaluate("export-real-package", code)
        check("real Unity-exported unitypackage", package.exists() and package.stat().st_size > 0)
        cache_root = str(cache.parents[1])
        listing = call("cache-list", ["package", "cache-list", "--cache-path", cache_root])
        item = listing["packages"][0]
        check("cache metadata", item["publisher"] == "Publisher" and item["name"] == "Small.v1" and item["size"] == package.stat().st_size)
        override = call("cache-environment", ["package", "cache-list"],
                        extra_env={"UNITY_CLI_ASSET_STORE_CACHE_PATH": cache_root})
        check("cache environment override", override["packages"] == listing["packages"])
        if args.asset_store:
            summary["asset_store_download_source"] = "synthetic cache arrival; no Asset Store download or human interaction"
            opened = call("open-my-assets", ["package", "asset-store-open"])
            check("My Assets opened with unresolved selection", opened["myAssetsOpened"]
                  and opened["selection"] == "unresolved" and not opened["selectionConfirmed"]
                  and opened["manualDownloadRequired"])
            waiting = call("wait-timeout", ["package", "asset-store-wait", "Missing", "--cache-path", cache_root,
                           "--download-timeout-ms", "2000"], error="ASSET_STORE_DOWNLOAD_TIMEOUT")
            check("download timeout state", waiting["status"] == "waiting_download")
            complete = package.read_bytes()
            package.write_bytes(complete[:20])
            waiting = call("partial-timeout", ["package", "asset-store-wait", item["name"], "--cache-path", cache_root,
                           "--download-timeout-ms", "2000"], error="ASSET_STORE_DOWNLOAD_TIMEOUT")
            check("partial archive was not imported", waiting["reason"] == "incomplete_archive" and not (project / asset).exists())

            def finish_download():
                time.sleep(2)
                partial = package.with_suffix(".tmp")
                partial.write_bytes(complete)
                partial.replace(package)

            arrival = threading.Thread(target=finish_download)
            arrival.start()
            try:
                result = call("asset-store-import", ["package", "asset-store-import", item["name"],
                              "--cache-path", cache_root, "--download-timeout-ms", "30000"])
            finally:
                arrival.join()
            check("open wait import flow returns imported paths", result["status"] == "completed"
                  and asset in result["importedAssets"] and result["assetStore"]["manualDownloadRequired"])
            check("flow imported content", (project / asset).read_text() == "real Unity export")
            evaluate("flow-delete", f'UnityEditor.AssetDatabase.DeleteAsset({json.dumps(asset)});')
            ready = call("wait-existing", ["package", "asset-store-wait", item["name"], "--cache-path", cache_root])
            check("standalone wait has no import side effect", ready["status"] == "ready" and not (project / asset).exists())
        call("missing", ["package", "import", str(out / "missing.unitypackage")], error="PACKAGE_NOT_FOUND")
        broken = out / "broken.unitypackage"
        broken.write_text("broken archive")
        call("corrupt", ["package", "import", str(broken)], error="INVALID_UNITYPACKAGE")
        for name, words in [
            ("local", ["package", "import", str(package)]),
            ("cached-name", ["package", "cache-import", item["name"], "--cache-path", cache_root]),
            ("cached-id", ["package", "cache-import", item["id"], "--cache-path", cache_root]),
        ]:
            result = call(name, words)
            check(name + ": imported paths and settled compilation", result["status"] == "completed" and asset in result["importedAssets"]
                  and not result["compileState"]["isCompiling"] and not result["compileState"]["isUpdating"])
            check(name + ": imported content", (project / asset).read_text() == "real Unity export")
            evaluate(name + "-delete", f'UnityEditor.AssetDatabase.DeleteAsset({json.dumps(asset)});')
        # Export the script through Unity as well. Keep assemblies locked only
        # while preparing/removing the seed, so reload cannot interrupt export.
        reload_package = out / "reload.unitypackage"
        script = "Assets/Scenes/Generated/E2E/PackageE2E/PackageImportReloadProbe.cs"
        source = 'using UnityEditor; [InitializeOnLoad] public static class PackageImportReloadProbe { static PackageImportReloadProbe() { SessionState.SetString("Issue559ReloadProbe", "loaded"); } }'
        evaluate("export-script-package", 'UnityEditor.EditorApplication.LockReloadAssemblies(); try { '
                 + f'System.IO.File.WriteAllText({json.dumps(script)}, {json.dumps(source)}); '
                 + f'System.IO.File.WriteAllText({json.dumps(asset)}, "real Unity export"); '
                 + f'UnityEditor.AssetDatabase.ImportAsset({json.dumps(script)}, UnityEditor.ImportAssetOptions.ForceSynchronousImport); '
                 + f'UnityEditor.AssetDatabase.ImportAsset({json.dumps(asset)}, UnityEditor.ImportAssetOptions.ForceSynchronousImport); '
                 + f'UnityEditor.AssetDatabase.ExportPackage(new[] {{ {json.dumps(script)}, {json.dumps(asset)} }}, {json.dumps(reload_package.as_posix())}); '
                 + f'UnityEditor.AssetDatabase.DeleteAsset({json.dumps(script)}); UnityEditor.AssetDatabase.DeleteAsset({json.dumps(asset)}); '
                 + '} finally { UnityEditor.EditorApplication.UnlockReloadAssemblies(); }')
        check("real Unity-exported script package", reload_package.exists() and reload_package.stat().st_size > 0)
        idle = 0
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline and idle < 2:
            time.sleep(.5)
            try:
                state = call("export-idle", ["raw", "get_compilation_state", "--json", "{}"], timeout_ms=2000)
                idle = idle + 1 if not state["isCompiling"] and not state["isUpdating"] else 0
            except RuntimeError:
                idle = 0
        check("export preparation settled", idle >= 2)
        result = call("script-reload", ["package", "import", str(reload_package)])
        check("domain reload survived", result["status"] == "completed" and result["domainReloads"] >= 1 and script in result["importedAssets"])
        probe = call("reload-probe", ["editor", "eval", 'UnityEditor.SessionState.GetString("Issue559ReloadProbe", "missing")'])
        check("imported script executed", probe["value"] == "loaded")
        # A deliberately short wait must expose the saved request for recovery.
        result = call("timeout", ["package", "import", str(package)], error="PACKAGE_IMPORT_TIMEOUT", timeout_ms=500)
        check("timeout snapshot", bool(result["requestId"]) and result["status"] != "completed" and "compileState" in result)
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            status = call("timeout-status", ["package", "import-status", result["requestId"]])
            if status["status"] == "completed":
                break
            time.sleep(.25)
        check("timed out import completed without resubmission", status["status"] == "completed")
        summary["status"] = "PASS"
    finally:
        subprocess.run(cli + ["unityd", "stop"], env=env, stdout=subprocess.DEVNULL,
                       stderr=subprocess.STDOUT, timeout=15)
        if host is not None:
            (out / "stop").touch()
            try:
                host.wait(timeout=20)
            except subprocess.TimeoutExpired:
                matrix.stop_owned(host)
        save()
        print("Evidence:", out, flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
