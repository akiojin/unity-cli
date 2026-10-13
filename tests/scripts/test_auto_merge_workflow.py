"""Checks .github/workflows/auto-merge.yml (Issue #486).

The step scripts run against a fake `gh` on PATH, so the permission check,
pinned head SHA and synchronize revocation are exercised without GitHub.
"""

import os
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / '.github' / 'workflows' / 'auto-merge.yml'
README = ROOT / '.github' / 'workflows' / 'README.md'

LABEL = 'reviewed-safe'
SHA = '0123456789abcdef0123456789abcdef01234567'

# Fake gh: logs every call, answers from FAKE_* environment variables.
FAKE_GH = r'''#!/bin/sh
printf '%s\n' "$*" >> "$FAKE_GH_LOG"
case "$*" in
  *collaborators/*/permission*) printf '%s\n' "$FAKE_PERMISSION"; exit 0 ;;
  "pr view"*headRefOid*) printf '%s\n' "$FAKE_HEAD_SHA"; exit 0 ;;
  "pr view"*mergeable*) printf 'MERGEABLE\n'; exit 0 ;;
  "pr merge"*--disable-auto*) exit "${FAKE_DISABLE_EXIT:-0}" ;;
  "pr merge"*) exit 0 ;;
  "pr edit"*) exit 0 ;;
esac
exit 0
'''


def load_workflow():
    with WORKFLOW.open(encoding='utf-8') as f:
        return yaml.safe_load(f)


def triggers(workflow):
    # PyYAML (YAML 1.1) reads the bare `on` key as True.
    return workflow.get('on', workflow.get(True))


def run_job(job, env):
    """Run every `run` step of a job with a fake gh; return the gh call log."""
    with tempfile.TemporaryDirectory() as tmp:
        tmp_path = Path(tmp)
        gh = tmp_path / 'gh'
        gh.write_text(FAKE_GH, encoding='utf-8')
        gh.chmod(gh.stat().st_mode | stat.S_IEXEC)
        log = tmp_path / 'gh.log'
        log.touch()
        full_env = {
            'PATH': f'{tmp_path}{os.pathsep}{os.environ["PATH"]}',
            'FAKE_GH_LOG': str(log),
            'GH_TOKEN': 'token',
            'REPO': 'akiojin/unity-cli',
            'PR_NUMBER': '481',
            'LABEL': LABEL,
            'HEAD_SHA': SHA,
            'FAKE_HEAD_SHA': SHA,
            'FAKE_PERMISSION': 'write',
            'ACTOR': 'reviewer',
        }
        full_env.update(env)
        code = 0
        for step in job['steps']:
            if 'run' not in step:
                continue
            result = subprocess.run(['bash', '-c', step['run']], env=full_env,
                                    capture_output=True, text=True)
            if result.returncode != 0:
                code = result.returncode
                break
        return code, log.read_text(encoding='utf-8').splitlines()


class AutoMergeWorkflowStructure(unittest.TestCase):
    def setUp(self):
        self.workflow = load_workflow()
        self.jobs = self.workflow['jobs']

    def test_triggers_on_labeled_and_synchronize(self):
        on = triggers(self.workflow)
        types = on['pull_request_target']['types']
        self.assertIn('labeled', types)
        self.assertIn('synchronize', types)

    def test_minimal_permissions(self):
        self.assertEqual(self.workflow['permissions'],
                         {'contents': 'write', 'pull-requests': 'write'})

    def test_never_checks_out_fork_code(self):
        for name, job in self.jobs.items():
            for step in job['steps']:
                self.assertNotIn('uses', step, f'{name} must only call gh')

    def test_owner_job_keeps_behavior_and_ignores_label_events(self):
        cond = self.jobs['auto-merge']['if']
        self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", cond)
        self.assertIn("'akiojin'", cond)
        self.assertIn("'dependabot[bot]'", cond)
        self.assertIn("github.event.action != 'labeled'", cond)

    def test_external_job_conditions(self):
        cond = self.jobs['external-reviewed']['if']
        self.assertIn("github.event.action == 'labeled'", cond)
        self.assertIn(f"github.event.label.name == '{LABEL}'", cond)
        self.assertIn("github.event.pull_request.head.repo.full_name != github.repository", cond)
        self.assertIn('github.event.pull_request.draft == false', cond)
        self.assertIn("github.event.pull_request.base.ref == 'develop'", cond)

    def test_revoke_job_conditions(self):
        cond = self.jobs['external-revoke']['if']
        self.assertIn("github.event.action == 'synchronize'", cond)
        self.assertIn("github.event.pull_request.head.repo.full_name != github.repository", cond)
        self.assertIn(f"'{LABEL}'", cond)

    def test_external_job_pins_labeled_head_sha(self):
        env = self.jobs['external-reviewed']['steps'][0]['env']
        self.assertEqual(env['HEAD_SHA'], '${{ github.event.pull_request.head.sha }}')
        self.assertEqual(env['ACTOR'], '${{ github.event.sender.login }}')


class ExternalReviewedJob(unittest.TestCase):
    def setUp(self):
        self.job = load_workflow()['jobs']['external-reviewed']

    def test_write_permission_enables_pinned_merge(self):
        for permission in ('admin', 'maintain', 'write'):
            code, calls = run_job(self.job, {'FAKE_PERMISSION': permission})
            self.assertEqual(code, 0, calls)
            merges = [c for c in calls if c.startswith('pr merge')]
            self.assertEqual(len(merges), 1, calls)
            self.assertIn('--auto', merges[0])
            self.assertIn('--merge', merges[0])
            self.assertIn(f'--match-head-commit {SHA}', merges[0])

    def test_owner_is_trusted_without_permission_lookup(self):
        code, calls = run_job(self.job, {'ACTOR': 'akiojin', 'FAKE_PERMISSION': 'none'})
        self.assertEqual(code, 0, calls)
        self.assertTrue(any(c.startswith('pr merge') for c in calls), calls)

    def test_insufficient_permission_does_not_merge(self):
        for permission in ('read', 'triage', 'none'):
            code, calls = run_job(self.job, {'FAKE_PERMISSION': permission})
            self.assertNotEqual(code, 0, calls)
            self.assertFalse(any(c.startswith('pr merge') for c in calls), calls)
            self.assertTrue(any(c.startswith('pr edit') and '--remove-label' in c
                                for c in calls), calls)

    def test_head_moved_since_label_does_not_merge(self):
        code, calls = run_job(self.job, {'FAKE_HEAD_SHA': 'f' * 40})
        self.assertNotEqual(code, 0, calls)
        self.assertFalse(any(c.startswith('pr merge') for c in calls), calls)
        self.assertTrue(any('--remove-label' in c for c in calls), calls)


class ExternalRevokeJob(unittest.TestCase):
    def setUp(self):
        self.job = load_workflow()['jobs']['external-revoke']

    def test_new_push_removes_label_and_disables_auto_merge(self):
        code, calls = run_job(self.job, {})
        self.assertEqual(code, 0, calls)
        self.assertTrue(any(c.startswith('pr edit') and f'--remove-label {LABEL}' in c
                            for c in calls), calls)
        self.assertTrue(any(c.startswith('pr merge') and '--disable-auto' in c
                            for c in calls), calls)

    def test_disable_without_auto_merge_still_succeeds(self):
        code, calls = run_job(self.job, {'FAKE_DISABLE_EXIT': '1'})
        self.assertEqual(code, 0, calls)
        self.assertTrue(any('--remove-label' in c for c in calls), calls)


class WorkflowReadme(unittest.TestCase):
    def test_documents_external_review_flow(self):
        text = README.read_text(encoding='utf-8')
        self.assertIn(LABEL, text)
        self.assertIn('--match-head-commit', text)
        self.assertIn('synchronize', text)


if __name__ == '__main__':
    unittest.main()
