#!/usr/bin/env python3
"""Verify globally installed skills in a fresh Claude Code + real Unity session.

Requires an authenticated Claude Code and an already running, isolated Editor.
Only generated scenes under Assets/Scenes/Generated/E2E/ are created. Global
skills are installed without --force; existing unowned skills are never replaced.
"""

import argparse
import atexit
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--unity-cli", type=Path, default=Path("target/debug/unity-cli"))
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    project = args.project_root.resolve(strict=True)
    binary = args.unity_cli.resolve(strict=True)
    out = args.out or Path(tempfile.mkdtemp(prefix="unity-cli-skills-e2e-"))
    out = out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    env = {
        key: value
        for key, value in os.environ.items()
        if not key.startswith("GWT_") and key not in ("CLAUDECODE", "CLAUDE_CONFIG_DIR")
    }
    env.update(
        PATH=str(binary.parent) + os.pathsep + env["PATH"],
        UNITY_CLI_HOST="127.0.0.1",
        UNITY_CLI_PORT=str(args.port),
        UNITY_PROJECT_ROOT=str(project),
        UNITY_CLI_NO_AUTO_UPDATE="1",
        UNITY_CLI_TOOLS_ROOT=str(out / "tools"),
    )

    def stop_test_daemon():
        try:
            subprocess.run([str(binary), "unityd", "stop"], cwd=project, env=env,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)
        except (OSError, subprocess.TimeoutExpired):
            pass

    atexit.register(stop_test_daemon)

    def cli(*command):
        result = subprocess.run(
            [str(binary), *command], cwd=project, env=env,
            capture_output=True, text=True, timeout=60, check=True,
        )
        return result.stdout

    target = ["--host", "127.0.0.1", "--port", str(args.port), "--output", "json"]
    (out / "ping.json").write_text(cli(*target, "system", "ping"))
    (out / "install-paths.txt").write_text(cli("skills", "install", "claude-code"))
    skill = Path.home() / ".claude/skills/unity-scene-create/SKILL.md"
    embedded = cli("skills", "show", "unity-scene-create")
    if skill.read_text().strip() != embedded.strip():
        raise RuntimeError("installed scene skill differs from the embedded version")

    settings = {"disableAllHooks": True}
    user_settings = Path.home() / ".claude/settings.json"
    if user_settings.is_file():
        plugins = json.loads(user_settings.read_text()).get("enabledPlugins", {})
        settings["enabledPlugins"] = {name: False for name in plugins}
    name = "SkillInstall" + uuid.uuid4().hex[:12]
    scene_path = f"Assets/Scenes/Generated/E2E/{name}.unity"
    prompt = (
        "/unity-scene-create This is the approved automated acceptance test for "
        "unity-cli skills install. Confirm this installed skill is loaded; if it "
        "is unknown, stop and report failure without creating anything. "
        f"Use the skill to create and save a new scene named {name} in "
        "Assets/Scenes/Generated/E2E/ in the current project. "
        "All Bash commands must use bash -lc with this project as working directory. "
        f"Use the binary {binary}, always explicitly passing --host 127.0.0.1 "
        f"--port {args.port}. The Editor is already running; do not run setup, "
        "install, or quit it. Do not edit any scripts, settings, or scene YAML "
        "directly. Do not launch subagents. Inspect the created scene using "
        "a read-only CLI call, then report the skill source and scene path. "
        "Default scene objects are fine; no extra objects or visual review."
    )
    command = [
        "claude", "-p", prompt, "--output-format", "stream-json", "--verbose",
        "--no-session-persistence", "--setting-sources", "user",
        "--settings", json.dumps(settings), "--strict-mcp-config",
        "--mcp-config", '{"mcpServers":{}}', "--no-chrome",
        "--tools", "Bash,Read,Skill", "--allowedTools", "Bash(bash -lc *)",
        "Read", "Skill", "--permission-mode", "dontAsk", "--max-turns", "12",
    ]
    (out / "claude-command.json").write_text(json.dumps(command, indent=2))
    print(f"Fresh Claude Code session; evidence: {out}", flush=True)
    with (out / "claude-session.jsonl").open("w") as stdout, (out / "claude-stderr.log").open("w") as stderr:
        subprocess.run(command, cwd=project, env=env, stdout=stdout, stderr=stderr,
                       timeout=300, check=True)
    events = [json.loads(line) for line in (out / "claude-session.jsonl").read_text().splitlines()]
    init = next(event for event in events if event.get("type") == "system" and event.get("subtype") == "init")
    result = next(event for event in events if event.get("type") == "result")
    if "unity-scene-create" not in init.get("slash_commands", []):
        raise RuntimeError("fresh session did not discover /unity-scene-create")
    if result.get("is_error") or result.get("permission_denials"):
        raise RuntimeError("Claude session failed or a required tool was denied")
    scene = json.loads(cli(*target, "raw", "get_scene_info", "--json", json.dumps({"scenePath": scene_path})))["data"]
    (out / "scene.json").write_text(json.dumps(scene, indent=2))
    if scene.get("scenePath") != scene_path or not scene.get("isLoaded") or scene.get("isDirty"):
        raise RuntimeError("Editor did not confirm the new scene was loaded and saved")
    scene_file = project / scene_path
    if not scene_file.is_file() or not scene_file.with_suffix(".unity.meta").is_file():
        raise RuntimeError("Editor did not persist the scene and its metadata")
    evidence = {
        "status": "PASS", "claude_session": init["session_id"],
        "binary": str(binary), "binary_sha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
        "installed_skill": str(skill), "skill_sha256": hashlib.sha256(skill.read_bytes()).hexdigest(),
        "scene": scene, "artifacts": str(out),
    }
    (out / "acceptance.json").write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence, indent=2))


if __name__ == "__main__":
    main()
