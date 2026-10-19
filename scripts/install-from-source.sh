#!/bin/sh
# Build this checkout and install it as the managed unity-cli binary, the same
# place scripts/install.sh puts a release, so unityd and your agents run your
# build. For contributors testing changes the way users run them.
# Usage: ./scripts/install-from-source.sh [--debug] [--skip-build] [--skills <client> | --no-skills]
#
# Environment:
#   UNITY_CLI_TOOLS_ROOT  managed tools root (default: ~/.unity/tools)
#
# Export UNITY_CLI_NO_AUTO_UPDATE=1 (in your shell profile) while you run a
# source build: otherwise the next release's self-update replaces it.
set -e

die() { echo "error: $*" >&2; exit 1; }

PROFILE=release
SKIP_BUILD=0
SKILLS=""
NO_SKILLS=0
while [ $# -gt 0 ]; do
    case "$1" in
        --debug)      PROFILE=debug; shift ;;
        --skip-build) SKIP_BUILD=1; shift ;;
        --no-skills)  NO_SKILLS=1; shift ;;
        --skills)     SKILLS="${2:-}"; [ -n "$SKILLS" ] || die "--skills needs a client"; shift 2 ;;
        -h|--help)    sed -n '2,11p' "$0"; exit 0 ;;
        *)            die "unknown option: $1" ;;
    esac
done

REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
BUILT="${CARGO_TARGET_DIR:-${REPO_ROOT}/target}/${PROFILE}/unity-cli"

case "$(uname -s)/$(uname -m)" in
    Darwin/arm64|Darwin/aarch64) RID=osx-arm64 ;;
    Darwin/x86_64)               RID=osx-x64 ;;
    Linux/aarch64|Linux/arm64)   RID=linux-arm64 ;;
    Linux/x86_64)                RID=linux-x64 ;;
    *) die "unsupported platform $(uname -s)/$(uname -m) (on Windows use scripts/install-from-source.ps1)" ;;
esac

# 1. Build
if [ "$SKIP_BUILD" = 0 ]; then
    command -v cargo >/dev/null 2>&1 || die "cargo not found (install Rust from https://rustup.rs)"
    if [ "$PROFILE" = release ]; then
        (cd "$REPO_ROOT" && cargo build --bin unity-cli --locked --release)
    else
        (cd "$REPO_ROOT" && cargo build --bin unity-cli --locked)
    fi
fi
[ -x "$BUILT" ] || die "no build at ${BUILT} (drop --skip-build)"

VERSION=$("$BUILT" --version | awk '{print $2}')
[ -n "$VERSION" ] || die "could not read the build's version"
COMMIT=$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo unknown)

# 2. Stop the daemon running the old binary
DEST_DIR="${UNITY_CLI_TOOLS_ROOT:-${HOME}/.unity/tools}/unity-cli/${RID}"
BINARY="${DEST_DIR}/unity-cli"
mkdir -p "$DEST_DIR"
if [ -x "$BINARY" ]; then
    UNITY_CLI_NO_AUTO_UPDATE=1 "$BINARY" unityd stop >/dev/null 2>&1 || true
fi

# 3. Install and write VERSION. VERSION holds the build's own version, so
#    self-update only replaces the build once a newer release exists.
TMP="${DEST_DIR}/unity-cli.download"
cp "$BUILT" "$TMP"
chmod 755 "$TMP"
mv -f "$TMP" "$BINARY"
printf '%s\n' "$VERSION" > "${DEST_DIR}/VERSION"

# 4. Symlink (same as install.sh)
LINK_DIR="${HOME}/.local/bin"
mkdir -p "$LINK_DIR"
ln -sf "$BINARY" "${LINK_DIR}/unity-cli"
echo "Installed unity-cli ${VERSION} (${PROFILE} build of ${COMMIT}) -> ${LINK_DIR}/unity-cli"

# 5. Skills: refresh installed copies to this build, or install for a client
if [ "$NO_SKILLS" = 1 ]; then
    : # leave installed skills alone (tests, or a binary-only change)
elif [ -n "$SKILLS" ]; then
    "$BINARY" skills install "$SKILLS" --force
elif "$BINARY" skills refresh >/dev/null 2>&1; then
    echo "Refreshed installed skills to this build."
fi

if [ -f "${HOME}/.cargo/bin/unity-cli" ]; then
    echo ""
    echo "WARNING: ${HOME}/.cargo/bin/unity-cli exists and may shadow the managed binary."
    echo "  Consider running: cargo uninstall unity-cli"
fi
if [ "${UNITY_CLI_NO_AUTO_UPDATE:-}" != 1 ]; then
    echo ""
    echo "WARNING: self-update will replace this build when a newer release ships."
    echo "  To keep it, add to your shell profile: export UNITY_CLI_NO_AUTO_UPDATE=1"
fi
