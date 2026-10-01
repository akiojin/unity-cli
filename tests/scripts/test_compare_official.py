import importlib.util
import json
from pathlib import Path
import sys
import time
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
spec = importlib.util.spec_from_file_location('compare', ROOT / 'scripts/bench-compare-official.py')
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)


class CompareTests(unittest.TestCase):
    def shell(self, expression):
        code = 'import sys,json,time\nfor line in sys.stdin:\n r=json.loads(line)\n if r.get("type")=="shutdown": break\n ' + expression
        return bench.OfficialShell([sys.executable, '-u', '-c', code], {}, 2)

    def test_shell_reuses_process_and_correlates_responses(self):
        with self.shell('print(json.dumps({"id":r["id"],"exitCode":0,"envelope":{"success":True,"data":r["argv"]}}),flush=True)') as shell:
            pid = shell.process.pid
            self.assertEqual(shell.request(['one'])['data'], ['one'])
            self.assertEqual(shell.request(['two'])['data'], ['two'])
            self.assertEqual(shell.process.pid, pid)
        self.assertIsNotNone(shell.process.poll())

    def test_shell_rejects_mismatched_id(self):
        with self.shell('print(json.dumps({"id":"wrong","exitCode":0,"envelope":{"success":True}}),flush=True)') as shell:
            with self.assertRaises(ValueError):
                shell.request(['one'])

    def test_shell_rejects_in_band_failure(self):
        with self.shell('print(json.dumps({"id":r["id"],"exitCode":6,"envelope":{"success":False}}),flush=True)') as shell:
            with self.assertRaises(ValueError):
                shell.request(['one'])

    def test_shell_timeout_is_bounded(self):
        started = time.monotonic()
        with self.shell('time.sleep(10)') as shell:
            shell.timeout = .2
            with self.assertRaises(TimeoutError):
                shell.request(['one'])
        self.assertLess(time.monotonic() - started, 3)

    def test_failed_create_is_not_a_measurement(self):
        with self.assertRaises(ValueError):
            bench.check_unity_cli('create_gameobject', '{"success":false,"error":"failed"}')

    def test_official_nested_failure_is_rejected(self):
        with self.assertRaises(ValueError):
            bench.check_official('create_gameobject', json.dumps({'success':True,'data':{'result':{'success':False,'error':'failed'}}}))

    def test_empty_read_and_unapplied_material_are_not_success(self):
        for tool, result in [('read_text_file', {'contents': ''}),
                             ('set_material_properties', {'applied': [], 'unknown': ['_Color']})]:
            with self.subTest(tool=tool), self.assertRaises(ValueError):
                bench.check_official(tool, json.dumps({'success': True, 'data': {'result': result}}))
        bench.check_official('read_text_file', json.dumps({'success': True, 'data': {'result': {'contents': 'source'}}}))
        bench.check_official('set_material_properties', json.dumps({'success': True, 'data': {'result': {'applied': ['_Color']}}}))

    def test_eval_must_report_completed(self):
        with self.assertRaises(ValueError):
            bench.check_unity_cli('eval', '{"state":"runtime_error","value":3}')
        bench.check_unity_cli('eval', '{"state":"completed","value":3}')

    def test_process_checks_accept_the_shared_cli_envelope(self):
        for operation, payload in [('eval', {'state': 'completed', 'value': 3}),
                                   ('hierarchy', {'hierarchy': []})]:
            response = {'success': True, 'command': operation, 'data': payload,
                        'errors': [], 'warnings': []}
            bench.check_unity_cli(operation, json.dumps(response))
            response['success'] = False
            with self.assertRaises(ValueError):
                bench.check_unity_cli(operation, json.dumps(response))

    def test_resident_operations_match_all_23_staff_operations(self):
        names = [row[0] for row in bench.resident_operations()]
        self.assertEqual(len(names), 23)
        self.assertEqual(len(set(names)), 23)
        self.assertEqual(names[-2:], ['play_until_ready', 'stop_until_ready'])

    def test_read_uses_same_asset_within_official_authoring_root(self):
        row = next(row for row in bench.resident_operations() if row[0] == 'read_csharp')
        self.assertTrue(row[2]['path'].startswith('Assets/'))
        self.assertEqual(row[2]['path'], row[4]['path'])

    def test_transition_waits_for_completed_state(self):
        calls = []
        states = iter([
            {'playMode': 'stopped', 'compiling': False, 'domainReloadInProgress': False},
            {'playMode': 'playing', 'compiling': True, 'domainReloadInProgress': False},
            {'playMode': 'playing', 'compiling': False, 'domainReloadInProgress': False},
        ])
        def call(tool, params):
            calls.append(tool)
            return {} if tool == 'editor_play' else next(states)
        result = bench.official_transition(call, True, 1)
        self.assertFalse(result['compiling'])
        self.assertEqual(calls, ['editor_play'] + ['editor_status'] * 3)

    def test_benchmark_does_not_reuse_another_projects_daemon_or_host(self):
        with patch.dict('os.environ', {'UNITY_CLI_HOST': 'remote', 'UNITY_CLI_TOOLS_ROOT': '/other',
                                      'UNITY_CLI_ALLOW_UNAUTHENTICATED': '1',
                                      'UNITY_CLI_AUTH_TOKEN_FILE': '/other/token'}, clear=True):
            env = bench.benchmark_environment(Path('/owned/project'), 6471)
        self.assertEqual(env['UNITY_CLI_HOST'], '127.0.0.1')
        self.assertEqual(env['UNITY_CLI_PORT'], '6471')
        self.assertTrue(env['UNITY_CLI_TOOLS_ROOT'].startswith('/owned/project/'))
        self.assertNotIn('UNITY_CLI_ALLOW_UNAUTHENTICATED', env)
        self.assertNotIn('UNITY_CLI_AUTH_TOKEN_FILE', env)

    def test_coexistence_rejects_a_different_project(self):
        project = ROOT.resolve()
        bridge = {'projectRoot': str(project), 'unity': {'unityVersion': '6000.3.25f1'}}
        official = {'projectPath': str(project.parent), 'unityVersion': '6000.3.25f1', 'status': 'ready'}
        with self.assertRaises(ValueError):
            bench.verify_coexistence(project, bridge, official)
        official['projectPath'] = str(project)
        self.assertTrue(bench.verify_coexistence(project, bridge, official)['same_project'])

    def test_coexistence_rejects_a_different_editor_version(self):
        project = ROOT.resolve()
        bridge = {'projectRoot': str(project), 'unity': {'unityVersion': '6000.3.25f1'}}
        official = {'projectPath': str(project), 'unityVersion': '2022.3.62f3', 'status': 'ready'}
        with self.assertRaises(ValueError):
            bench.verify_coexistence(project, bridge, official)


if __name__ == '__main__':
    unittest.main()
