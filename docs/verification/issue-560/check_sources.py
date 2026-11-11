"""Recheck public Unity reference-source evidence; never call Asset Store APIs.

Use --record only to intentionally refresh the committed evidence manifest.
Unity reference source is read in memory, not copied into this repository.
"""

import argparse
import hashlib
import json
from pathlib import Path
import urllib.request


VERSIONS = {
    "6000.3.28f1": "efa8fffa1235e83ebc71a52b49887135e2aa5602",
    "6000.6.8f1": "b66d07c652a67a46f8ca9a24ea4143940b106c84",
    "7000.0.0a16": "cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b",
}
ASSET_STORE = "Modules/PackageManagerUI/Editor/Services/AssetStore/"
PROXIES = "Modules/PackageManagerUI/Editor/Services/Proxies/"
WINDOW = "Modules/PackageManagerUI/Editor/UI/MainWindow/PackageManagerWindow.cs"
COMMON = {
    ASSET_STORE + "AssetStoreDownloadManager.cs": {
        "internal_manager": "internal class AssetStoreDownloadManager",
        "entry_point": "public bool Download(IEnumerable<long> productIds)",
        "terms_check": "m_AssetStoreRestAPI.CheckTermsAndConditions(",
    },
    ASSET_STORE + "AssetStoreDownloadOperation.cs": {
        "internal_operation": "internal class AssetStoreDownloadOperation",
        "download_metadata": "m_AssetStoreRestAPI.GetDownloadDetail(",
        "native_download": "m_AssetStoreUtils.Download(",
    },
    ASSET_STORE + "AssetStoreRestAPI.cs": {
        "internal_rest_client": "internal class AssetStoreRestAPI",
        "metadata_endpoint": '"/-/api/legacy-package-download-info"',
        "terms_endpoint": '"/-/api/terms/check"',
        "configured_host": "CloudConfigUrl.CloudPackagesApi",
    },
    ASSET_STORE + "AssetStoreDownloadInfo.cs": {
        "download_url": "public string url;",
        "download_key": "public string key;",
        "upload_version": "public long uploadId;",
    },
    ASSET_STORE + "AssetStoreUtils.cs": {
        "internal_wrapper": "internal class AssetStoreUtils",
        "native_forwarding": "UnityAssetStoreUtils.Download(",
    },
    "Editor/Mono/AssetStoreUtils.bindings.cs": {
        "internal_native_binding": "internal class AssetStoreUtils",
        "native_download": "extern public static void Download(",
    },
    WINDOW: {
        "public_window_helper": "public static class Window",
        "public_open": "public static void Open(string packageToSelect)",
        "selection_only": "PackageManagerWindow.OpenAndSelectPackage(packageToSelect);",
    },
}
LEGACY_OAUTH = {
    "internal_oauth": "internal class AssetStoreOAuth",
    "authorization_code": "m_UnityOAuth.GetAuthorizationCodeAsync(",
    "token_endpoint": '"/v1/oauth2/token"',
    "refresh_grant": "grant_type=refresh_token",
    "expiry_buffer": "k_ExpirationBufferTime = 15L",
    "editor_configuration": "CloudConfigUrl.CloudPackagesKey",
    "clear_on_login_change": "private void OnUserLoginStateChange(",
}
UNITY_CONNECT = {
    "internal_proxy": "internal class UnityConnectProxy",
    "editor_token": "m_AccessToken = UnityConnect.instance.userInfo.accessToken;",
    "callback_api": "public void GetAccessToken(Action<string> doneCallback",
    "no_token_error": "UIErrorCode.UserNotSignedIn",
}


def inspect(version, commit):
    checks = {path: dict(markers) for path, markers in COMMON.items()}
    if version == "6000.3.28f1":
        checks[WINDOW.replace("MainWindow/", "")] = checks.pop(WINDOW)
    rest = checks[ASSET_STORE + "AssetStoreRestAPI.cs"]
    if version.startswith("6000."):
        checks[ASSET_STORE + "AssetStoreOAuth.cs"] = LEGACY_OAUTH
        rest["token_provider"] = "m_AssetStoreOAuth.FetchAccessToken("
        rest["bearer"] = '\"Bearer \" + token.accessToken'
    else:
        checks[PROXIES + "UnityConnectProxy.cs"] = UNITY_CONNECT
        rest["token_provider"] = "m_UnityConnect.GetAccessToken("
        rest["bearer"] = '\"Bearer \" + token;'

    files = []
    for path, markers in checks.items():
        url = f"https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/{commit}/{path}"
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read()
        lines = data.decode("utf-8-sig").splitlines()
        locations = {}
        for name, marker in markers.items():
            matches = [i for i, line in enumerate(lines, 1) if marker in line]
            if not matches:
                raise ValueError(f"{version}/{path}: missing {name}")
            locations[name] = matches[0]
        files.append({"path": path, "sha256": hashlib.sha256(data).hexdigest(),
                      "claim_lines": locations})
    count = sum(len(file["claim_lines"]) for file in files)
    print(f"PASS {version}: {len(files)} pinned files, {count} source observations")
    return {"version": version, "commit": commit, "files": files}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--record", action="store_true")
    args = parser.parse_args()
    manifest = Path(__file__).with_name("source-evidence.json")
    evidence = {"repository": "https://github.com/Unity-Technologies/UnityCsReference",
                "versions": [inspect(v, c) for v, c in VERSIONS.items()]}
    if args.record:
        manifest.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print(f"Recorded {manifest.name}")
    else:
        expected = json.loads(manifest.read_text(encoding="utf-8"))
        if evidence != expected:
            raise ValueError("Pinned source hashes/observations differ from source-evidence.json")
        print("PASS committed evidence matches all 24 upstream files")


if __name__ == "__main__":
    main()
