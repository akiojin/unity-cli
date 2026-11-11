"""Validate retained batchmode compilation logs against the current Bridge C#."""
import gzip
import hashlib
import json
from pathlib import Path
import re


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BRIDGE = ROOT / "UnityCliBridge/Packages/unity-cli-bridge"


def main():
    report = json.loads((HERE / "compile.json").read_text(encoding="utf-8"))
    sources = {
        path.relative_to(BRIDGE).as_posix(): hashlib.sha256(
            path.read_bytes().replace(b"\r\n", b"\n")
        ).hexdigest()
        for path in sorted(BRIDGE.rglob("*.cs"))
    }
    assert sources and sources == report["source_sha256"], "Bridge source changed after compilation"
    assert report["bridge_version"] == json.loads(
        (BRIDGE / "package.json").read_text(encoding="utf-8")
    )["version"]
    rows = report["editors"]
    assert sorted(row["version"] for row in rows) == ["6000.3.28f1", "6000.6.8f1", "7000.0.0a16"]
    for row in rows:
        raw = gzip.decompress((HERE / row["log"]).read_bytes())
        assert hashlib.sha256(raw).hexdigest() == row["log_sha256"]
        text = raw.decode("utf-8")
        assert row["version"] in text and "-batchmode" in row["command"]
        assert "-quit" in row["command"]
        assert row["returncode"] == row["compiler_errors"] == 0 and row["status"] == "PASS"
        assert not re.search(r"\berror CS\d+:|Project has invalid dependencies:|Scripts have compiler errors", text)
        assert "Exiting batchmode successfully now!" in text
        print(f"PASS {row['version']}: batchmode exit 0, compiler errors 0, retained log hash matches")
    print(f"PASS {len(sources)} Bridge C# source hashes match the compiled snapshot")


if __name__ == "__main__":
    main()
