"""Check acquisition safety boundaries and typed/raw CLI wiring without Unity."""
import json
import os
import re
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[3]
HANDLERS = ROOT / "UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers"


def main():
    source = (HANDLERS / "UnityPackageHandler.cs").read_text(encoding="utf-8")
    for forbidden in (
        "UnityEditor.PackageManager.UI.Internal", "System.Reflection", "BindingFlags",
        "SendEvent(", "SendMessage(", "UnityConnect", "OAuth", "access_token",
        "GetDownloadDetail", "HttpClient", "UnityWebRequest",
    ):
        assert forbidden not in source, forbidden
    assert not re.search(r"\bDownload\s*\(", source), "Download API invocation"
    assert 'ExecuteMenuItem("Window/Package Management/My Assets")' in source
    assert "UnityEditor.PackageManager.UI.Window.Open(" in source
    print("PASS: public display APIs; no download, reflection, synthetic input or account access")

    binary = ROOT / "target/debug" / ("unity-cli.exe" if os.name == "nt" else "unity-cli")
    env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1")
    cases = [
        (["asset-store-open"], "open_asset_store", None),
        (["asset-store-open", "12345"], "open_asset_store", "12345"),
        (["asset-store-wait", "Small", "--download-timeout-ms", "1234"], "wait_download", "Small"),
        (["asset-store-import", "12345", "--cache-path", "C:/cache"], "acquire", "12345"),
    ]
    for words, action, selector in cases:
        result = subprocess.run([str(binary), "--dry-run", "--output", "json", "package"] + words,
                                env=env, text=True, capture_output=True, check=True, timeout=30)
        data = json.loads(result.stdout)["data"]
        assert data["executed"] is False, data
        assert data["params"]["action"] == action, data
        assert data["params"].get("nameOrId") == selector, data
        if action == "wait_download":
            assert data["params"]["downloadTimeoutMs"] == 1234, data
        if action == "acquire":
            assert data["params"]["cachePath"] == "C:/cache", data
        print("PASS: typed CLI dry-run", " ".join(words))
    for action in ("open_asset_store", "wait_download", "acquire"):
        payload = {"action": action}
        if action != "open_asset_store":
            payload["nameOrId"] = "12345"
        result = subprocess.run([str(binary), "--dry-run", "--output", "json", "raw",
                                 "package_manager", "--json", json.dumps(payload)],
                                env=env, text=True, capture_output=True, check=True, timeout=30)
        assert json.loads(result.stdout)["data"]["params"]["action"] == action
        print("PASS: raw schema", action)


if __name__ == "__main__":
    main()
