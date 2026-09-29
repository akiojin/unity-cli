#!/usr/bin/env python3
"""Exercise Input Actions source persistence, reimport, and Editor restart locally."""

import argparse
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import uuid


ROOT = Path(__file__).resolve().parent.parent


class Runner:
    def __init__(self, args):
        self.args = args
        self.project = args.project_path.resolve()
        self.cli = str(args.unity_cli.resolve())
        version_file = self.project / "ProjectSettings/ProjectVersion.txt"
        self.version = next(line.split(": ", 1)[1] for line in
                            version_file.read_text().splitlines()
                            if line.startswith("m_EditorVersion: "))
        self.unity = args.unity_path or Path(
            f"/Applications/Unity/Hub/Editor/{self.version}/Unity.app/Contents/MacOS/Unity")
        self.artifacts = Path(tempfile.mkdtemp(prefix="input-actions-persistence-"))
        self.asset_path = ("Assets/Scenes/Generated/E2E/"
                           f"InputActionsPersistence-{uuid.uuid4().hex}/Controls.inputactions")
        self.asset = self.project / self.asset_path
        self.stop_file = self.artifacts / "stop"
        self.process = None
        self.passed = 0
        self.generation = 0

    def call(self, tool, params=None, timeout=120):
        env = dict(os.environ, UNITY_PROJECT_ROOT=str(self.project))
        result = subprocess.run(
            [self.cli, "tool", "call", tool, "--json", json.dumps(params or {}),
             "--host", "127.0.0.1", "--port", str(self.args.port),
             "--timeout-ms", str(timeout * 1000), "--output", "json"],
            cwd=self.project, env=env, capture_output=True, text=True, timeout=timeout + 5)
        with (self.artifacts / "commands.log").open("a") as log:
            log.write(f"{tool} {json.dumps(params or {})}\n{result.stdout}\n{result.stderr}\n")
        if result.returncode:
            raise RuntimeError(f"{tool}: {result.stderr or result.stdout}")
        response = json.loads(result.stdout)
        if response.get("error") or response.get("success") is False:
            raise RuntimeError(f"{tool}: {response}")
        return response

    def start(self):
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", self.args.port))
        self.stop_file.unlink(missing_ok=True)
        self.generation += 1
        log = self.artifacts / f"unity-{self.generation}.log"
        env = dict(os.environ, UNITY_CLI_ALLOW_BATCH_HOST="1",
                   UNITY_CLI_PORT_OVERRIDE=str(self.args.port),
                   UNITY_CLI_PORT=str(self.args.port),
                   UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(self.stop_file))
        self.process = subprocess.Popen(
            [str(self.unity), "-batchmode", "-nographics", "-projectPath", str(self.project),
             "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
             "-logFile", str(log)], cwd=self.project, env=env,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        deadline = time.monotonic() + 360
        last_error = "listener not available"
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError(f"Unity exited ({self.process.returncode}); see {log}")
            try:
                with socket.create_connection(("127.0.0.1", self.args.port), timeout=1):
                    pass
                self.call("get_editor_state", timeout=5)
                print(f"Unity host {self.generation} ready: {log}", flush=True)
                return
            except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
                last_error = str(error)
                time.sleep(2)
        raise RuntimeError(f"Unity readiness timeout: {last_error}; see {log}")

    def stop(self):
        if self.process is None:
            return
        if self.process.poll() is None:
            self.stop_file.touch()
            try:
                self.process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                self.process.terminate()
                try:
                    self.process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait(timeout=10)
        self.process = None
        self.stop_file.unlink(missing_ok=True)

    def check(self, condition, label):
        if not condition:
            raise AssertionError(label)
        self.passed += 1
        print(f"PASS {label}", flush=True)

    def snapshot(self):
        params = {"assetPath": self.asset_path}
        analysis = self.call("analyze_input_actions_asset", params)
        state = self.call("get_input_actions_state", params)
        # Input System serializes absent optional binding strings as empty strings.
        for action_map in analysis["actionMaps"]:
            for action in action_map["actions"]:
                for binding in action["bindings"]:
                    for key in ("groups", "interactions", "processors"):
                        binding[key] = binding.get(key) or ""
        return {"actionMaps": analysis["actionMaps"], "controlSchemes": state["controlSchemes"]}

    @staticmethod
    def source_projection(source):
        maps = []
        for action_map in source["maps"]:
            actions = []
            for action in action_map["actions"]:
                bindings = [b for b in action_map["bindings"] if b["action"] == action["name"]]
                actions.append({"name": action["name"], "id": action["id"],
                                "bindings": [{"id": b["id"], "path": b["path"]} for b in bindings]})
            maps.append({"name": action_map["name"], "id": action_map["id"], "actions": actions})
        return maps

    @staticmethod
    def memory_projection(snapshot):
        return [{"name": m["name"], "id": m["id"], "actions": [
            {"name": a["name"], "id": a["id"], "bindings": [
                {"id": b["id"], "path": b["path"]} for b in a["bindings"]]}
            for a in m["actions"]]} for m in snapshot["actionMaps"]]

    def mutation(self, tool, **params):
        response = self.call(tool, {"assetPath": self.asset_path, **params})
        if response.get("success") is not True:
            raise AssertionError(f"{tool} did not report success: {response}")
        source = json.loads(self.asset.read_text())
        memory = self.snapshot()
        self.check(self.source_projection(source) == self.memory_projection(memory),
                   f"{tool}: source map/action/binding contents and IDs match memory")
        source_schemes = [{"name": s["name"], "bindingGroup": s.get("bindingGroup") or "",
                           "devices": [{"controlPath": d["devicePath"],
                                        "isOptional": d["isOptional"]} for d in s["devices"]]}
                          for s in source["controlSchemes"]]
        schemes = [{**s, "bindingGroup": s.get("bindingGroup") or ""}
                   for s in memory["controlSchemes"]]
        self.check(source_schemes == schemes, f"{tool}: source control schemes match memory")
        with self.asset.open("a") as stream:
            stream.write("\n")
        self.call("refresh_assets")
        self.check(self.snapshot() == memory, f"{tool}: reimport preserves structure and IDs")

    def run(self):
        print(f"Unity {self.version}; port {self.args.port}; artifacts {self.artifacts}", flush=True)
        self.asset.parent.mkdir(parents=True, exist_ok=False)
        self.asset.write_text(json.dumps({"name": "Controls", "maps": [], "controlSchemes": []}))
        self.start()
        self.call("refresh_assets")
        # This first operation must fail against the original SetDirty/SaveAssets implementation.
        self.mutation("create_action_map", mapName="Audit", actions=[{"name": "Jump", "type": "Button"}])
        common = {"mapName": "Audit", "actionName": "Move"}
        self.mutation("add_input_action", **common, actionType="Value")
        self.mutation("add_input_binding", **common, path="<Keyboard>/space")
        self.mutation("remove_input_binding", **common, bindingIndex=0)
        self.mutation("create_composite_binding", **common, compositeType="2DVector",
                      bindings={"up": "<Keyboard>/w", "down": "<Keyboard>/s",
                                "left": "<Keyboard>/a", "right": "<Keyboard>/d"})
        self.mutation("remove_all_bindings", **common)
        self.mutation("add_input_binding", **common, path="<Gamepad>/leftStick")
        # Only persistence is asserted: action-removal semantics belong to a separate issue.
        self.mutation("remove_input_action", **common)
        self.mutation("create_action_map", mapName="Temporary")
        self.mutation("remove_action_map", mapName="Temporary")
        self.mutation("manage_control_schemes", operation="add", schemeName="Keyboard", devices=["Keyboard"])
        self.mutation("manage_control_schemes", operation="remove", schemeName="Keyboard")
        self.mutation("manage_control_schemes", operation="add", schemeName="Gamepad", devices=["Gamepad"])
        self.mutation("add_input_binding", mapName="Audit", actionName="Jump", path="<Keyboard>/space")
        before_restart = self.snapshot()
        source_before_restart = json.loads(self.asset.read_text())
        (self.artifacts / "before-restart.json").write_text(json.dumps(before_restart, indent=2))
        self.stop()
        self.start()
        after_restart = self.snapshot()
        (self.artifacts / "after-restart.json").write_text(json.dumps(after_restart, indent=2))
        self.check(after_restart == before_restart, "Editor restart preserves maps/actions/bindings/schemes and IDs")
        self.check(json.loads(self.asset.read_text()) == source_before_restart,
                   "Editor restart preserves source JSON")
        self.mutation("remove_action_map", mapName="Audit")
        self.mutation("manage_control_schemes", operation="remove", schemeName="Gamepad")

    def cleanup(self):
        self.stop()
        if self.asset.parent.exists():
            shutil.rmtree(self.asset.parent)
        self.asset.parent.with_suffix(".meta").unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-cli", type=Path, default=ROOT / "target/debug/unity-cli")
    parser.add_argument("--unity-path", type=Path)
    parser.add_argument("--port", type=int, default=6428)
    parser.add_argument("--project-path", type=Path, default=ROOT / "UnityCliBridge")
    args = parser.parse_args()
    runner = Runner(args)
    failed = 0
    try:
        runner.run()
    except (Exception, KeyboardInterrupt) as error:
        failed = 1
        print(f"FAIL {error}", file=sys.stderr, flush=True)
    finally:
        try:
            runner.cleanup()
        except Exception as error:
            failed = 1
            print(f"FAIL cleanup: {error}", file=sys.stderr, flush=True)
        summary = {"unity_version": runner.version, "passed": runner.passed, "failed": failed,
                   "editor_starts": runner.generation, "artifacts": str(runner.artifacts)}
        (runner.artifacts / "summary.json").write_text(json.dumps(summary, indent=2))
        print(json.dumps(summary), flush=True)
    return failed


if __name__ == "__main__":
    sys.exit(main())
