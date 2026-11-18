"""README catalog facts for Issue #571.

The bundled skill inventory, tool counts, install path, configuration, and
translation sync are checked against the plugin directory, docs/skills.md,
docs/tools.md, and Cargo.toml. Acceptance is the source of truth named by
the issue: the plugin directories and docs/skills.md, whose count is 23.
"""

import re
import unicodedata
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
READMES = [
    'README.md',
    'README.ja.md',
    'README.zh.md',
    'README.de.md',
    'README.es.md',
    'README.fr.md',
    'README.it.md',
]
SKILL_ROOT = ROOT / '.claude-plugin' / 'plugins' / 'unity-cli' / 'skills'
DOC_LINKS = [
    'docs/skills.md',
    'docs/configuration.md',
    'docs/architecture.md',
    'docs/hot-reload.md',
    'docs/migration-notes.md',
]
SYNC_MARKERS = [
    'docs/tools.md#v013v015-executable-examples',
    'unity-cli package asset-store-import',
    'UNITY_CLI_ASSET_STORE_CACHE_PATH',
    'unity-cli tool schema create_scene',
    'unity-cli --dry-run tool call create_scene',
    'gwt-spec',
    '6000.3.28f1',
    '6000.6.8f1',
    '7000.0.0a16',
    'UNITY_CLI_UNITYD_PORT',
    'UNITY_CLI_LSPD_PORT',
    'UNITY_CLI_AUTH_TOKEN_FILE',
    'UNITY_CLI_ALLOW_UNAUTHENTICATED',
]
CONFIG_MARKERS = [
    'UNITY_CLI_AUTH_TOKEN_FILE',
    'UNITY_CLI_ALLOW_UNAUTHENTICATED',
    'docs/configuration.md',
    'UNITY_CLI_UNITYD_PORT',
    'UNITY_CLI_LSPD_PORT',
    'UNITY_CLI_NO_AUTO_UPDATE',
]


def readme_text(name):
    return (ROOT / name).read_text(encoding='utf-8')


def h2_sections(text):
    parts = re.split(r'^(?=## )', text, flags=re.MULTILINE)
    return [part for part in parts if part.startswith('## ')]


def section_matching(text, predicate):
    matches = [part for part in h2_sections(text) if predicate(part)]
    return matches


def bundled_skills():
    return sorted(
        path.name
        for path in SKILL_ROOT.iterdir()
        if path.is_dir() and path.name.startswith('unity-')
    )


def cargo_version():
    match = re.search(r'^version = "([^"]+)"', (ROOT / 'Cargo.toml').read_text(encoding='utf-8'), re.MULTILINE)
    if match is None:
        raise AssertionError('Cargo.toml version not found')
    return match.group(1)


def github_slug(heading):
    text = heading.strip().lower()
    text = re.sub(r'`([^`]*)`', r'\1', text)
    chars = []
    for char in text:
        if char.isspace():
            chars.append('-')
        elif char in '-_':
            chars.append(char)
        elif unicodedata.category(char)[0] in ('L', 'N'):
            chars.append(char)
    slug = ''.join(chars).strip('-')
    slug = re.sub(r'-{2,}', '-', slug)
    return slug


def heading_slugs(text):
    counts = {}
    slugs = set()
    for line in text.splitlines():
        match = re.match(r'^#{1,6}\s+(.+?)\s*#*\s*$', line)
        if match is None:
            continue
        base = github_slug(match.group(1))
        seen = counts.get(base, 0)
        counts[base] = seen + 1
        slugs.add(base if seen == 0 else f'{base}-{seen}')
    return slugs


class ReadmeCatalogTest(unittest.TestCase):
    def test_skill_inventory_matches_plugin_and_skills_doc(self):
        skills = bundled_skills()
        self.assertGreaterEqual(len(skills), 1)
        document = (ROOT / 'docs' / 'skills.md').read_text(encoding='utf-8')
        inventory = document.split('### Skill inventory', 1)[1].split('\n### ', 1)[0]
        documented = re.findall(r'`(unity-[a-z0-9-]+)`', inventory)
        self.assertEqual(sorted(set(documented)), skills)

    def test_every_readme_lists_bundled_skills_and_marks_dev_only_helper(self):
        skills = bundled_skills()
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                matches = section_matching(text, lambda part: re.match(r'^## .+\(\d+\)\s*$', part.splitlines()[0]))
                self.assertEqual(len(matches), 1, [part.splitlines()[0] for part in matches])
                section = matches[0]
                heading = section.splitlines()[0]
                self.assertIn(f'({len(skills)})', heading)
                for skill in skills:
                    self.assertIn(f'`{skill}`', section, skill)
                self.assertIn('gh-skills-sync', section)
                self.assertIn('dev-skills/', section)
                self.assertNotIn('| Maintenance |', section)
                self.assertNotIn('| メンテナンス |', section)

    def test_tool_counts_follow_tools_doc(self):
        tools = (ROOT / 'docs' / 'tools.md').read_text(encoding='utf-8')
        self.assertIn('## Runtime Tool APIs (143 tools)', tools)
        self.assertIn('## Reference Cache (11 tools)', tools)
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                self.assertNotIn('`101`', text)
                self.assertRegex(text, r'143.{0,80}11|11.{0,80}143')

    def test_version_pin_matches_cargo_toml(self):
        version = cargo_version()
        pin = f'UNITY_CLI_VERSION=v{version}'
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                self.assertIn(pin, text)
                self.assertNotIn('UNITY_CLI_VERSION=v0.16.0', text)

    def test_crates_io_install_is_documented_beside_path_install(self):
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                self.assertIn('cargo install unity-cli', text)
                self.assertIn('cargo install --path .', text)

    def test_configuration_table_has_auth_and_windows_daemon_settings(self):
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                matches = section_matching(
                    text,
                    lambda part: '`UNITY_CLI_PORT`' in part and '`UNITY_PROJECT_ROOT`' in part,
                )
                self.assertEqual(len(matches), 1)
                section = matches[0]
                for marker in CONFIG_MARKERS:
                    self.assertIn(marker, section, marker)

    def test_documentation_section_links_the_missing_guides(self):
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                matches = section_matching(
                    text,
                    lambda part: 'docs/development.md' in part and 'RELEASE.md' in part,
                )
                self.assertEqual(len(matches), 1)
                section = matches[0]
                for link in DOC_LINKS:
                    self.assertIn(f']({link})', section, link)

    def test_translations_include_the_english_sections_that_were_missing(self):
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                for marker in SYNC_MARKERS:
                    self.assertIn(marker, text, marker)
                contributing = section_matching(
                    text,
                    lambda part: (
                        'CONTRIBUTING.md' in part
                        and re.search(r'(?<![\w-])develop(?![\w-])', part) is not None
                        and 'RELEASE.md' not in part
                    ),
                )
                self.assertEqual(len(contributing), 1, [part.splitlines()[0] for part in contributing])

    def test_relative_readme_links_resolve(self):
        link_re = re.compile(r'\[[^]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)')
        for name in READMES:
            with self.subTest(readme=name):
                text = readme_text(name)
                for target in link_re.findall(text):
                    if target.startswith(('http://', 'https://', 'mailto:')):
                        continue
                    path_part, _, anchor = target.partition('#')
                    if path_part == '':
                        document = text
                        location = ROOT / name
                    else:
                        location = (ROOT / path_part).resolve()
                        self.assertTrue(location.is_file(), target)
                        document = location.read_text(encoding='utf-8')
                    if anchor:
                        self.assertIn(anchor, heading_slugs(document), target)


if __name__ == '__main__':
    unittest.main()
