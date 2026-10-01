"""Prefab Variant/override/unpack acceptance through the real CLI and Editor.

--launch creates disposable projects for both supported acceptance versions.
Fixtures and complete command/results evidence are retained under --output.
"""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]


class Suite:
    def __init__(self, args, project, output, env):
        self.args, self.project, self.output, self.env = args, project, output, env
        self.passed = 0
        self.folder = "Assets/Scenes/Generated/E2E/Prefab-" + uuid.uuid4().hex
        self.base, self.variant = self.folder + "/Base.prefab", self.folder + "/Variant.prefab"
        self.prefix = [str(args.unity_cli), "--host", "127.0.0.1", "--port", str(args.port),
                       "--timeout-ms", "120000", "--output", "json"]

    def raw(self, tool, params, error=None):
        command = self.prefix + ["raw", tool, "--json", json.dumps(params)]
        deadline = time.monotonic() + 60
        while True:
            result = subprocess.run(command, env=self.env, capture_output=True, text=True, timeout=150)
            try:
                data = json.loads(result.stdout)
            except ValueError:
                data = {"unparsed": result.stdout, "stderr": result.stderr}
            with (self.output / "calls.jsonl").open("a") as stream:
                stream.write(json.dumps({"command": command, "exit": result.returncode, "result": data, "stderr": result.stderr}) + "\n")
            if (data.get("errors") or [{}])[0].get("code") == "EDITOR_BUSY" and time.monotonic() < deadline:
                time.sleep(0.5)
                continue
            break
        if error:
            self.check(result.returncode != 0 and error in json.dumps(data), tool + " rejects " + error)
        else:
            assert result.returncode == 0 and not data.get("error") and data.get("success") is not False, (tool, data)
        return data["data"]

    def evaluate(self, code, statements=False):
        data = self.raw("eval_csharp", {"code": code, "mode": "statements" if statements else "expression"})
        assert data.get("state") == "completed", data
        return data.get("value")

    def check(self, condition, name):
        assert condition, name
        self.passed += 1
        print("PASS", name, flush=True)
        with (self.output / "assertions.log").open("a") as stream:
            stream.write("PASS " + name + "\n")

    def value(self, code, expected, name):
        actual = self.evaluate(code)
        self.check(actual == expected, f"{name}: expected {expected!r}, got {actual!r}")

    def edit(self, component, statement, name="PrefabProbe"):
        self.evaluate(f'var c = GameObject.Find("{name}").GetComponent<{component}>(); {statement}; '
                      'PrefabUtility.RecordPrefabInstancePropertyModifications(c); return true;', True)

    def component_id(self, component, name="PrefabProbe"):
        return self.evaluate(f'GameObject.Find("{name}").GetComponent<{component}>().GetInstanceID()')

    def manage(self, action, scope, name="PrefabProbe", **params):
        return self.raw("manage_prefab_overrides", dict(gameObjectPath="/" + name, action=action, scope=scope, **params))

    def asset(self, path, expression):
        return f'AssetDatabase.LoadAssetAtPath<GameObject>("{path}").{expression}'

    def create_variant(self, path):
        name = "VariantSource" + uuid.uuid4().hex
        self.raw("instantiate_prefab", {"prefabPath": self.base, "name": name})
        instance_id = self.evaluate(f'GameObject.Find("{name}").GetInstanceID()')
        self.raw("create_prefab", {"gameObjectPath": "/" + name, "prefabPath": path})
        self.value(f'PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>("{path}")).ToString()',
                   "Variant", "existing create_prefab creates inherited Variant")
        self.evaluate(f'UnityEngine.Object.DestroyImmediate(EditorUtility.InstanceIDToObject({instance_id})); return true;', True)

    def run(self):
        info = self.raw("get_editor_info", {})
        print("Unity", info["unity"]["unityVersion"], flush=True)
        self.evaluate('UnityEditor.SceneManagement.EditorSceneManager.NewScene('
                      'UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);'
                      f'System.IO.Directory.CreateDirectory("{self.folder}"); AssetDatabase.Refresh();'
                      'var root = new GameObject("PrefabSource"); root.AddComponent<BoxCollider>();'
                      'root.AddComponent<SphereCollider>(); return true;', True)
        self.raw("create_prefab", {"gameObjectPath": "/PrefabSource", "prefabPath": self.base})
        self.create_variant(self.variant)
        self.raw("instantiate_prefab", {"prefabPath": self.variant, "name": "PrefabProbe"})
        self.edit("BoxCollider", "c.size = new Vector3(3, 1, 1)")
        listing = self.raw("get_prefab_overrides", {"gameObjectPath": "/PrefabProbe"})
        self.check(listing["applyTargets"] == [self.variant, self.base], "Variant and base are explicit apply targets")
        box_id = next(p["instanceId"] for p in listing["properties"] if p["propertyPath"] == "m_Size.x")
        self.manage("apply", "property", instanceId=box_id, propertyPath="m_Size.x", assetPath=self.variant)
        self.value(self.asset(self.base, "GetComponent<BoxCollider>().size.x"), 1, "Variant apply leaves base unchanged")
        self.evaluate(f'var root = PrefabUtility.LoadPrefabContents("{self.base}");'
                      'root.GetComponent<BoxCollider>().size = new Vector3(9, 7, 1);'
                      f'PrefabUtility.SaveAsPrefabAsset(root, "{self.base}"); PrefabUtility.UnloadPrefabContents(root); return true;', True)
        self.value(self.asset(self.variant, "GetComponent<BoxCollider>().size.y"), 7, "base edit propagates to Variant")
        self.value(self.asset(self.variant, "GetComponent<BoxCollider>().size.x"), 3, "Variant override survives base edit")

        self.edit("SphereCollider", "c.radius = 2.5f")
        self.manage("apply", "property", instanceId=self.component_id("SphereCollider"), propertyPath="m_Radius", assetPath=self.base)
        self.value(self.asset(self.base, "GetComponent<SphereCollider>().radius"), 2.5, "individual property applies to base through Variant")
        self.edit("BoxCollider", "c.size = new Vector3(3, 8, 1)")
        self.edit("SphereCollider", "c.radius = 4")
        self.manage("revert", "property", instanceId=self.component_id("BoxCollider"), propertyPath="m_Size.y")
        self.value('GameObject.Find("PrefabProbe").GetComponent<BoxCollider>().size.y', 7, "individual property reverts")
        self.value('GameObject.Find("PrefabProbe").GetComponent<SphereCollider>().radius', 4, "revert preserves unrelated override")
        self.manage("revert", "object", instanceId=self.component_id("SphereCollider"))
        self.value('GameObject.Find("PrefabProbe").GetComponent<SphereCollider>().radius', 2.5, "object override reverts")
        self.edit("SphereCollider", "c.radius = 5")
        self.manage("apply", "object", instanceId=self.component_id("SphereCollider"), assetPath=self.variant)
        self.value(self.asset(self.variant, "GetComponent<SphereCollider>().radius"), 5, "object override applies to Variant")

        for action in ("revert", "apply"):
            self.evaluate('var go = GameObject.Find("PrefabProbe"); go.AddComponent<CapsuleCollider>();'
                          'UnityEngine.Object.DestroyImmediate(go.GetComponent<SphereCollider>()); return true;', True)
            self.edit("BoxCollider", "c.isTrigger = true")
            listing = self.raw("get_prefab_overrides", {"gameObjectPath": "/PrefabProbe"})
            self.check(any(p["propertyPath"] == "m_IsTrigger" for p in listing["properties"]), "structured property override")
            added = next(p for p in listing["addedComponents"] if p["type"] == "UnityEngine.CapsuleCollider")
            removed = next(p for p in listing["removedComponents"] if p["type"] == "UnityEngine.SphereCollider")
            self.check(added["instanceId"] != 0 and removed["assetComponentId"] != 0, "structured added and removed components")
            destination = {"assetPath": self.variant} if action == "apply" else {}
            self.manage(action, "added_component", instanceId=added["instanceId"], **destination)
            self.manage(action, "removed_component", instanceId=removed["instanceId"], assetComponentId=removed["assetComponentId"], **destination)
            self.value('GameObject.Find("PrefabProbe").GetComponent<CapsuleCollider>() != null', action == "apply", action + " added component")
            self.value('GameObject.Find("PrefabProbe").GetComponent<SphereCollider>() != null', action == "revert", action + " removed component")
        self.value(self.asset(self.base, "GetComponent<SphereCollider>() != null"), True, "component apply to Variant leaves base intact")
        self.edit("CapsuleCollider", "c.radius = 3")
        self.raw("manage_prefab_overrides", {"gameObjectPath": "/PrefabProbe", "action": "apply", "scope": "all", "assetPath": self.base}, "INVALID_APPLY_TARGET")
        self.value(self.asset(self.base, "GetComponent<BoxCollider>().isTrigger"), False, "ineligible base apply fails before other overrides change")
        self.manage("revert", "all")

        # Both target choices for whole-instance apply; refresh IDs after every mutation.
        for target_name in ("Variant", "Base"):
            path = self.folder + "/All" + target_name + ".prefab"
            name = "All" + target_name
            self.create_variant(path)
            self.raw("instantiate_prefab", {"prefabPath": path, "name": name})
            self.edit("BoxCollider", "c.size = new Vector3(11, 7, 1)", name)
            self.evaluate(f'var go = GameObject.Find("{name}"); go.AddComponent<CapsuleCollider>();'
                          'UnityEngine.Object.DestroyImmediate(go.GetComponent<SphereCollider>()); return true;', True)
            destination = path if target_name == "Variant" else self.base
            self.manage("apply", "all", name=name, assetPath=destination)
            self.value(self.asset(destination, "GetComponent<BoxCollider>().size.x"), 11, "all properties applied to " + target_name)
            self.value(self.asset(destination, "GetComponent<CapsuleCollider>() != null"), True, "all addition applied to " + target_name)
            self.value(self.asset(destination, "GetComponent<SphereCollider>() == null"), True, "all removal applied to " + target_name)
            self.edit("BoxCollider", "c.size = new Vector3(22, 7, 1)", name)
            self.evaluate(f'var go = GameObject.Find("{name}"); go.AddComponent<Light>();'
                          'UnityEngine.Object.DestroyImmediate(go.GetComponent<CapsuleCollider>()); return true;', True)
            self.manage("revert", "all", name=name)
            self.value(f'GameObject.Find("{name}").GetComponent<BoxCollider>().size.x', 11, "all property overrides reverted")
            self.value(f'GameObject.Find("{name}").GetComponent<CapsuleCollider>() != null', True, "all removed components restored")
            self.value(f'GameObject.Find("{name}").GetComponent<Light>() == null', True, "all added components reverted")

        # Persist/reload assets and scene, so no result relies only on in-memory state.
        scene = self.folder + "/Acceptance.unity"
        self.evaluate(f'AssetDatabase.SaveAssets(); UnityEditor.SceneManagement.EditorSceneManager.SaveScene('
                      f'UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "{scene}");'
                      f'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("{scene}"); return true;', True)
        self.value(self.asset(self.variant, "GetComponent<BoxCollider>().size.x"), 3, "Variant override persists after reload")
        self.value(self.asset(self.base, "GetComponent<BoxCollider>().size.x"), 11, "base apply persists after reload")

        nested = self.folder + "/Nested.prefab"
        self.evaluate('var root = new GameObject("NestedSource");'
                      f'PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("{self.base}"), root.transform);'
                      f'PrefabUtility.SaveAsPrefabAsset(root, "{nested}"); UnityEngine.Object.DestroyImmediate(root); return true;', True)
        for mode in ("Outermost", "Completely"):
            name = "Unpack" + mode
            self.raw("instantiate_prefab", {"prefabPath": nested, "name": name})
            result = self.raw("unpack_prefab", {"gameObjectPath": "/" + name, "mode": mode})
            self.check(result["isPartOfPrefabInstance"] is False, mode + " disconnects outer root")
            self.check(result["connectedObjectCount"] == (1 if mode == "Outermost" else 0), mode + " reports nested connections accurately")
            self.value(f'PrefabUtility.IsPartOfPrefabInstance(GameObject.Find("{name}").transform.GetChild(0).gameObject)', mode == "Outermost", mode + " nested connection verified independently")
        self.raw("unpack_prefab", {"gameObjectPath": "/UnpackCompletely", "mode": "Completely"}, "NOT_PREFAB_INSTANCE_ROOT")
        self.evaluate('GameObject.Find("PrefabProbe").SetActive(false); return true;', True)
        self.check(self.raw("get_prefab_overrides", {"gameObjectPath": "/PrefabProbe"})["success"], "inactive Prefab root can be inspected")
        self.evaluate('UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());'
                      f'UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "{scene}"); return true;', True)
        return info["unity"]["unityVersion"]

    def unit_tests(self):
        started = self.raw("run_tests", {"testMode": "EditMode", "filter": "UnityCliBridge.Tests.PrefabWorkflowHandlerTests", "includeDetails": True})
        assert started.get("status") == "running" and started.get("runId"), started
        deadline = time.monotonic() + 300
        while time.monotonic() < deadline:
            result = self.raw("get_test_status", {"includeTestResults": True})
            if result.get("status") == "completed":
                (self.output / "editmode.json").write_text(json.dumps(result, indent=2) + "\n")
                assert (result.get("runId") == started["runId"] and result.get("failedTests") == 0
                        and result.get("passedTests", 0) >= 22 and result.get("passedTests") == result.get("totalTests")), result
                print(f'PASS focused EditMode: {result["passedTests"]} passed, 0 failed', flush=True)
                return {"passed": result["passedTests"], "failed": result["failedTests"], "runId": result["runId"]}
            assert result.get("status") not in ("failed", "error"), result
            time.sleep(1)
        raise AssertionError("EditMode tests timed out")


def load_matrix():
    spec = importlib.util.spec_from_file_location("prefab_matrix", ROOT / "scripts/e2e-matrix.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--launch", action="store_true")
    parser.add_argument("--versions", default="6000.3.25f1,2022.3.62f3")
    parser.add_argument("--unity-cli", type=Path, default=ROOT / "target/debug/unity-cli")
    parser.add_argument("--project-root", type=Path, default=ROOT / "UnityCliBridge")
    parser.add_argument("--port", type=int, default=6495)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--editmode", action="store_true")
    args = parser.parse_args()
    args.unity_cli = args.unity_cli.resolve()
    output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="unity-cli-prefab-"))
    if args.output:
        output.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, UNITY_CLI_TOOLS_ROOT=str(output / "tools"), UNITY_CLI_NO_AUTO_UPDATE="1")
    report = {"started_at": datetime.now(timezone.utc).isoformat(), "editors": []}
    matrix = load_matrix()
    print("Evidence:", output, flush=True)
    try:
        for version in args.versions.split(",") if args.launch else ["existing"]:
            destination = output / version
            destination.mkdir(exist_ok=False)
            host = None
            suite = None
            row = {"version": version, "status": "FAIL", "passed": 0, "failed": 1, "artifacts": str(destination)}
            try:
                project = args.project_root.resolve()
                run_env = env.copy()
                if args.launch:
                    editor = Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents/MacOS/Unity"
                    assert editor.is_file(), str(editor)
                    _, project, _ = matrix.prepare(editor, destination)
                    run_env.update(UNITY_CLI_ALLOW_BATCH_HOST="1", UNITY_CLI_PORT_OVERRIDE=str(args.port),
                                   UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(destination / "stop"))
                    with socket.socket() as probe:
                        probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                        probe.bind(("127.0.0.1", args.port))
                    command = [str(editor), "-batchmode", "-nographics", "-projectPath", str(project),
                               "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run", "-logFile", str(destination / "editor.log")]
                    row["launch"] = command
                    host = subprocess.Popen(command, env=run_env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
                run_env["UNITY_PROJECT_ROOT"] = str(project)
                suite = Suite(args, project, destination, run_env)
                deadline = time.monotonic() + 600
                while time.monotonic() < deadline:
                    if host and host.poll() is not None:
                        raise AssertionError("Editor exited; inspect editor.log")
                    log = destination / "editor.log"
                    if log.exists() and re.search(r"\berror CS\d+:|Project has invalid dependencies:", log.read_text(errors="replace")):
                        raise AssertionError("Editor compilation/package error; inspect editor.log")
                    try:
                        if host:
                            owners = subprocess.run(["lsof", "-nP", f"-iTCP:{args.port}", "-sTCP:LISTEN", "-t"],
                                                    capture_output=True, text=True, timeout=5)
                            if set(owners.stdout.split()) != {str(host.pid)}:
                                time.sleep(2)
                                continue
                        if matrix.ready(suite.raw("get_compilation_state", {})):
                            break
                    except (AssertionError, subprocess.TimeoutExpired):
                        pass
                    time.sleep(2)
                else:
                    raise AssertionError("Editor startup timed out")
                actual = suite.run()
                assert not args.launch or actual == version, actual
                if args.editmode:
                    row["editmode"] = suite.unit_tests()
                row.update(version=actual, status="PASS", passed=suite.passed, failed=0)
            except Exception as error:
                row.update(error=str(error), passed=suite.passed if suite else 0)
                print("FAIL", version, str(error), flush=True)
            finally:
                matrix.stop_owned(host)
                report["editors"].append(row)
                (output / "results.json").write_text(json.dumps(report, indent=2) + "\n")
    finally:
        subprocess.run([str(args.unity_cli), "unityd", "stop"], env=env, capture_output=True, timeout=30)
    print(json.dumps(report, indent=2), flush=True)
    return 0 if report["editors"] and all(row["status"] == "PASS" for row in report["editors"]) else 1


if __name__ == "__main__":
    raise SystemExit(main())
