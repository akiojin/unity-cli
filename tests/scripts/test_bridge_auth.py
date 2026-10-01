"""Credential selection for direct-wire E2E probes must not leak tokens."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("bridge_auth", ROOT / "scripts/bridge_auth.py")
auth = importlib.util.module_from_spec(spec)
spec.loader.exec_module(auth)


class BridgeAuthTests(unittest.TestCase):
    def test_only_live_matching_local_endpoint_supplies_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            lock = {"pid": os.getpid(), "port": 6540, "host": "127.0.0.1", "authToken": "private-token"}
            path = folder / "editor.json"
            path.write_text(json.dumps(lock))
            self.assertEqual(auth.auth_fields(6540, folder), {"authToken": "private-token"})
            self.assertEqual(auth.auth_fields(6541, folder), {})
            self.assertEqual(auth.auth_fields(6540, folder, pid=os.getpid() + 1), {})
            for replacement in ({"pid": 0}, {"host": "remote-host"}, {"authToken": ""}):
                path.write_text(json.dumps(dict(lock, **replacement)))
                self.assertEqual(auth.auth_fields(6540, folder), {})

    def test_missing_and_malformed_lockfiles_do_not_break_readiness_polling(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            self.assertEqual(auth.auth_fields(6540, folder), {})
            for content in ('{broken private-token', '[]', '{"port": "bad"}'):
                (folder / "editor.json").write_text(content)
                self.assertEqual(auth.auth_fields(6540, folder), {})

    def test_ambiguous_credentials_are_rejected_without_disclosing_tokens(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            for name in ("first", "second"):
                (folder / (name + ".json")).write_text(json.dumps({
                    "pid": os.getpid(), "port": 6540, "host": "127.0.0.1", "authToken": name + "-secret"}))
            with self.assertRaises(RuntimeError) as raised:
                auth.auth_fields(6540, folder)
            self.assertNotIn("secret", str(raised.exception))
