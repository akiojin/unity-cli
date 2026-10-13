"""Check the Windows cold-start latency AC without hiding write failures."""
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("reliability", ROOT / "scripts/lsp-write-reliability.py")
reliability = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(reliability)

report_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("after.json")
report = json.loads(report_path.read_text(encoding="utf-8"))
assert report["os"] == "Windows" and report["rid"] == "win-x64"
assert report.get("finished_at"), "measurement is incomplete"
rows = [row for row in report["rows"]
        if row["condition"] == "daemon_stopped" and row["scenario"] == "write_file"]
assert len(rows) >= 3, "at least three independent cold starts are required"
assert len({row["sample"] for row in rows}) == len(rows), "duplicate samples"
for row in rows:
    assert row["returncode"] == 0 and not reliability.response_failures(row["returncode"], row["response"])
baseline = json.loads((ROOT / "tests/fixtures/lsp-write-baseline.json").read_text(encoding="utf-8"))
assert not reliability.regressions(rows, baseline), "a new write defect must not count as a latency pass"
summary = reliability.summarize(rows)
assert summary["p50_ms"] < 10_000, f"Windows daemon_stopped p50 exceeds the AC: {summary}"
print(f"PASS: Windows daemon_stopped/write_file n={len(rows)}, "
      f"p50={summary['p50_ms']}ms, p95={summary['p95_ms']}ms; "
      f"recorded write defects={summary['failure_types']}")
