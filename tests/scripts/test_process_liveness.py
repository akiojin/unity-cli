"""Editor authentication and lifecycle probes must only observe processes."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import bridge_auth

spec = importlib.util.spec_from_file_location(
    "editor_lifecycle", ROOT / "scripts/e2e-editor-lifecycle.py")
lifecycle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lifecycle)


class ProcessLivenessTests(unittest.TestCase):
    def child(self, code):
        process = subprocess.Popen(
            [sys.executable, "-c", code], stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, text=True)

        def cleanup():
            if process.poll() is None:
                process.terminate()
            process.communicate(timeout=10)

        self.addCleanup(cleanup)
        return process

    def probe(self, kind, pid):
        if kind == "lifecycle":
            return lifecycle.alive(pid)
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "editor.json").write_text(json.dumps({
                "pid": pid, "host": "127.0.0.1", "port": 6540,
                "authToken": "fixture-token"}))
            return bool(bridge_auth.auth_fields(6540, directory))

    def test_live_process_survives_authentication_and_lifecycle_probes(self):
        for kind in ("auth", "lifecycle"):
            with self.subTest(kind=kind):
                process = self.child(
                    'import time; print("ready", flush=True); time.sleep(60)')
                self.assertEqual(process.stdout.readline().strip(), "ready")
                self.assertTrue(self.probe(kind, process.pid))
                try:
                    process.wait(timeout=0.2)
                except subprocess.TimeoutExpired:
                    pass
                else:
                    self.fail(f"{kind} liveness probe terminated its live child")

    def test_exited_process_is_not_alive_or_eligible_for_authentication(self):
        process = self.child("pass")
        process.wait(timeout=10)
        for kind in ("auth", "lifecycle"):
            with self.subTest(kind=kind):
                self.assertFalse(self.probe(kind, process.pid))

    def test_invalid_process_ids_are_not_alive_or_eligible_for_authentication(self):
        for kind in ("auth", "lifecycle"):
            for pid in (0, -1, 0xFFFFFFFF, 0x100000000):
                with self.subTest(kind=kind, pid=pid):
                    self.assertFalse(self.probe(kind, pid))

    @unittest.skipUnless(os.name == "nt", "Windows process exit code contract")
    def test_exit_code_still_active_does_not_mean_process_is_running(self):
        process = self.child("import sys; sys.exit(259)")
        self.assertEqual(process.wait(timeout=10), 259)
        for kind in ("auth", "lifecycle"):
            with self.subTest(kind=kind):
                self.assertFalse(self.probe(kind, process.pid))


if __name__ == "__main__":
    unittest.main()
