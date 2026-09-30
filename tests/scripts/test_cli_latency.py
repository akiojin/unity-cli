"""Run with UNITY_CLI_PERF_TEST_BINARY pointing at the release binary."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class CliLatencyTests(unittest.TestCase):
    def test_benchmark_entrypoint_exists(self):
        self.assertTrue((ROOT / "scripts/bench-cli-latency.py").exists())

    @unittest.skipUnless(os.getenv("UNITY_CLI_PERF_TEST_BINARY"), "release binary integration opt-in")
    def test_normal_pass_and_30ms_transport_regression_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            for delay in [0, 30]:
                output = Path(directory) / f"{delay}.json"
                result = subprocess.run([sys.executable, str(ROOT / "scripts/bench-cli-latency.py"),
                                         "--unity-cli", os.environ["UNITY_CLI_PERF_TEST_BINARY"],
                                         "--delay-ms", str(delay), "--out", str(output)],
                                        capture_output=True, text=True, timeout=180)
                self.assertEqual(result.returncode, 1 if delay else 0, result.stdout + result.stderr)
                report = json.loads(output.read_text())
                self.assertEqual(len(report["results"]), 8)
                self.assertTrue(all(row["n"] >= 50 for row in report["results"].values()))
                if delay:
                    self.assertTrue(any("ping" in message for message in report["violations"]))


if __name__ == "__main__":
    unittest.main()
