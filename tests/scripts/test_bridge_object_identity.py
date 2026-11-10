"""Keep legacy Unity identity APIs inside the compatibility helper (Issue #546).

Run the focused guard used by the required CI lint job:
python -m unittest discover -s tests/scripts -p test_bridge_object_identity.py -v
"""

import os
from pathlib import Path
import re
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
HELPER = Path("Packages/unity-cli-bridge/Editor/Helpers/ObjectIdentity.cs")
LEGACY_CALL = re.compile(r"\b(GetInstanceID|InstanceIDToObject)\s*\(")


def find_legacy_calls(root):
    """Report source locations, including calls in inactive preprocessor branches."""
    violations = []
    for directory, children, files in os.walk(root):
        if Path(directory) == root:
            # Unity's generated project caches are not maintained Bridge source.
            children[:] = [name for name in children if name not in {
                "Library", "Temp", "Obj", "Logs", "UserSettings",
            }]
        for name in sorted(files):
            path = Path(directory) / name
            if path.suffix != ".cs" or path.relative_to(root) == HELPER:
                continue
            source = path.read_text(encoding="utf-8-sig")
            for match in LEGACY_CALL.finditer(source):
                line = source.count("\n", 0, match.start()) + 1
                violations.append(
                    f"{path.relative_to(root).as_posix()}:{line}: {match.group(1)}"
                )
    return sorted(violations)


class BridgeObjectIdentityTests(unittest.TestCase):
    def test_bridge_sources_use_the_compatibility_helper(self):
        self.assertTrue((ROOT / "UnityCliBridge" / HELPER).is_file())
        violations = find_legacy_calls(ROOT / "UnityCliBridge")
        self.assertEqual([], violations, "\n".join([
            "Use ObjectIdentity.GetInstanceId / ObjectIdentity.FindObject:",
            *violations,
        ]))

    def test_reintroduced_calls_fail_in_editor_tests_and_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in (
                "Packages/unity-cli-bridge/Editor/Handlers/Regression.cs",
                "Packages/unity-cli-bridge/Tests/Editor/Regression.cs",
                "Assets/Regression.cs",
            ):
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    "// regression\nobj.GetInstanceID ();\n"
                    "EditorUtility.InstanceIDToObject\n (42);\n",
                    encoding="utf-8",
                )
            violations = find_legacy_calls(root)
            self.assertEqual(6, len(violations))
            for relative in (
                "Packages/unity-cli-bridge/Editor/Handlers/Regression.cs",
                "Packages/unity-cli-bridge/Tests/Editor/Regression.cs",
                "Assets/Regression.cs",
            ):
                self.assertIn(f"{relative}:2: GetInstanceID", violations)
                self.assertIn(f"{relative}:3: InstanceIDToObject", violations)

    def test_inactive_legacy_branches_are_still_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Regression.cs").write_text(
                "#if !UNITY_6000_5_OR_NEWER\nobj.GetInstanceID();\n#endif\n",
                encoding="utf-8-sig",
            )
            self.assertEqual(
                ["Regression.cs:2: GetInstanceID"], find_legacy_calls(root),
            )

    def test_only_the_canonical_helper_is_exempt(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in (HELPER, Path("Assets/ObjectIdentity.cs")):
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("obj.GetInstanceID();", encoding="utf-8")
            self.assertEqual(
                ["Assets/ObjectIdentity.cs:1: GetInstanceID"],
                find_legacy_calls(root),
            )

    def test_compatible_calls_and_generated_sources_are_allowed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Assets").mkdir()
            (root / "Assets/Valid.cs").write_text(
                "ObjectIdentity.GetInstanceId(obj);\nObjectIdentity.FindObject(42);",
                encoding="utf-8",
            )
            (root / "Library").mkdir()
            (root / "Library/Generated.cs").write_text(
                "obj.GetInstanceID();", encoding="utf-8",
            )
            self.assertEqual([], find_legacy_calls(root))


if __name__ == "__main__":
    unittest.main()
