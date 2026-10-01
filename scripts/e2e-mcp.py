#!/usr/bin/env python3
"""MCP Inspector and late-connect acceptance against an isolated real Editor."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import queue
import shutil
import socket
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("matrix", ROOT / "scripts/e2e-matrix.py")
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)
INSPECTOR = "@modelcontextprotocol/inspector@2.9.0"


class StdioSession:
    def __init__(self, cli, env, log):
        self.log = log.open("w")
        self.process = subprocess.Popen([str(cli), "mcp"], env=env, text=True,
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=self.log)
        self.messages = queue.Queue()
        self.transcript = []

        def read():
            for line in self.process.stdout:
                try:
                    self.messages.put(json.loads(line))
                except ValueError:
                    self.messages.put({"invalid_stdout": line})
        self.thread = threading.Thread(target=read, daemon=True)
        self.thread.start()

    def send(self, value):
        self.transcript.append({"client": value})
        self.process.stdin.write(json.dumps(value) + "\n")
        self.process.stdin.flush()

    def receive(self, timeout=20):
        value = self.messages.get(timeout=timeout)
        self.transcript.append({"server": value})
        return value

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            matrix.stop_owned(self.process)
        self.log.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--cli", type=Path, default=ROOT / "target/debug/unity-cli")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--port", type=int, default=6550)
    parser.add_argument("--custom-tools", action="store_true",
                        help="Also verify project tools from #442 through MCP")
    args = parser.parse_args()
    started = datetime.now(timezone.utc).isoformat()
    out = args.output.resolve()
    out.mkdir(parents=True, mode=0o700, exist_ok=False)
    # Freeze the executable so concurrent development builds cannot mix two
    # versions within the same acceptance run.
    cli = out / "unity-cli-under-test"
    shutil.copy2(args.cli.resolve(), cli)
    editor = Path(f"/Applications/Unity/Hub/Editor/{args.version}/Unity.app/Contents/MacOS/Unity")
    _, project, _ = matrix.prepare(editor, out)
    if args.custom_tools:
        (project / "Assets/Editor/McpProbe444.cs").write_text('''
using UnityCliBridge.Tools;
public static class McpProbe444
{
    [UnityCliTool("mcp_probe_444")]
    public static string Probe() { return "mcp-dynamic-ok"; }
}
''')
    # Check ownership of the port before starting either peer.
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", args.port))
    env = dict(os.environ, UNITY_CLI_EDITORS_DIR=str(out / "editors"),
               UNITY_CLI_TOOLS_ROOT=str(out / "tools"), UNITY_CLI_CACHE_ROOT=str(out / "cache"),
               UNITY_CLI_REGISTRY_PATH=str(out / "instances.json"),
               UNITY_PROJECT_ROOT=str(project), UNITY_CLI_PORT_OVERRIDE=str(args.port),
               UNITY_CLI_HOST="127.0.0.1", UNITY_CLI_PORT=str(args.port),
               UNITY_CLI_ALLOW_BATCH_HOST="1", UNITY_CLI_NO_AUTO_UPDATE="1",
               UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(out / "stop"))
    for key in ("UNITY_CLI_AUTH_TOKEN_FILE", "UNITY_CLI_ALLOW_UNAUTHENTICATED"):
        env.pop(key, None)
    checks = []
    host = session = None
    completed = False

    def check(name, passed):
        checks.append({"name": name, "passed": bool(passed)})
        print(name, "PASS" if passed else "FAIL", flush=True)
        if not passed:
            raise AssertionError(name)

    def inspector(label, method, tool=None, params=None, extra_env=None):
        child_env = dict(env, **(extra_env or {}))
        command = ["npx", "--yes", INSPECTOR, "--cli", str(cli), "mcp"]
        # Inspector's stdio child environment is deliberately explicit. No token
        # value is ever placed in a command line or written to the evidence.
        for key in ("UNITY_CLI_EDITORS_DIR", "UNITY_CLI_TOOLS_ROOT", "UNITY_CLI_CACHE_ROOT",
                    "UNITY_CLI_REGISTRY_PATH", "UNITY_PROJECT_ROOT", "UNITY_CLI_HOST",
                    "UNITY_CLI_PORT", "UNITY_CLI_NO_AUTO_UPDATE", "UNITY_CLI_AUTH_TOKEN_FILE"):
            if key in child_env:
                command += ["-e", f"{key}={child_env[key]}"]
        command += ["--method", method]
        if tool:
            command += ["--tool-name", tool, "--tool-args-json", json.dumps(params or {})]
        result = subprocess.run(command, env=child_env, text=True, capture_output=True, timeout=180)
        (out / f"{label}.stdout.json").write_text(result.stdout)
        (out / f"{label}.stderr.txt").write_text(result.stderr)
        (out / f"{label}.command.json").write_text(json.dumps(command, indent=2) + "\n")
        if not result.stdout.strip():
            raise RuntimeError(f"Inspector {label} failed ({result.returncode}): {result.stderr}")
        value = json.loads(result.stdout)
        if tool:
            # isError is an MCP tool result, not a JSON-RPC transport failure.
            envelope = json.loads(value["content"][0]["text"])
            return value, envelope
        check(label + " inspector exit", result.returncode == 0)
        return value

    try:
        session = StdioSession(cli, env, out / "late-connect.stderr.txt")
        session.send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2025-11-25", "capabilities": {},
            "clientInfo": {"name": "unity-cli-e2e", "version": "1"}}})
        response = session.receive()
        check("starts with Editor absent", response.get("result", {}).get("capabilities", {}).get("tools", {}).get("listChanged"))
        session.send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        session.send({"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
        response = session.receive()
        check("catalog before connection", response.get("id") == 2 and len(response["result"]["tools"]) > 0)
        try:
            unexpected = session.receive(timeout=3)
        except queue.Empty:
            unexpected = None
        check("no false connection notification", unexpected is None)

        host = subprocess.Popen([str(editor), "-batchmode", "-projectPath", str(project),
                                 "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
                                 "-logFile", str(out / "editor.log")], env=env,
                                stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 900
        notification = None
        while time.monotonic() < deadline:
            if host.poll() is not None:
                raise RuntimeError("Editor exited; inspect editor.log")
            try:
                response = session.receive(timeout=2)
                if response.get("method") == "notifications/tools/list_changed":
                    notification = response
                    break
            except queue.Empty:
                pass
        check("real Editor late-connect notification", notification is not None)

        # The bridge can accept a connection before initial asset import ends.
        # Preserve the late-connect proof above, but run acceptance calls only
        # once the Editor is no longer compiling/updating.
        compilation = None
        while time.monotonic() < deadline:
            if host.poll() is not None:
                raise RuntimeError("Editor exited during import; inspect editor.log")
            state = subprocess.run([str(cli), "--timeout-ms", "2000", "--output", "json",
                                    "raw", "get_compilation_state", "--json", "{}"],
                                   env=env, capture_output=True, text=True, timeout=15)
            if state.returncode == 0:
                compilation = json.loads(state.stdout)["data"]
                if matrix.ready(compilation):
                    break
            time.sleep(2)
        check("Editor import and compilation ready", matrix.ready(compilation))
        (out / "compilation.json").write_text(json.dumps(compilation, indent=2) + "\n")

        catalog = inspector("tools-list", "tools/list")
        listed = subprocess.run([str(cli), "tool", "list"], env=env, capture_output=True, text=True, timeout=15)
        (out / "cli-tool-list.txt").write_text(listed.stdout)
        (out / "cli-tool-list.stderr.txt").write_text(listed.stderr)
        listed.check_returncode()
        names = sorted(tool["name"] for tool in catalog["tools"])
        check("Inspector and CLI tool names identical", names == sorted(listed.stdout.splitlines()))
        if args.custom_tools:
            described = subprocess.run([str(cli), "tool", "list", "--output", "json"],
                                       env=env, capture_output=True, text=True, check=True)
            (out / "cli-tool-descriptors.json").write_text(described.stdout)
            descriptors = {tool["name"]: tool for tool in json.loads(described.stdout)["data"]}
            # Inspector parses JSON through JavaScript Number, which rounds
            # int64 bounds. Compare its projection at that precision, then
            # independently prove exact schemas on the raw stdio transport.
            def inspector_numbers(value):
                return json.loads(json.dumps(value), parse_int=float)
            check("Inspector schema projection matches CLI", all(
                tool["description"] == descriptors[tool["name"]]["description"]
                and inspector_numbers(tool["inputSchema"]) == inspector_numbers(descriptors[tool["name"]]["params_schema"])
                for tool in catalog["tools"]))
            session.send({"jsonrpc": "2.0", "id": 3, "method": "tools/list"})
            while True:
                raw = session.receive()
                if raw.get("id") == 3:
                    break
            check("raw MCP and CLI schemas exactly identical", all(
                tool["inputSchema"] == descriptors[tool["name"]]["params_schema"]
                for tool in raw["result"]["tools"]))
            check("project custom tool is published", "mcp_probe_444" in names)
            result, envelope = inspector("custom-tool", "tools/call", "mcp_probe_444")
            check("MCP calls project custom tool", not result.get("isError") and envelope["success"]
                  and "mcp-dynamic-ok" in json.dumps(envelope["data"]))

        marker = f"McpInspector444_{args.version}"
        result, envelope = inspector("create", "tools/call", "create_gameobject", {"name": marker})
        check("Inspector creates GameObject", not result.get("isError") and envelope["success"])
        result, envelope = inspector("hierarchy", "tools/call", "get_hierarchy", {"nameOnly": True})
        check("hierarchy confirms created GameObject", envelope["success"] and marker in json.dumps(envelope["data"]))

        result, envelope = inspector("eval-authorized", "tools/call", "eval_csharp", {"code": "1 + 2", "mode": "expression"})
        check("MCP eval authenticated", not result.get("isError") and envelope["success"] and envelope["data"].get("value") == 3)
        wrong_file = out / "wrong-auth.json"
        wrong_file.write_text(json.dumps({"authToken": "deliberately-invalid-test-token"}))
        wrong_file.chmod(0o600)
        rejected_marker = "McpUnauthorized444"
        result, envelope = inspector("eval-unauthorized", "tools/call", "eval_csharp",
            {"code": f'new UnityEngine.GameObject("{rejected_marker}")', "mode": "expression"},
            {"UNITY_CLI_AUTH_TOKEN_FILE": str(wrong_file)})
        check("MCP rejects mismatched token with original envelope", result.get("isError") and not envelope["success"] and envelope["errors"][0]["code"] == "UNAUTHORIZED")
        _, envelope = inspector("hierarchy-after-rejection", "tools/call", "get_hierarchy", {"nameOnly": True})
        check("rejected eval has no scene side effect", envelope["success"] and rejected_marker not in json.dumps(envelope["data"]))
        completed = True
    finally:
        if session:
            session.close()
            (out / "late-connect.json").write_text(json.dumps(session.transcript, indent=2) + "\n")
        # Stop only this run's Editor/daemon, never another agent's instance.
        (out / "stop").touch()
        if host:
            try:
                host.wait(timeout=30)
            except subprocess.TimeoutExpired:
                matrix.stop_owned(host)
        subprocess.run([str(cli), "unityd", "stop"], env=env, capture_output=True, timeout=30)
        summary = {"started_at": started, "finished_at": datetime.now(timezone.utc).isoformat(),
                   "version": args.version, "inspector": INSPECTOR, "checks": checks,
                   "status": "PASS" if completed else "FAIL",
                   "binary_sha256": hashlib.sha256(cli.read_bytes()).hexdigest()}
        (out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")


if __name__ == "__main__":
    main()
