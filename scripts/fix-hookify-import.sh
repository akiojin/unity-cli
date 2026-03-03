#!/bin/bash
# Fix Claude Code hookify plugin import error ("No module named 'hookify'")

set -euo pipefail

CACHE_DIR="${HOME}/.claude/plugins/cache"
if [ ! -d "$CACHE_DIR" ]; then
    echo "ERROR: Claude plugin cache not found: $CACHE_DIR" >&2
    exit 1
fi

plugin_roots=()
while IFS= read -r -d '' file; do
    root_dir="$(cd "$(dirname "$file")/.." && pwd)"
    plugin_roots+=("$root_dir")
done < <(find "$CACHE_DIR" -type f -path "*/hookify/*/hooks/userpromptsubmit.py" -print0 2>/dev/null || true)

if [ ${#plugin_roots[@]} -eq 0 ]; then
    echo "ERROR: hookify plugin not found under: $CACHE_DIR" >&2
    exit 1
fi

# Deduplicate plugin roots
IFS=$'\n' plugin_roots=($(printf '%s\n' "${plugin_roots[@]}" | sort -u))
unset IFS

python3 - "${plugin_roots[@]}" <<'PY'
import pathlib
import re
import sys
from typing import Tuple

HOOK_FILES = ("userpromptsubmit.py", "pretooluse.py", "posttooluse.py", "stop.py")

PATCH_BLOCK = """# CRITICAL: Add plugin root to Python path for imports
PLUGIN_ROOT = os.environ.get('CLAUDE_PLUGIN_ROOT')
if not PLUGIN_ROOT:
    # Fallback: resolve plugin root from this file's location
    PLUGIN_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))

if PLUGIN_ROOT:
    if PLUGIN_ROOT not in sys.path:
        sys.path.insert(0, PLUGIN_ROOT)

    # Provide a "hookify" package root so absolute imports like `hookify.core.*` work,
    # even when the plugin is installed under a versioned directory.
    if 'hookify' not in sys.modules:
        hookify_pkg = types.ModuleType('hookify')
        hookify_pkg.__path__ = [PLUGIN_ROOT]
        sys.modules['hookify'] = hookify_pkg

"""

IMPORT_TYPES_RE = re.compile(r"(?m)^import types\\s*$")
IMPORT_JSON_RE = re.compile(r"(?m)^(import json\\s*\\n)")
HOOKIFY_ALIAS_RE = re.compile(r"types\\.ModuleType\\(['\\\"]hookify['\\\"]\\)")

BLOCK_RE = re.compile(
    r"(?ms)^# CRITICAL: Add plugin root to Python path for imports\\n.*?(?=^try:\\n)"
)


def patch_hook_file(path: pathlib.Path) -> Tuple[bool, str]:
    original = path.read_text(encoding="utf-8")

    if HOOKIFY_ALIAS_RE.search(original):
        return False, "already patched"

    text = original

    if not IMPORT_TYPES_RE.search(text):
        m = IMPORT_JSON_RE.search(text)
        if not m:
            return False, "could not locate import json block"
        text = text[: m.end()] + "import types\\n" + text[m.end() :]

    if BLOCK_RE.search(text):
        text = BLOCK_RE.sub(PATCH_BLOCK + "\\n", text, count=1)
    else:
        m = re.search(r"(?m)^try:\\s*$", text)
        if not m:
            return False, "could not locate try block"
        text = text[: m.start()] + PATCH_BLOCK + "\\n" + text[m.start() :]

    if text == original:
        return False, "no changes"

    path.write_text(text, encoding="utf-8")
    return True, "patched"


def main() -> int:
    roots = [pathlib.Path(p) for p in sys.argv[1:]]
    changed = 0
    skipped = 0
    failed = 0

    for root in roots:
        hooks_dir = root / "hooks"
        if not hooks_dir.is_dir():
            print(f"skip: hooks dir not found: {hooks_dir}", file=sys.stderr)
            skipped += 1
            continue

        for name in HOOK_FILES:
            path = hooks_dir / name
            if not path.is_file():
                continue

            try:
                did_change, reason = patch_hook_file(path)
                if did_change:
                    print(f"patched: {path}")
                    changed += 1
                else:
                    print(f"skip: {path} ({reason})")
                    skipped += 1
            except Exception as e:
                print(f"error: {path} ({e})", file=sys.stderr)
                failed += 1

    if failed:
        print(f"done: changed={changed} skipped={skipped} failed={failed}", file=sys.stderr)
        return 1

    print(f"done: changed={changed} skipped={skipped} failed={failed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
PY
