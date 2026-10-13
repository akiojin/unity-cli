import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
READMES = ['README.md', 'README.ja.md', 'README.zh.md', 'README.de.md',
           'README.es.md', 'README.fr.md', 'README.it.md']

PROMPT_STEPS = [
    'scripts/install.sh',
    'scripts/install.ps1',
    'PATH',
    'unity-cli --output json setup --launch-editor',
    'unity-cli system ping',
    'unity-cli doctor --output json',
    'Packages/manifest.json',
]

MANUAL_CONTENT = [
    'scripts/install.sh | sh',
    'scripts/install.ps1 | iex',
    'SHA256SUMS',
    'UNITY_CLI_VERSION',
    'UNITY_CLI_NO_AUTO_UPDATE',
    'brew install akiojin/tap/unity-cli',
    'winget install akiojin.unity-cli',
    'ORT_DYLIB_PATH',
    'releases',
    'cargo install --path .',
    'unity-cli --output json setup --launch-editor',
    'unity-cli bridge install | upgrade | status',
    'activeInputHandler',
    'openupm add com.akiojin.unity-cli-bridge',
    '?path=UnityCliBridge/Packages/unity-cli-bridge',
    'unity-cli system ping',
    'unity-cli doctor --output json',
    'unity-cli cli doctor',
    'unity-cli cli install',
    'UNITY_CLI_TOOLS_ROOT',
]


def getting_started(text):
    """Return the `##` section that holds the Editor compatibility note."""
    sections = re.split(r'^(?=## )', text, flags=re.MULTILINE)
    matches = [s for s in sections if 'docs/editor-compatibility.md' in s]
    assert len(matches) == 1, 'Getting Started section not found'
    return matches[0]


def subsections(section):
    parts = re.split(r'^(?=### )', section, flags=re.MULTILINE)[1:]
    return [(part.splitlines()[0], part) for part in parts]


class ReadmeInstallStructureTest(unittest.TestCase):
    def test_getting_started_has_compat_llm_and_manual_paths(self):
        for name in READMES:
            with self.subTest(readme=name):
                subs = subsections(getting_started((ROOT / name).read_text()))
                self.assertEqual(len(subs), 3, [title for title, _ in subs])
                self.assertIn('docs/editor-compatibility.md', subs[0][1])

    def test_llm_section_has_single_prompt_with_all_steps(self):
        for name in READMES:
            with self.subTest(readme=name):
                llm = subsections(getting_started((ROOT / name).read_text()))[1][1]
                prompts = re.findall(r'^```text\n(.*?)^```', llm,
                                     flags=re.MULTILINE | re.DOTALL)
                self.assertEqual(len(prompts), 1)
                prompt = prompts[0]
                positions = [prompt.find(step) for step in PROMPT_STEPS]
                for step, pos in zip(PROMPT_STEPS, positions):
                    self.assertGreaterEqual(pos, 0, step)
                ordered = [positions[0], positions[3], positions[4], positions[5]]
                self.assertEqual(ordered, sorted(ordered))
                # manifest.json confirmation must come before setup runs
                self.assertLess(positions[6], positions[3])
                self.assertIn('/plugin marketplace add akiojin/unity-cli', llm)
                self.assertIn('.agents/skills/', llm)

    def test_manual_section_keeps_every_install_path(self):
        for name in READMES:
            with self.subTest(readme=name):
                manual = subsections(getting_started((ROOT / name).read_text()))[2][1]
                for needle in MANUAL_CONTENT:
                    self.assertIn(needle, manual, needle)


if __name__ == '__main__':
    unittest.main()
