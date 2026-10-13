#!/bin/sh
# Installs a locally built unity-cli through scripts/install-from-source.sh
# (--skip-build) into a temporary HOME and UNITY_CLI_TOOLS_ROOT and checks the
# managed layout: fresh install, symlink, reinstall, a missing build, and the
# auto-update warning.
# Usage: tests/scripts/install-from-source-sh-test.sh target/debug/unity-cli
set -eu

BUILT=${1:?usage: $0 <built unity-cli binary>}
REPO_ROOT=$(cd "$(dirname "$0")/../.." && pwd)
INSTALLER="${REPO_ROOT}/scripts/install-from-source.sh"

fail() { echo "FAIL: $*" >&2; exit 1; }

case "$(uname -s)/$(uname -m)" in
    Darwin/arm64|Darwin/aarch64) RID=osx-arm64 ;;
    Darwin/x86_64)               RID=osx-x64 ;;
    Linux/aarch64|Linux/arm64)   RID=linux-arm64 ;;
    Linux/x86_64)                RID=linux-x64 ;;
    *) fail "unsupported platform $(uname -s)/$(uname -m)" ;;
esac

BUILT_VERSION=$("$BUILT" --version)
VERSION=$(echo "$BUILT_VERSION" | awk '{print $2}')
hash_of() { cksum < "$1"; }
BUILT_HASH=$(hash_of "$BUILT")

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/home" "$WORK/target/debug" "$WORK/empty-target"
cp "$BUILT" "$WORK/target/debug/unity-cli"

export HOME="$WORK/home"
export UNITY_CLI_TOOLS_ROOT="$WORK/tools"
export CARGO_TARGET_DIR="$WORK/target"
export UNITY_CLI_NO_AUTO_UPDATE=1
DEST="$WORK/tools/unity-cli/$RID"
INSTALLED="$DEST/unity-cli"
OUT="$WORK/out"

run() { sh "$INSTALLER" --debug --skip-build --no-skills >"$OUT" 2>&1; }

# Fresh install
run || fail "fresh install failed: $(cat "$OUT")"
[ -x "$INSTALLED" ] || fail "binary must be installed into the managed layout"
[ "$(hash_of "$INSTALLED")" = "$BUILT_HASH" ] || fail "installed binary must match the build"
[ "$(cat "$DEST/VERSION")" = "$VERSION" ] || fail "VERSION must hold the build version"
[ "$("$INSTALLED" --version)" = "$BUILT_VERSION" ] || fail "installed binary must run"
[ "$(readlink "$HOME/.local/bin/unity-cli")" = "$INSTALLED" ] || fail "~/.local/bin/unity-cli must point at the managed binary"
! grep -q "self-update will replace" "$OUT" || fail "no auto-update warning when UNITY_CLI_NO_AUTO_UPDATE=1"

# Reinstall replaces an existing binary
printf 'stale\n' > "$INSTALLED"
run || fail "reinstall failed: $(cat "$OUT")"
[ "$(hash_of "$INSTALLED")" = "$BUILT_HASH" ] || fail "reinstall must replace the binary"

# A missing build fails and leaves the install alone
CARGO_TARGET_DIR="$WORK/empty-target"
if run; then fail "a missing build must fail"; fi
grep -q "no build at" "$OUT" || fail "missing build must say so: $(cat "$OUT")"
[ "$(hash_of "$INSTALLED")" = "$BUILT_HASH" ] || fail "a failed run must leave the install alone"
CARGO_TARGET_DIR="$WORK/target"

# Auto-update warning when the opt-out is not set
unset UNITY_CLI_NO_AUTO_UPDATE
run || fail "install without the opt-out failed: $(cat "$OUT")"
grep -q "self-update will replace" "$OUT" || fail "must warn when UNITY_CLI_NO_AUTO_UPDATE is unset"

echo "install-from-source.sh test passed (${BUILT_VERSION})"
