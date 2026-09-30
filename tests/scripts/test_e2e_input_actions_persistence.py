"""Real socket regression for restarting an owned Editor on the same port."""
import importlib.util
import os
from pathlib import Path
import socket
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("persistence", ROOT / "scripts/e2e-input-actions-persistence.py")
persistence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(persistence)


class RestartPortTests(unittest.TestCase):
    def test_live_listener_is_never_considered_available(self):
        with socket.socket() as listener:
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listener.bind(("127.0.0.1", 0))
            listener.listen(1)
            with self.assertRaises(OSError):
                persistence.check_port_available(listener.getsockname()[1])

    @unittest.skipIf(os.name == "nt", "Unix TIME_WAIT rebinding contract")
    def test_closed_connection_does_not_block_same_port_restart(self):
        with socket.socket() as listener:
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
            listener.listen(1)
            with socket.create_connection(("127.0.0.1", port), timeout=2) as client:
                connection, _ = listener.accept()
                # Server actively closes: its port remains in TIME_WAIT after shutdown.
                connection.close()
                self.assertEqual(client.recv(1), b"")
        persistence.check_port_available(port)


if __name__ == "__main__":
    unittest.main()
