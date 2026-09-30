#!/usr/bin/env python3
"""CLI spawn-to-exit latency against deterministic framed TCP responses."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import socket
import socketserver
import struct
import subprocess
import tempfile
import threading
import time
from perf_gate import check, summarize

ROOT = Path(__file__).resolve().parents[1]


def recv_exact(stream, count):
    data = b""
    while len(data) < count:
        chunk = stream.recv(count - len(data))
        if not chunk:
            raise EOFError()
        data += chunk
    return data


class Bridge(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self, delay_ms):
        self.delay = delay_ms / 1000
        self.connections = 0
        super().__init__(("127.0.0.1", 0), Handler)


class Handler(socketserver.BaseRequestHandler):
    def handle(self):
        self.server.connections += 1
        self.request.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        try:
            while True:
                length = struct.unpack(">I", recv_exact(self.request, 4))[0]
                request = json.loads(recv_exact(self.request, length))
                if self.server.delay:
                    time.sleep(self.server.delay)
                result = {"mock": True, "operation": request["type"], "success": True}
                payload = json.dumps({"id": request["id"], "status": "success", "result": result}).encode()
                self.request.sendall(struct.pack(">I", len(payload)) + payload)
        except (EOFError, ConnectionError):
            return


def invoke(argv, env):
    started = time.perf_counter()
    proc = subprocess.run(argv, env=env, capture_output=True, text=True, timeout=30)
    elapsed = (time.perf_counter() - started) * 1000
    if proc.returncode:
        raise RuntimeError(f"{argv}: exit {proc.returncode}: {proc.stdout} {proc.stderr}")
    return elapsed, proc.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-cli", type=Path, default=ROOT / "target/release/unity-cli")
    parser.add_argument("--iterations", type=int, default=50)
    parser.add_argument("--delay-ms", type=float, default=0, help="inject Bridge delay to prove gate rejection")
    parser.add_argument("--budgets", type=Path, default=ROOT / "perf-budgets.json")
    parser.add_argument("--out", type=Path, default=ROOT / ".unity/perf/cli-latency.json")
    args = parser.parse_args()
    if args.iterations < 50 or args.delay_ms < 0:
        parser.error("at least 50 samples and a non-negative delay are required")
    binary = str(args.unity_cli.resolve())
    results, violations, routes = {}, [], {}
    with tempfile.TemporaryDirectory(prefix="cli-perf-") as temporary, Bridge(args.delay_ms) as bridge:
        thread = threading.Thread(target=bridge.serve_forever, daemon=True)
        thread.start()
        runtime = Path(temporary)
        # Exercise the existing direct fallback: a regular file cannot hold a daemon socket.
        # This avoids introducing a product flag that contradicts always-auto-managed unityd.
        disabled = runtime / "unavailable"
        disabled.touch()
        env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1", UNITY_CLI_UNITYD_IDLE_TIMEOUT="3600")
        env.pop("UNITY_CLI_UNITYD", None)
        base = [binary, "--output", "json", "--host", "127.0.0.1", "--port", str(bridge.server_address[1])]
        daemon_env = dict(env, UNITY_CLI_TOOLS_ROOT=str(runtime / "daemon"))
        daemon = None
        try:
            for name, command in [("startup", ["--help"]), ("tool_list", ["tool", "list"])]:
                samples = []
                for i in range(args.iterations + 3):
                    elapsed, stdout = invoke([binary] + command, daemon_env)
                    if not stdout.strip():
                        raise RuntimeError(f"{name}: empty output")
                    if i >= 3:
                        samples.append(elapsed)
                results["cli_" + name] = summarize(samples)
            daemon = subprocess.Popen([binary, "unityd", "serve"], env=daemon_env,
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            deadline = time.monotonic() + 10
            while not json.loads(invoke([binary, "--output", "json", "unityd", "status"], daemon_env)[1]).get("running"):
                if daemon.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError("owned daemon did not start")
                time.sleep(.05)
            commands = [("ping", ["system", "ping"], "ping"),
                        ("state", ["raw", "get_editor_state", "--json", "{}"], "get_editor_state"),
                        ("batch5", ["batch", "--json", json.dumps([{"tool": "get_editor_state", "params": {}}] * 5)],
                         "get_editor_state")]
            for route in ("direct", "unityd"):
                route_env = dict(daemon_env, UNITY_CLI_TOOLS_ROOT=str(disabled)) if route == "direct" else daemon_env
                before = bridge.connections
                for name, command, operation in commands:
                    samples = []
                    for i in range(args.iterations + 3):
                        elapsed, stdout = invoke(base + command, route_env)
                        result = json.loads(stdout)
                        values = [item["result"] for item in result if item.get("ok")] if name == "batch5" else [result]
                        if len(values) != (5 if name == "batch5" else 1) or any(
                                value.get("mock") is not True or value.get("operation") != operation for value in values):
                            raise RuntimeError(f"{route}/{name}: unexpected result {stdout}")
                        if i >= 3:
                            samples.append(elapsed)
                    results[f"cli_{route}_{name}"] = summarize(samples)
                    print(f"measured {route}/{name}: {len(samples)} samples", flush=True)
                connections = bridge.connections - before
                routes[route] = {"tcp_connections": connections}
                expected = 3 * (args.iterations + 3) if route == "direct" else 1
                if connections != expected:
                    violations.append(f"{route}: expected {expected} TCP connections, got {connections}")
            violations += check(results, json.loads(args.budgets.read_text())["budgets"])
        except Exception as error:
            violations.append(str(error))
        finally:
            if daemon is not None:
                daemon.terminate()
                try:
                    daemon.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    daemon.kill()
                    daemon.wait()
            bridge.shutdown()
    report = {"timestamp": datetime.now(timezone.utc).isoformat(), "results": results, "routes": routes,
              "sample": "wall-clock CLI process spawn to exit; 3 warmup calls excluded",
              "injected_delay_ms": args.delay_ms, "violations": violations,
              "status": "FAIL" if violations else "PASS"}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return int(bool(violations))


if __name__ == "__main__":
    raise SystemExit(main())
