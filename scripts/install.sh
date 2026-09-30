#!/bin/sh
# Install unity-cli managed binary.
# Usage: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
#
# The download is verified against the release SHA256SUMS (or the release
# manifest for older releases) and the install aborts on any mismatch.
#
# Environment:
#   UNITY_CLI_VERSION           install a specific tag instead of the latest
#   UNITY_CLI_RELEASE_BASE_URL  release download base (default: GitHub Releases)
set -e

REPO="akiojin/unity-cli"
INSTALL_DIR="${HOME}/.unity/tools/unity-cli"
LINK_DIR="${HOME}/.local/bin"

# --- helpers ----------------------------------------------------------------

die() { echo "error: $*" >&2; exit 1; }

need() {
    command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

sha256_check() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f1
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | cut -d ' ' -f1
    else
        die "neither sha256sum nor shasum found"
    fi
}

# --- detect RID -------------------------------------------------------------

detect_rid() {
    os=$(uname -s)
    arch=$(uname -m)

    case "$os" in
        Darwin)
            case "$arch" in
                arm64|aarch64) echo "osx-arm64" ;;
                x86_64)        echo "osx-x64" ;;
                *)             die "unsupported macOS architecture: $arch" ;;
            esac
            ;;
        Linux)
            case "$arch" in
                aarch64|arm64) echo "linux-arm64" ;;
                x86_64)        echo "linux-x64" ;;
                *)             die "unsupported Linux architecture: $arch" ;;
            esac
            ;;
        *)
            die "unsupported OS: $os"
            ;;
    esac
}

# --- main -------------------------------------------------------------------

need curl

RID=$(detect_rid)
echo "Detected platform: ${RID}"

# 1. Resolve release tag (UNITY_CLI_VERSION pins a tag, e.g. v0.16.0)
if [ -n "${UNITY_CLI_VERSION:-}" ]; then
    TAG="$UNITY_CLI_VERSION"
    case "$TAG" in v*) ;; *) TAG="v${TAG}" ;; esac
else
    echo "Fetching latest release..."
    TAG=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" \
        | grep '"tag_name"' | head -1 | sed 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
fi

[ -z "$TAG" ] && die "failed to determine release tag"
echo "Release: ${TAG}"

# UNITY_CLI_RELEASE_BASE_URL overrides the download host (mirrors, tests).
BASE_URL="${UNITY_CLI_RELEASE_BASE_URL:-https://github.com/${REPO}/releases/download}"
ASSET_NAME="unity-cli-${RID}"

# 2. Resolve the expected SHA-256 and asset URL.
#    Prefer SHA256SUMS; fall back to the manifest for releases without it.
SUMS=$(curl -fsSL "${BASE_URL}/${TAG}/SHA256SUMS" 2>/dev/null) || SUMS=""
if [ -n "$SUMS" ]; then
    EXPECTED_SHA=$(printf '%s\n' "$SUMS" \
        | awk -v name="$ASSET_NAME" '{ f = $2; sub(/^\*/, "", f); if (f == name) { print tolower($1); exit } }')
    [ -z "$EXPECTED_SHA" ] && die "SHA256SUMS has no entry for ${ASSET_NAME}"
    ASSET_URL="${BASE_URL}/${TAG}/${ASSET_NAME}"
else
    MANIFEST=$(curl -fsSL "${BASE_URL}/${TAG}/unity-cli-manifest.json") \
        || die "failed to download SHA256SUMS or manifest"
    ASSET_URL=$(echo "$MANIFEST" | grep -A2 "\"${RID}\"" | grep '"url"' | head -1 \
        | sed 's/.*"url"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
    EXPECTED_SHA=$(echo "$MANIFEST" | grep -A2 "\"${RID}\"" | grep '"sha256"' | head -1 \
        | sed 's/.*"sha256"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/' | tr 'A-F' 'a-f')
    [ -z "$ASSET_URL" ] && die "manifest has no asset for RID: ${RID}"
    [ -z "$EXPECTED_SHA" ] && die "manifest has no sha256 for RID: ${RID}"
fi

# 3. Download binary
DEST_DIR="${INSTALL_DIR}/${RID}"
mkdir -p "$DEST_DIR"
TMP="${DEST_DIR}/unity-cli.download"

echo "Downloading ${ASSET_NAME}..."
curl -fsSL -o "$TMP" "$ASSET_URL" || { rm -f "$TMP"; die "download failed"; }

# 4. Verify SHA-256 before touching the installed binary
ACTUAL_SHA=$(sha256_check "$TMP")
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
    rm -f "$TMP"
    die "checksum mismatch for ${ASSET_NAME}: expected ${EXPECTED_SHA}, got ${ACTUAL_SHA}"
fi
echo "Checksum verified."

# 5. Install
BINARY="${DEST_DIR}/unity-cli"
mv -f "$TMP" "$BINARY"
chmod 755 "$BINARY"

# 6. Write VERSION
VERSION=$(echo "$TAG" | sed 's/^v//')
printf '%s\n' "$VERSION" > "${DEST_DIR}/VERSION"

# 7. Symlink
mkdir -p "$LINK_DIR"
ln -sf "$BINARY" "${LINK_DIR}/unity-cli"
echo "Installed unity-cli ${VERSION} -> ${LINK_DIR}/unity-cli"

# 8. Warn about cargo conflict
if [ -f "${HOME}/.cargo/bin/unity-cli" ]; then
    echo ""
    echo "WARNING: ${HOME}/.cargo/bin/unity-cli exists and may shadow the managed binary."
    echo "  Consider running: cargo uninstall unity-cli"
fi

# 9. PATH check
case ":${PATH}:" in
    *":${LINK_DIR}:"*) ;;
    *)
        echo ""
        echo "NOTE: ${LINK_DIR} is not in your PATH."
        echo "  Add the following to your shell profile:"
        echo "    export PATH=\"${LINK_DIR}:\$PATH\""
        ;;
esac

echo ""
echo "Done. Run 'unity-cli --version' to verify."
