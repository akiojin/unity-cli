"""Validate recorded Windows evidence; this does not rerun Unity Editor."""

import hashlib
import json
from pathlib import Path


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def load(name):
    value = json.loads((HERE / name).read_text(encoding="utf-8"))
    assert "authToken" not in json.dumps(value), f"credential in {name}"
    return value


def check(name, condition):
    assert condition, name
    print(f"PASS: {name}")


summary = load("summary.json")
setup = load("setup.json")
discovery = load("discovery.json")
ping = load("ping.json")
lock = load("lockfile.json")
sources = load("bridge-source.json")

check("all three actual CLI invocations succeeded",
      all(value["success"] for value in (setup, discovery, ping))
      and len(summary["commands"]) == 3
      and all(command["exit_code"] == 0 for command in summary["commands"]))
setup = setup["data"]
ping = ping["data"]
check("AC-2 actual installation changed the manifest without dry-run",
      setup["ok"] and setup["bridge"]["manifestChanged"]
      and setup["bridge"]["dryRun"] is False
      and setup["bridge"]["source"] == "registry"
      and setup["bridge"]["declared"] == "0.19.0")
check("AC-2 launched real Windows Unity with a matching project and Bridge",
      setup["launch"]["launched"] and setup["launch"]["editorPath"].endswith("Unity.exe")
      and setup["editor"]["reachable"] and setup["editor"]["projectMatches"]
      and setup["editor"]["versionCheck"]["status"] == "match")
check("AC-1 Bridge lockfile matches the launched PID, port and Unity version",
      lock["pid"] == setup["launch"]["pid"] == summary["editor_pid"]
      and lock["port"] == setup["launch"]["port"]
      and lock["unityVersion"] == setup["launch"]["unityVersion"] == summary["unity_version"]
      and lock["state"] == "ready")
check("AC-1 discovery reports that live lockfile",
      any(row["source"] == "lockfile" and row["status"] == "up"
          and row.get("pid") == lock["pid"] and row["port"] == lock["port"]
          and row["project_path"] == lock["projectPath"] for row in discovery["data"]))
check("AC-1 ping reached the same project and Bridge",
      ping["message"] == "pong"
      and ping["projectPath"] == lock["projectPath"] == setup["editor"]["projectPath"]
      and ping["bridgeVersion"] == lock["bridgeVersion"] == "0.19.0")
check("discovery and ping used no explicit port or ports",
      all(not any(arg in ("--port", "--ports") for arg in command["argv"])
          for command in summary["commands"] if command["name"] in ("discovery", "ping")))
check("the owned Editor exited and removed its lockfile",
      summary["editor_exited"] and summary["editor_lockfile_removed_on_exit"])

package = ROOT / "UnityCliBridge/Packages/unity-cli-bridge"
recorded = {row["path"] for row in sources}
check("source inventory includes every Bridge C# file",
      bool(recorded) and len(recorded) == len(sources)
      and recorded == {path.relative_to(package).as_posix() for path in package.rglob("*.cs")})
for row in sources:
    digest = hashlib.sha256((package / row["path"]).read_text(encoding="utf-8-sig").encode()).hexdigest()
    assert digest == row["checkout_sha256"] == row["installed_sha256"], row["path"]
print(f"PASS: installed Bridge matches all {len(sources)} checkout C# files (newlines normalized)")
print("Recorded live CLI checks: PASS; record validation: PASS; Unity was not rerun.")
