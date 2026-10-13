"""Measure real CLI/LSP writes against independent direct-edit byte oracles (#490).

No Unity Editor is needed: syntax and filesystem editing are the measured surface.
Failure observations are data; an explicit baseline gates *new* checkout regressions.
The initial-download route deliberately measures the published LSP, not a mock.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import difflib
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import shutil
import socket
import subprocess
import tempfile
import time

CONDITIONS = ("warm_indexed", "daemon_stopped", "unindexed", "first_download")
OS_NAMES = ("Windows", "macOS", "Linux")
BOM = b"\xef\xbb\xbf"
CORRUPTION = {"bom_added", "bom_removed", "newlines_changed", "text_changed", "file_set_changed", "wrong_backend"}


def scenarios():
    cases = []

    def add(scenario_name, tool, before, after, **params):
        encode = lambda files: {key: value.encode("utf-8") for key, value in files.items()}
        cases.append({"id": scenario_name, "tool": tool, "before": encode(before),
                      "after": encode(after), "params": params})

    path = "Assets/Sample.cs"
    for kind, parent in (("monobehaviour", "MonoBehaviour"), ("scriptableobject", "ScriptableObject"), ("plain", None)):
        text = ("using UnityEngine;\n\n" if parent else "") + "public class Sample" + (f" : {parent}" if parent else "") + "\n{\n}\n"
        add("create_class_" + kind, "create_class", {}, {path: text}, name="Sample", path=path, **({"inherits": parent} if parent else {}))
    for kind, text in (
        ("plain", "public class Sample {}\n"),
        ("monobehaviour", "using UnityEngine;\npublic class Sample : MonoBehaviour {}\n"),
        ("scriptableobject", "using UnityEngine;\npublic class Sample : ScriptableObject {}\n"),
    ):
        add("create_" + kind, "create_csharp_file", {}, {path: text}, relative=path, text=text)
    original = "public class Sample { public int Run(){return 1;}}\n"
    updated = original.replace("return 1;", "return 2;")
    add("replace_body", "replace_symbol_body", {path: original}, {path: updated}, relative=path, namePath="Sample/Run", body="{return 2;}")
    add("write_file", "write_csharp_file", {path: original}, {path: updated}, relative=path, newText=updated)
    # Compact source makes insertion/removal trivia unambiguous to the direct oracle.
    member = "public int Added;"
    compact = "public class Sample {public int Run(){return 1;}}\n"
    add("insert_before", "insert_before_symbol", {path: compact}, {path: compact.replace("public int Run", member + "public int Run")}, relative=path, namePath="Sample/Run", text=member)
    add("insert_after", "insert_after_symbol", {path: compact}, {path: compact.replace("return 1;}", "return 1;}" + member)}, relative=path, namePath="Sample/Run", text=member)
    add("remove_member", "remove_symbol", {path: compact}, {path: "public class Sample {}\n"}, relative=path, namePath="Sample/Run")
    shaped = "namespace Demo { [System.Serializable] public partial class Sample<T> { public T Run(){return default(T);}} }\n"
    add("namespace_partial_generic_attributes", "replace_symbol_body", {path: shaped}, {path: shaped.replace("return default(T);", "throw new System.Exception();")}, relative=path, namePath="Sample/Run", body="{throw new System.Exception();}")
    commented = "public class Sample { public int Run(){return 1;} // 日本語の説明を保持\npublic int Next(){return 3;} }\n"
    add("replace_preserve_comment", "replace_symbol_body", {path: commented}, {path: commented.replace("return 1;", "return 2;")}, relative=path, namePath="Sample/Run", body="{return 2;}")
    declaration = "namespace Demo { public partial class Sample {} }\n"
    use = "namespace Demo { public class Consumer { public Sample Value; } }\n"
    unrelated = "namespace Other { public class Sample {} public class Consumer { public Sample Value; } }\n"
    add("rename_multifile", "rename_symbol", {path: declaration, "Assets/Use.cs": use, "Assets/Other.cs": unrelated},
        {path: declaration.replace("Sample", "Renamed"), "Assets/Use.cs": use.replace("Sample", "Renamed"), "Assets/Other.cs": unrelated},
        relative=path, namePath="Sample", newName="Renamed")
    for bom in (False, True):
        unicode = ("\ufeff" if bom else "") + "// 日本語コメント 🐈\r\n" + original.replace("\n", "\r\n")
        new = unicode.replace("return 1;", "return 2;")
        # The BOM is a file encoding property, not part of the LSP newText string.
        add("unicode_" + ("bom" if bom else "no_bom") + "_crlf", "write_csharp_file", {path: unicode}, {path: new}, relative=path, newText=new.lstrip("\ufeff"))
    large = "public class Sample {\n" + "".join(f"    public int Field{i} = {i};\n" for i in range(3500)) + "}\n"
    large_new = large.replace("Field3499 = 3499", "Field3499 = 42")
    add("large_3500_lines", "write_csharp_file", {path: large}, {path: large_new}, relative=path, newText=large_new)
    broken = "public class Sample { public int Run(){return 1} }\n"
    add("repair_syntax_error", "apply_csharp_edits", {path: broken, "Assets/Second.cs": "public class Second {}\n"},
        {path: original, "Assets/Second.cs": "public class Second { public int Added; }\n"},
        files=[{"relative": path, "newText": original}, {"relative": "Assets/Second.cs", "newText": "public class Second { public int Added; }\n"}])
    add("validate_without_write", "validate_text_edits", {path: original}, {path: original}, relative=path, newText=updated)
    return cases


def compare_files(expected, actual):
    signatures, differences = set(), []
    for name in sorted(set(expected) | set(actual)):
        before, after = expected.get(name), actual.get(name)
        if before == after:
            continue
        if before is None or after is None:
            signatures.add("file_set_changed")
        else:
            if before.startswith(BOM) != after.startswith(BOM):
                signatures.add("bom_removed" if before.startswith(BOM) else "bom_added")
            left, right = before.removeprefix(BOM), after.removeprefix(BOM)
            if left.replace(b"\r\n", b"\n") == right.replace(b"\r\n", b"\n"):
                if left != right:
                    signatures.add("newlines_changed")
            else:
                signatures.add("text_changed")
        left_text = (before or b"").decode("utf-8", errors="replace").splitlines(keepends=True)
        right_text = (after or b"").decode("utf-8", errors="replace").splitlines(keepends=True)
        differences.append({"path": name, "direct_sha256": hashlib.sha256(before).hexdigest() if before is not None else None,
                            "lsp_sha256": hashlib.sha256(after).hexdigest() if after is not None else None,
                            "direct_text_sha256": hashlib.sha256(before.removeprefix(BOM).replace(b"\r\n", b"\n")).hexdigest() if before is not None else None,
                            "lsp_text_sha256": hashlib.sha256(after.removeprefix(BOM).replace(b"\r\n", b"\n")).hexdigest() if after is not None else None,
                            "direct_bytes": len(before) if before is not None else None,
                            "lsp_bytes": len(after) if after is not None else None,
                            "diff": "".join(difflib.unified_diff(left_text, right_text, fromfile="direct/" + name, tofile="lsp/" + name))})
    return sorted(signatures), differences


def response_failures(returncode, envelope):
    data = envelope.get("data", {})
    if not isinstance(data, dict):
        return ["other"]
    if data.get("success") is False:
        return ["tool_rejected"]
    if returncode != 0 or envelope.get("success") is not True or data.get("success") is not True:
        return ["other"]
    return [] if data.get("backend") == "lsp" else ["wrong_backend"]


def summarize(rows):
    def percentile(key, fraction):
        values = sorted(row[key] for row in rows)
        return round(values[max(0, math.ceil(len(values) * fraction) - 1)], 3) if values else None
    failures = Counter(failure for row in rows for failure in row["failures"])
    return {"attempts": len(rows), "successes": sum(not row["failures"] for row in rows),
            "success_rate": sum(not row["failures"] for row in rows) / len(rows) if rows else 0,
            "failure_types": dict(failures), "p50_ms": percentile("elapsed_ms", 0.5), "p95_ms": percentile("elapsed_ms", 0.95),
            "direct_p50_ms": percentile("direct_ms", 0.5), "direct_p95_ms": percentile("direct_ms", 0.95)}


def regressions(rows, baseline):
    result = []
    expected_paths = {case["id"]: {name for name in set(case["before"]) | set(case["after"])
                                   if case["before"].get(name) != case["after"].get(name)} for case in scenarios()}
    for row in rows:
        known = baseline.get(row["scenario"], {})
        allowed = known.get("failures", []) if isinstance(known, dict) else known
        new = set(row["failures"]) - set(allowed)
        if row["scenario"] in expected_paths and any(d["path"] not in expected_paths[row["scenario"]] for d in row.get("differences", [])):
            new.add("unexpected_file_difference")
        # A known text defect may never mask a different destructive edit.
        if "text_changed" in row["failures"]:
            hashes = known.get("text_hashes", {}) if isinstance(known, dict) else {}
            changes = [d for d in row.get("differences", []) if d.get("direct_text_sha256") != d.get("lsp_text_sha256")]
            if not changes or any(hashes.get(d["path"]) != d.get("lsp_text_sha256") for d in changes):
                new.add("unexpected_text_difference")
        if new:
            result.append({"scenario": row["scenario"], "condition": row.get("condition"), "new_failures": sorted(new)})
    return result


def markdown(reports):
    groups = {}
    for report in reports:
        for row in report["rows"]:
            groups.setdefault((report["os"], row["condition"], row["scenario"]), []).append(row)
    missing = [f"{system}/{condition}/{case['id']}" for system in OS_NAMES for condition in CONDITIONS for case in scenarios()
               if (system, condition, case["id"]) not in groups]
    corrupt = any(CORRUPTION.intersection(row["failures"]) for rows in groups.values() for row in rows)
    incomplete = missing or any(report.get("interrupted") or not report.get("finished_at") for report in reports)
    qualification = "INSUFFICIENT" if incomplete else "ELIGIBLE" if not corrupt and all(summarize(rows)["success_rate"] >= 0.99 for rows in groups.values()) else "DO NOT ENFORCE"
    lines = ["# C# LSP write reliability", "", f"Decision evidence: **{qualification}**.", "",
             "直接編集の禁止はオーナー判断です。全シナリオ・全 OS で成功率 99% 以上、誤編集・BOM/改行の破壊 0 件を目安にします。",
             "バイト差分がある試行は失敗として数えます。少数サンプルの 100% は将来の 99% の保証ではありません。",
             "初回ダウンロードは公開 LSP、warm/unindexed は checkout LSP、自動起動は LSP 更新を含みます。各試行の VERSION/SHA256 を区別します。Unity のコンパイル・refresh は測定範囲外です。", "",
             "Launch mode: autonomous", "User Verification Result: n/a (autonomous)", "Agent Visual Check: n/a (no UI surface)", "",
             "| OS | Condition | Scenario | Success / attempts | Rate | LSP p50 / p95 ms | Direct p50 / p95 ms | Failure signatures |",
             "| --- | --- | --- | --- | --- | --- | --- | --- |"]
    for (system, condition, scenario), rows in sorted(groups.items()):
        value = summarize(rows)
        lines.append(f"| {system} | {condition} | {scenario} | {value['successes']}/{value['attempts']} | {value['success_rate']:.1%} | {value['p50_ms']} / {value['p95_ms']} | {value['direct_p50_ms']} / {value['direct_p95_ms']} | {json.dumps(value['failure_types'])} |")
    lines += ["", "## Coverage", "", f"Missing OS/condition/scenario cells: {len(missing)}."]
    if missing:
        lines.append("Missing operating systems: " + ", ".join(system for system in OS_NAMES if not any(key[0] == system for key in groups)))
    lines += ["", "## Defects to fix before enforcement", ""]
    defects = sorted({failure for rows in groups.values() for row in rows for failure in row["failures"]})
    lines += ["- " + failure + ": see per-attempt raw response, SHA256 and unified diff in the JSON artifact." for failure in defects]
    if not defects:
        lines.append("No defects observed in executed samples; unexecuted cells remain unproven.")
    return "\n".join(lines) + "\n"


def rid():
    system = {"Windows": "win", "Darwin": "osx", "Linux": "linux"}[platform.system()]
    architecture = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
    return system + "-" + architecture


def write_files(root, files):
    for name, data in files.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)


def snapshot(root):
    return {path.relative_to(root).as_posix(): path.read_bytes() for path in root.rglob("*.cs")}


def sha256_file(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def cleanup_trial(directory, root):
    if not directory.resolve().is_relative_to(root.resolve()) or directory.resolve() == root.resolve():
        raise RuntimeError("refusing to clean a trial outside the measurement directory")
    deadline = time.monotonic() + 10
    while True:
        try:
            shutil.rmtree(directory)
            return
        except PermissionError:
            # Windows can keep clrjit.dll locked briefly after lspd exits.
            if time.monotonic() >= deadline:
                raise
            time.sleep(0.1)


class Trial:
    def __init__(self, cli, lsp_publish, root, condition, timeout):
        self.cli, self.root, self.condition, self.timeout = cli, root, condition, timeout
        self.project = root / "project"
        (self.project / "Assets").mkdir(parents=True)
        self.tools = root / "tools"
        self.env = os.environ.copy()
        self.env.update({"UNITY_CLI_TOOLS_ROOT": str(self.tools), "UNITY_CLI_CACHE_ROOT": str(root / "cache"),
                         "UNITY_CLI_REGISTRY_PATH": str(root / "registry.json"), "UNITY_PROJECT_ROOT": str(self.project),
                         "UNITY_CLI_LSP_MODE": "required", "UNITY_CLI_NO_AUTO_UPDATE": "1", "UNITY_CLI_LSPD_IDLE_TIMEOUT": "30"})
        # Never silently inherit a switch which disables the real download route.
        self.env.pop("UNITY_CLI_TEST_SKIP_MANAGED_UPDATE", None)
        install = self.tools / "unity-cli" / rid()
        install.mkdir(parents=True)
        shutil.copy2(cli, install / cli.name)
        (install / "VERSION").write_text("checkout\n", encoding="utf-8")
        self.server = self.tools / "csharp-lsp" / rid() / ("server.exe" if os.name == "nt" else "server")
        if condition != "first_download":
            shutil.copytree(lsp_publish, self.server.parent)
            source = self.server.parent / ("Server.exe" if os.name == "nt" else "Server")
            if source != self.server and (os.name != "nt" or source.name.lower() != self.server.name.lower()):
                shutil.copy2(source, self.server)
            self.server.chmod(0o755)
            (self.server.parent / "VERSION").write_text("checkout\n", encoding="utf-8")
            self.env["UNITY_CLI_TEST_SKIP_MANAGED_UPDATE"] = "1"
        self.process = None
        self.log = None

    def command(self, args):
        return [str(self.cli), "--output", "json", "--project-path", str(self.project), *args]

    def call(self, tool, params):
        # --params-file also covers large inputs on Windows without its argv limit.
        payload = self.root / "params.json"
        payload.write_text(json.dumps(params, ensure_ascii=False), encoding="utf-8")
        command = self.command(["raw", tool, "--params-file", str(payload)])
        completed = subprocess.run(command, env=self.env, capture_output=True, timeout=self.timeout)
        stdout = completed.stdout.decode("utf-8", errors="replace")
        stderr = completed.stderr.decode("utf-8", errors="replace")
        try:
            envelope = json.loads(stdout)
        except ValueError:
            envelope = {}
        return completed.returncode, envelope, stdout, stderr

    def start(self):
        # Windows lspd currently has a host-wide fixed port. Refuse to share it.
        if os.name == "nt":
            with socket.socket() as probe:
                if probe.connect_ex(("127.0.0.1", 6421)) == 0:
                    raise RuntimeError("lsp_startup_failure: lspd port 6421 already occupied; no process was stopped")
        self.log = (self.root / "daemon.log").open("wb")
        self.process = subprocess.Popen(self.command(["lspd", "serve"]), env=self.env, stdout=self.log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError("lsp_startup_failure: owned lspd exited: " + (self.root / "daemon.log").read_text(errors="replace"))
            completed = subprocess.run(self.command(["lspd", "status"]), env=self.env, capture_output=True, timeout=10)
            try:
                if json.loads(completed.stdout).get("data", {}).get("running") is True:
                    return
            except ValueError:
                pass
            time.sleep(0.05)
        raise RuntimeError("lsp_startup_failure: owned lspd did not become ready")

    def close(self):
        # A PID file in this unique tools root identifies the auto-started daemon.
        pid_file = self.tools / "run" / "lspd" / "lspd.pid"
        owned = pid_file.exists()
        if owned and self.process is not None:
            owned = pid_file.read_text().strip() == str(self.process.pid)
        if owned:
            try:
                subprocess.run(self.command(["lspd", "stop"]), env=self.env, capture_output=True, timeout=15)
            except subprocess.TimeoutExpired:
                pass
        if self.process is not None:
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()  # Only the exact child started by this trial.
                self.process.wait(timeout=10)
        if self.log is not None:
            self.log.close()


def measure(cli, lsp_publish, output, condition, case, sample, timeout):
    directory = output / "trials" / f"{condition}-{case['id']}-{sample}"
    directory.mkdir(parents=True)
    trial = Trial(cli, lsp_publish, directory, condition, timeout)
    write_files(trial.project, case["before"])
    direct = directory / "direct"
    write_files(direct, case["before"])
    started = time.perf_counter()
    for name in set(case["before"]) - set(case["after"]):
        (direct / name).unlink()
    write_files(direct, case["after"])
    direct_ms = (time.perf_counter() - started) * 1000
    row = {"scenario": case["id"], "tool": case["tool"], "condition": condition, "sample": sample,
           "direct_ms": direct_ms, "failures": [], "differences": []}
    started = time.perf_counter()
    try:
        if condition in ("warm_indexed", "unindexed"):
            trial.start()
        elif os.name == "nt":
            with socket.socket() as probe:
                if probe.connect_ex(("127.0.0.1", 6421)) == 0:
                    raise RuntimeError("lsp_startup_failure: lspd port 6421 already occupied")
        if condition == "warm_indexed":
            rc, value, _, error = trial.call("build_index", {})
            if rc or value.get("success") is not True:
                raise RuntimeError("lsp_startup_failure: index setup failed: " + error + json.dumps(value))
        row["setup_ms"] = (time.perf_counter() - started) * 1000
        started = time.perf_counter()
        params = dict(case["params"])
        if case["tool"] not in ("validate_text_edits", "create_class"):
            params["apply"] = True
        if case["tool"] in ("create_csharp_file", "write_csharp_file", "apply_csharp_edits"):
            params.update({"refresh": False, "waitForCompile": False})
        rc, value, stdout, stderr = trial.call(case["tool"], params)
        row.update({"returncode": rc, "response": value, "stdout": stdout, "stderr": stderr})
        row["failures"] = response_failures(rc, value)
        error = (stdout + stderr).lower()
        if row["failures"]:
            if "timeout" in error or "timed out" in error:
                row["failures"] = ["timeout"]
            elif "failed to start" in error or "failed to spawn" in error or "failed to connect" in error:
                row["failures"] = ["lsp_startup_failure"]
            elif "manifest" in error or "download" in error:
                row["failures"] = ["download_failure"]
    except subprocess.TimeoutExpired as error:
        row.update({"failures": ["timeout"], "error": str(error)})
    except (RuntimeError, OSError) as error:
        row.update({"failures": ["lsp_startup_failure"], "error": str(error)})
    finally:
        row["elapsed_ms"] = (time.perf_counter() - started) * 1000
        signatures, row["differences"] = compare_files(snapshot(direct), snapshot(trial.project))
        row["failures"] = sorted(set(row["failures"] + signatures))
        if trial.server.exists():
            row["lsp_version"] = (trial.server.parent / "VERSION").read_text().strip()
            row["lsp_sha256"] = sha256_file(trial.server)
        trial.close()
    # Delete only this generated trial, after resolving its boundary explicitly.
    cleanup_trial(directory, output)
    return row


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-cli", type=Path)
    parser.add_argument("--lsp-publish", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--repetitions", type=int, default=5)
    parser.add_argument("--download-repetitions", type=int, default=1)
    parser.add_argument("--cold-repetitions", type=int, default=1)
    parser.add_argument("--conditions", default=",".join(CONDITIONS))
    parser.add_argument("--scenarios", help="Comma-separated scenario IDs for a focused recovery run; default: all")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--record-only", action="store_true", help="Collect failures as evidence without a regression gate")
    parser.add_argument("--merge", type=Path, nargs="+", help="Aggregate existing JSON reports; no tests are executed")
    args = parser.parse_args()
    args.out = args.out.resolve()
    args.out.mkdir(parents=True, exist_ok=True)
    if args.merge:
        reports = [json.loads(path.read_text(encoding="utf-8")) for path in args.merge]
        (args.out / "summary.md").write_text(markdown(reports), encoding="utf-8")
        return 0
    conditions = args.conditions.split(",")
    cases = scenarios()
    if args.scenarios:
        selected = args.scenarios.split(",")
        if any(name not in {case["id"] for case in cases} for name in selected):
            parser.error("unknown scenario ID")
        cases = [case for case in cases if case["id"] in selected]
    if not args.unity_cli or not args.lsp_publish or min(args.repetitions, args.download_repetitions, args.cold_repetitions) < 1 or any(c not in CONDITIONS for c in conditions):
        parser.error("supply --unity-cli/--lsp-publish, positive repetitions and valid conditions")
    cli, publish = args.unity_cli.resolve(), args.lsp_publish.resolve()
    if not cli.is_file() or not (publish / ("Server.exe" if os.name == "nt" else "Server")).is_file():
        parser.error("build the CLI and publish the real LSP for the current RID first")
    # Use a short temporary path on Unix for the daemon's Unix socket limit.
    reports = {"schema_version": 1, "os": "macOS" if platform.system() == "Darwin" else platform.system(),
               "rid": rid(), "started_at": datetime.now(timezone.utc).isoformat(), "cli_sha256": sha256_file(cli),
               "runner_sha256": sha256_file(Path(__file__)),
               "requested_conditions": conditions, "repetitions": args.repetitions,
               "cold_repetitions": args.cold_repetitions, "download_repetitions": args.download_repetitions,
               "rows": []}
    with tempfile.TemporaryDirectory(prefix="lsp490-") as temp:
        for condition in conditions:
            count = args.download_repetitions if condition == "first_download" else args.cold_repetitions if condition == "daemon_stopped" else args.repetitions
            for case in cases:
                for sample in range(count):
                    row = measure(cli, publish, Path(temp), condition, case, sample, args.timeout)
                    reports["rows"].append(row)
                    print(f"{condition}/{case['id']}/{sample}: {','.join(row['failures']) or 'PASS'}", flush=True)
                    # Persist each measured row so a later abort cannot erase evidence.
                    (args.out / "results.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    reports["summary"] = summarize(reports["rows"])
    baseline = json.loads(args.baseline.read_text(encoding="utf-8")) if args.baseline else {}
    reports["regressions"] = regressions(reports["rows"], baseline)
    reports["finished_at"] = datetime.now(timezone.utc).isoformat()
    (args.out / "results.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    (args.out / "summary.md").write_text(markdown([reports]), encoding="utf-8")
    print(json.dumps(reports["summary"]))
    return 1 if reports["regressions"] and not args.record_only else 0


if __name__ == "__main__":
    raise SystemExit(main())
