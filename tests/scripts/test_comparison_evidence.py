"""Acceptance checks for the published #371 measurements (no live Editor needed)."""
import json
import math
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
DATE = '2026-10-02'


class ComparisonEvidenceTests(unittest.TestCase):
    def reports(self):
        paths = [ROOT / f'docs/benchmarks/official-comparison-{DATE}-{mode}-{focus}.json'
                 for mode in ('process', 'resident') for focus in ('frontmost', 'background')]
        for path in paths:
            self.assertTrue(path.is_file(), f'Missing published measurement: {path.name}')
        return [json.loads(path.read_text()) for path in paths]

    def test_four_runs_have_complete_raw_samples_and_correct_percentiles(self):
        for report in self.reports():
            conditions = report['conditions']
            with self.subTest(mode=conditions['mode'], focus=conditions['focus']):
                self.assertEqual(report['status'], 'PASS')
                self.assertEqual(conditions['iterations'], 100)
                self.assertEqual(conditions['warmup'], 3)
                self.assertEqual(set(report['results']), {'unity-cli', 'official'})
                names = set(report['results']['unity-cli'])
                self.assertEqual(names, set(report['results']['official']))
                self.assertEqual(len(names), 3 if conditions['mode'] == 'process' else 23)
                for tool, results in report['results'].items():
                    for name, summary in results.items():
                        values = report['samples_ms'][tool][name]
                        self.assertEqual(len(values), 100)
                        self.assertEqual(summary['n'], 100)
                        self.assertTrue(all(math.isfinite(value) and value > 0 for value in values))
                        self.assertEqual(summary['p50_ms'], round(sorted(values)[49], 2))
                        self.assertEqual(summary['p95_ms'], round(sorted(values)[94], 2))

    def test_identical_versions_and_script_and_quiet_host_are_recorded(self):
        reports = self.reports()
        for report in reports:
            c = report['conditions']
            # Preserve historical results when a future benchmark revision lands.
            # Publication additionally compares this hash to the script being run.
            self.assertRegex(c['benchmark_sha256'], r'^[0-9a-f]{64}$')
            self.assertEqual(c['unity_editor_version'], '6000.3.25f1')
            self.assertEqual(c['bridge_version'], '0.17.0')
            self.assertEqual(c['official_cli_version'], '1.0.0-beta.11')
            self.assertEqual(c['com.unity.pipeline'], '0.8.0-exp.1')
            self.assertEqual(c['arch'], 'arm64')
            self.assertTrue(report['coexistence']['same_project'])
            self.assertEqual(report['coexistence']['unity_version'], c['unity_editor_version'])
            self.assertEqual(report['coexistence']['official_state'], 'ready')
            self.assertRegex(report['coexistence']['project_identity_sha256'], r'^[0-9a-f]{64}$')
            self.assertLess(c['host_at_start']['load_average'][0], 9)
            self.assertTrue(c['host_at_start']['top_cpu_processes'])
            self.assertTrue(c['host_at_end']['top_cpu_processes'])
            for row in c['host_at_start']['top_cpu_processes']:
                if row['executable'] in ('rustc', 'cargo', 'Unity') and row['pid'] != c['editor_pid']:
                    self.assertLessEqual(row['cpu_percent'], 50)
            for key in ('unity_cli_sha256', 'official_cli_sha256', 'benchmark_sha256', 'source_commit', 'project'):
                self.assertEqual(c[key], reports[0]['conditions'][key])
        self.assertEqual({(r['conditions']['mode'], r['conditions']['focus']) for r in reports},
                         {(mode, focus) for mode in ('process', 'resident') for focus in ('frontmost', 'background')})

    def test_document_tables_match_each_published_measurement(self):
        document = (ROOT / 'docs/comparison.md').read_text()
        for report in self.reports():
            c = report['conditions']
            marker = f'<!-- {c["mode"]}-{c["focus"]} -->'
            self.assertIn(marker, document)
            section = document.split(marker, 1)[1].split('<!--', 1)[0]
            for name in report['results']['unity-cli']:
                values = [report['results'][tool][name][pct]
                          for tool in ('unity-cli', 'official') for pct in ('p50_ms', 'p95_ms')]
                rows = [row for row in section.splitlines() if f'`{name}`' in row]
                self.assertEqual(len(rows), 1, name)
                cells = [cell.strip() for cell in rows[0].split('|') if cell.strip()]
                self.assertEqual(cells[-4:], [f'{value:.2f}' for value in values])

    def test_all_readmes_link_comparison_and_relative_sources_exist(self):
        for path in ROOT.glob('README*.md'):
            self.assertIn('(docs/comparison.md)', path.read_text(), path.name)
        document = (ROOT / 'docs/comparison.md').read_text()
        targets = re.findall(r'\]\(([^)]+)\)', document)
        targets += re.findall(r'^\[[^]]+\]:\s+(\S+)', document, re.MULTILINE)
        for target in targets:
            if target.startswith(('https://', 'http://', '#')):
                continue
            path = target.split('#', 1)[0]
            self.assertTrue((ROOT / 'docs' / path).exists(), path)

    def test_each_feature_row_has_sources_in_both_languages(self):
        document = (ROOT / 'docs/comparison.md').read_text()
        for heading in ('### Feature comparison', '### 機能比較'):
            table = document.split(heading, 1)[1].split('\n### ', 1)[0]
            rows = [line for line in table.splitlines() if line.startswith('|')][2:]
            self.assertEqual(len(rows), 11)
            for row in rows:
                for cell in row.split('|')[2:4]:
                    self.assertRegex(cell, r'\[[^]]+\](?:\(|\[)', row)


class StaffReportEvidenceTests(unittest.TestCase):
    def rows(self, text, heading):
        section = text.split(heading, 1)[1].split('\n##', 1)[0]
        return [[cell.strip() for cell in line.strip('|').split('|')]
                for line in section.splitlines() if line.startswith('|')][2:]

    def test_staff_features_preserve_original_japanese_and_english_marks(self):
        source = (ROOT / 'docs/verification/issue-534/staff-report-2026-10-05.md').read_text(encoding='utf-8')
        document = (ROOT / 'docs/comparison.md').read_text(encoding='utf-8')
        original = self.rows(source, '#### 機能比較')
        japanese = self.rows(document, '### スタッフ調査の機能比較')
        english = self.rows(document, '### Staff feature comparison')
        self.assertEqual(len(original), 24)
        self.assertEqual(japanese, original)
        self.assertEqual(len(english), len(original))
        for translated, row in zip(english, original):
            for actual, expected in zip(translated[1:], row[1:]):
                self.assertEqual(actual[0] if actual[0] in '○△—' else 'n/a',
                                 expected[0] if expected[0] in '○△—' else 'n/a')

    def test_staff_medians_match_source_in_both_languages(self):
        source = (ROOT / 'docs/verification/issue-534/staff-report-2026-10-05.md').read_text(encoding='utf-8')
        document = (ROOT / 'docs/comparison.md').read_text(encoding='utf-8')
        original = self.rows(source, '#### 速度比較')
        self.assertEqual(len(original), 23)
        for heading in ('### Staff speed comparison', '### スタッフ調査の速度比較'):
            rows = self.rows(document, heading)
            self.assertEqual(len(rows), 23)
            self.assertEqual([row[1:] for row in rows], [row[1:] for row in original])
            self.assertTrue(all(float(row[1].split()[0]) < float(row[2].split()[0])
                                for row in rows))

    def test_report_provenance_and_reconstructed_category_mapping(self):
        document = (ROOT / 'docs/comparison.md').read_text(encoding='utf-8')
        for language in ('English', '日本語'):
            section = document.split(f'## {language}\n', 1)[1].split('\n## ', 1)[0]
            for fact in ('2026-10-05', 'Windows', '6000.4.12f1', '0.18.1',
                         '1.0.0-beta.11', '0.7.0-exp.1', '[staff-report]', '○', '△', '—'):
                self.assertIn(fact, section)
        mapping = (ROOT / 'docs/verification/issue-386/resume/staff-scope-reconstruction.md').read_text(encoding='utf-8')
        rows = self.rows(mapping, '## 原本受領と13カテゴリの対応（2026-10-26）')
        self.assertEqual(len(rows), 13)
        self.assertEqual([int(row[0].split('.')[0]) for row in rows], list(range(1, 14)))
        self.assertIn('daemon lifecycle', rows[6][2])
        self.assertIn('not reported', rows[6][2])


if __name__ == '__main__':
    unittest.main()
