"""Host-free contract tests for the real Editor matrix runner."""
import importlib.util
import json
import os
import re
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("matrix", ROOT / "scripts/e2e-matrix.py")
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)


class MatrixTests(unittest.TestCase):
    def test_hub_layout_supports_windows_and_preserves_macos(self):
        windows = Path('C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe')
        self.assertEqual(matrix.editor_layout(windows),
                         ('7000.0.0a16', windows.parent / 'Data'))
        mac = Path('/Applications/Unity/Hub/Editor/6000.7.0b2/Unity.app/Contents/MacOS/Unity')
        self.assertEqual(matrix.editor_layout(mac), ('6000.7.0b2', mac.parent.parent))
        with self.assertRaisesRegex(ValueError, 'Hub Editor path'):
            matrix.editor_layout(Path('/tmp/unversioned/Unity.exe'))

    def test_windows_defaults_include_all_issue_544_editors(self):
        with patch.object(matrix.sys, 'platform', 'win32'), patch.dict(
                matrix.os.environ, {'ProgramFiles': 'C:/Program Files'}):
            editors = matrix.default_editors()
        self.assertEqual([matrix.editor_layout(editor)[0] for editor in editors],
                         ['6000.3.28f1', '6000.6.8f1', '7000.0.0a16'])
        self.assertTrue(all(editor.name == 'Unity.exe' for editor in editors))

    def test_windows_listener_ownership_ignores_remote_port_and_other_processes(self):
        output = ('  TCP    127.0.0.1:6508  0.0.0.0:0  LISTENING  1234\n'
                  '  TCP    127.0.0.1:6509  127.0.0.1:6508  ESTABLISHED  5678\n')
        with patch.object(matrix.sys, 'platform', 'win32'), patch.object(
                matrix.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, output)):
            self.assertEqual(matrix.listener_pids(6508), {1234})
            self.assertEqual(matrix.listener_pids(6510), set())

    def test_ping_requires_matching_reported_cli_and_bridge_versions(self):
        self.assertTrue(matrix.matching_ping({'versionCheck': {
            'status': 'match', 'cliVersion': '0.19.0', 'bridgeVersion': '0.19.0'}}))
        for value in ({}, {'versionCheck': {'status': 'match'}},
                      {'versionCheck': {'status': 'mismatch', 'cliVersion': '0.19.0', 'bridgeVersion': '0.18.1'}}):
            self.assertFalse(matrix.matching_ping(value))

    def test_runtime_requires_positive_mono_or_coreclr_evidence(self):
        mono = {'state': 'completed', 'value': True}
        non_mono = {'state': 'completed', 'value': False}
        corelib = {'state': 'completed', 'value': 'System.Private.CoreLib'}
        self.assertEqual(matrix.editor_runtime(mono), 'Mono')
        self.assertEqual(matrix.editor_runtime(non_mono, corelib), 'CoreCLR')
        for first, second in (({}, None), (non_mono, None), (non_mono, {'value': 'System.Private.CoreLib'})):
            with self.assertRaisesRegex(ValueError, 'runtime'):
                matrix.editor_runtime(first, second)

    def test_macos_listener_ownership_and_inspection_errors(self):
        with patch.object(matrix.sys, 'platform', 'darwin'), patch.object(
                matrix.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, '1234\n')):
            self.assertEqual(matrix.listener_pids(6508), {1234})
        with patch.object(matrix.sys, 'platform', 'darwin'), patch.object(
                matrix.subprocess, 'run', return_value=subprocess.CompletedProcess([], 2, '', 'denied')):
            with self.assertRaisesRegex(RuntimeError, 'inspect listener ownership'):
                matrix.listener_pids(6508)

    def test_gui_fixture_regenerates_only_urp_upgrade_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            settings = project / "ProjectSettings"
            settings.mkdir()
            cache = settings / "URPProjectSettings.asset"
            cache.write_text("m_LastMaterialVersion: 10\n")
            rendering = settings / "GraphicsSettings.asset"
            rendering.write_text("keep render pipeline\n")
            matrix.prepare_perf_fixture(project)
            self.assertFalse(cache.exists())
            self.assertEqual(rendering.read_text(), "keep render pipeline\n")
            self.assertTrue((project / ".unity/perf-owned-project").is_file())
            matrix.prepare_perf_fixture(project)

    def test_perf_requires_gui_without_changing_other_suites(self):
        self.assertTrue(getattr(matrix, "requires_gui", lambda _: False)("perf"))
        self.assertTrue(matrix.requires_gui("compile,perf"))
        self.assertFalse(matrix.requires_gui("input,timeline"))
        self.assertFalse(matrix.requires_gui(None))

    def test_exit_two_requires_actual_missing_module_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            command = ['python3', 'e2e-player-build.py', '--project-path', str(project)]
            self.assertEqual(matrix.step_status('player-build', command, 2), 'FAIL')
            run = project / '.unity/player-build-e2e/fresh'
            run.mkdir(parents=True)
            (run / 'summary.json').write_text(json.dumps({'status': 'UNSUPPORTED', 'failed': 0}))
            (run / 'build-status.json').write_text(json.dumps({'code': 'BUILD_TARGET_MISMATCH'}))
            self.assertEqual(matrix.step_status('player-build', command, 2), 'FAIL')
            (run / 'build-status.json').write_text(json.dumps({'code': 'BUILD_MODULE_MISSING'}))
            self.assertEqual(matrix.step_status('player-build', command, 2), 'UNSUPPORTED')
            self.assertEqual(matrix.step_status('reference', command, 2), 'FAIL')

    def test_extended_suites_use_selected_editor_cli_and_isolated_projects(self):
        from argparse import Namespace
        args = Namespace(unity_cli=Path('/checkout/cli'), port=6532)
        editor = Path('/hub/6000.3.25f1/Unity.app/Contents/MacOS/Unity')
        with patch.object(Path, 'is_dir', return_value=True):
            suites = matrix.extended_suites(editor, args, Path('/run/project'), Path('/run'))
        expected = {'bake', 'player-build', 'video-formats', 'animation-curves',
                    'input-actions-persistence', 'reference'}
        self.assertEqual(set(suites), expected)
        self.assertTrue(expected <= set(matrix.DEFAULT_SUITES))
        for name, commands in suites.items():
            for command in commands:
                self.assertIn(str(args.unity_cli), command, name)
        persistence = suites['input-actions-persistence'][0]
        self.assertIn(str(editor), persistence)
        self.assertIn(str(Path('/run/persistence/project')), persistence)
        windows = suites['player-build'][1]
        self.assertIn('StandaloneWindows64', windows)
        self.assertIn(str(Path('/run/windows/project')), windows)
        self.assertIn(str(editor), windows)

    def test_missing_windows_module_is_probed_on_current_editor(self):
        from argparse import Namespace
        args = Namespace(unity_cli=Path('/checkout/cli'), port=6532)
        with patch.object(Path, 'is_dir', return_value=False):
            suites = matrix.extended_suites(Path('/hub/6000.3.28f1/Unity.app/Contents/MacOS/Unity'),
                                            args, Path('/run/project'), Path('/run'))
        windows = suites['player-build'][1]
        self.assertNotIn('--launch', windows)
        self.assertIn(str(Path('/run/project')), windows)
        self.assertEqual(windows[windows.index('--port') + 1], '6532')

    def test_packages_follow_editor_catalog_and_preserve_bridge(self):
        catalog = {"com.unity.visualeffectgraph": {"version": "14.0.12"},
                   "com.unity.render-pipelines.universal": {"version": "14.0.12"},
                   "com.unity.test-framework": {"version": "1.1.33"},
                   "com.unity.timeline": {"version": "1.7.7"},
                   "com.unity.ugui": {"version": "1.0.0"}}
        result = matrix.package_manifest("2022.3.62f3", catalog, [])
        self.assertEqual(result["dependencies"]["com.unity.visualeffectgraph"], "14.0.12")
        self.assertEqual(result["dependencies"]["com.unity.ugui"], "1.0.0")
        self.assertEqual(result["dependencies"]["com.akiojin.unity-cli-bridge"], "file:unity-cli-bridge")
        self.assertIn("com.akiojin.unity-cli-bridge", result["testables"])

    def test_missing_vfx_catalog_entry_is_not_silently_skipped(self):
        with self.assertRaisesRegex(ValueError, "visualeffectgraph"):
            matrix.package_manifest("6000.7.0b2", {}, [])

    def test_unity_seven_uses_its_catalog_instead_of_unity_six_package_pins(self):
        names = ('visualeffectgraph', 'render-pipelines.universal', 'test-framework', 'timeline', 'ugui',
                 'addressables', 'inputsystem', 'recorder', 'ai.navigation')
        catalog = {'com.unity.' + name: {'version': '7.0.0'} for name in names}
        catalog['com.unity.addressables']['version'] = '4.1.2'
        result = matrix.package_manifest('7000.0.0a16', catalog, [])['dependencies']
        self.assertEqual(result['com.unity.addressables'], '4.1.2')
        self.assertEqual(result['com.unity.inputsystem'], '7.0.0')
        self.assertEqual(matrix.package_manifest('6000.6.8f1', catalog, [])['dependencies']
                         ['com.unity.addressables'], '2.9.1')
        del catalog['com.unity.addressables']
        with self.assertRaisesRegex(ValueError, 'addressables'):
            matrix.package_manifest('7000.0.0a16', catalog, [])

    def test_readiness_rejects_error_missing_and_busy_states(self):
        for value in [{}, {"error": "offline"}, {"isCompiling": True, "isUpdating": False},
                      {"success": False, "isCompiling": False, "isUpdating": False},
                      {"isCompiling": False, "isUpdating": False, "errorCount": 1}]:
            self.assertFalse(matrix.ready(value), value)
        self.assertTrue(matrix.ready({"isCompiling": False, "isUpdating": False}))

    def test_failed_or_partial_suites_cannot_pass_report(self):
        self.assertFalse(matrix.all_passed([]))
        self.assertFalse(matrix.all_passed([{"status": "PASS"}, {"status": "SKIP"}]))
        self.assertFalse(matrix.all_passed([{"status": "FAIL"}]))
        self.assertTrue(matrix.all_passed([{"status": "PASS"}]))
        self.assertEqual(matrix.result_status([{'status': 'PASS'}, {'status': 'UNSUPPORTED'}]), 'UNSUPPORTED')
        self.assertEqual(matrix.result_status([{'status': 'UNSUPPORTED'}, {'status': 'FAIL'}]), 'FAIL')
        self.assertEqual(matrix.result_status([]), 'FAIL')

    def test_suite_timeout_terminates_the_owned_process(self):
        with tempfile.TemporaryFile(mode="w+") as log:
            with self.assertRaises(subprocess.TimeoutExpired):
                matrix.run_suite([sys.executable, "-c", "import time; time.sleep(30)"],
                                 {}, log, 0.05)
            self.assertEqual(matrix.run_suite([sys.executable, "-c", "raise SystemExit(3)"],
                                             {}, log, 5), 3)

    def test_shell_suite_preserves_unity_paths_and_uses_lf_json(self):
        with tempfile.TemporaryFile() as log:
            status = matrix.run_suite(
                ['bash', '-c', "jq -nc --arg path /Canvas/InputE2E_Panel '{path:$path}'"],
                dict(os.environ), log, 20)
            self.assertEqual(status, 0)
            log.seek(0)
            output = log.read()
            self.assertEqual(json.loads(output), {'path': '/Canvas/InputE2E_Panel'})
            self.assertNotIn(b'\r', output)

    def test_shell_suite_can_read_a_posix_input_file_with_native_jq(self):
        with tempfile.TemporaryDirectory() as directory, tempfile.TemporaryFile() as log:
            path = Path(directory) / 'input.json'
            payload = {'path': '/Canvas', 'text': '\u65e5\u672c\u8a9e'}
            path.write_text(json.dumps(payload), encoding='utf-8')
            command = ('input="$JQ_INPUT_FILE"; if command -v cygpath >/dev/null; then '
                       'input="$(cygpath -u "$input")"; fi; jq -c . "$input"')
            status = matrix.run_suite(['bash', '-c', command],
                                     dict(os.environ, JQ_INPUT_FILE=str(path)), log, 20)
            self.assertEqual(status, 0)
            log.seek(0)
            self.assertEqual(json.loads(log.read()), payload)

    def test_shell_python_reads_utf8_metrics_without_a_locale_dependency(self):
        with tempfile.TemporaryDirectory() as directory, tempfile.TemporaryFile() as log:
            command = "printf '\u65e5\u672c\u8a9e' | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))'"
            env = matrix.runtime_environment(Path(directory), Path('cli'))
            status = matrix.run_suite(['bash', '-c', command], env, log, 20)
            self.assertEqual(status, 0)
            log.seek(0)
            self.assertEqual(json.loads(log.read()), '\u65e5\u672c\u8a9e')

    def test_lsp_size_metrics_keep_diagnostics_separate_and_preserve_failure_exit(self):
        source = (ROOT / 'scripts/lsp-perf-check.sh').read_text(encoding='utf-8')
        functions = '\n'.join(re.search(r'(?ms)^' + name + r'\(\) \{.*?^\}', source)[0]
                              for name in ('calc_stats_json', 'collect_csv', 'run_lsp_case'))
        command = '''set -euo pipefail
fake_cli() { printf '%s\\n' '{"data":{"backend":"lsp"}}'; }
now_ms() { printf '1\\n'; }
calc_size_json() {
  printf 'fixture diagnostic\\n' >&2
  printf '%s\\n' '{"bytes":1,"chars":1,"tokens":1}'
  return "$METRICS_RC"
}
UNITY_CLI=fake_cli
WARMUP=0
RUNS=1
''' + functions + '''
run_lsp_case fixture fixture '{}' 100
printf '%s\\n' "$CASE_RESULT_JSON"
'''
        for exit_code in (0, 9):
            with self.subTest(exit_code=exit_code), tempfile.TemporaryDirectory() as directory, tempfile.TemporaryFile() as log:
                env = matrix.runtime_environment(Path(directory), Path('cli'))
                env['METRICS_RC'] = str(exit_code)
                status = matrix.run_suite(['bash', '-c', command], env, log, 20)
                self.assertEqual(status == 0, exit_code == 0)
                log.seek(0)
                output = log.read().decode('utf-8')
                if exit_code == 0:
                    self.assertIn('fixture diagnostic', output)
                    self.assertTrue(json.loads(output.splitlines()[-1])['pass'])

    def test_runtime_isolates_registry_and_does_not_inherit_another_editors_token(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(matrix.sys, 'platform', 'win32'), patch.dict(os.environ, {
                'UNITY_CLI_EDITORS_DIR': 'unrelated-project/editors',
                'UNITY_CLI_AUTH_TOKEN_FILE': 'unrelated-project/private.json'}):
            output = Path(directory)
            env = matrix.runtime_environment(output, Path('cli'))
            self.assertEqual(Path(env['UNITY_CLI_EDITORS_DIR']), output / 'editors')
            self.assertNotIn('UNITY_CLI_AUTH_TOKEN_FILE', env)
            self.assertGreater(int(env['UNITY_CLI_UNITYD_PORT']), 0)
            self.assertGreater(int(env['UNITY_CLI_LSPD_PORT']), 0)
            self.assertNotEqual(env['UNITY_CLI_UNITYD_PORT'], env['UNITY_CLI_LSPD_PORT'])
            self.assertEqual(os.environ['UNITY_CLI_AUTH_TOKEN_FILE'], 'unrelated-project/private.json')

    def test_fixture_is_isolated_and_ignores_package_hash_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "repo/UnityCliBridge"
            for name in ("Assets", "ProjectSettings", "Packages/unity-cli-bridge"):
                (source / name).mkdir(parents=True)
            (source / "ProjectSettings/ProjectVersion.txt").write_text("original\n")
            fixtures = root / "repo/tests/fixtures/hot-reload"
            fixtures.mkdir(parents=True)
            for name in ("HotReloadProbe.cs", "HotReloadE2EFixture.cs"):
                (fixtures / name).write_text("// fixture\n")
            editor = root / "2022.3.62f3/Unity.app/Contents/MacOS/Unity"
            editor.parent.mkdir(parents=True)
            pm = editor.parent.parent / "Resources/PackageManager"
            (pm / "Editor").mkdir(parents=True)
            catalog = {name: {"version": "1.0.0"} for name in (
                "com.unity.visualeffectgraph", "com.unity.render-pipelines.universal",
                "com.unity.ugui", "com.unity.test-framework", "com.unity.timeline")}
            (pm / "Editor/manifest.json").write_text(json.dumps({"packages": catalog}))
            (pm / "BuiltInPackages/com.unity.modules.ui").mkdir(parents=True)
            (pm / "BuiltInPackages/com.unity.modules.ui.sha1").touch()
            destination = root / "run"
            destination.mkdir()
            with patch.object(matrix, "ROOT", root / "repo"):
                version, project, manifest = matrix.prepare(editor, destination)
            self.assertEqual(version, "2022.3.62f3")
            self.assertIn("com.unity.modules.ui", manifest["dependencies"])
            self.assertNotIn("com.unity.modules.ui.sha1", manifest["dependencies"])
            self.assertEqual((source / "ProjectSettings/ProjectVersion.txt").read_text(), "original\n")
            self.assertIn(version, (project / "ProjectSettings/ProjectVersion.txt").read_text())
            self.assertTrue((project / "Assets/HotReloadProbe.cs").is_file())
            self.assertTrue((project / "Assets/Editor/HotReloadE2EFixture.cs").is_file())

    def test_real_hot_reload_suites_need_backend_and_add_x64_editor_when_present(self):
        from argparse import Namespace
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            editor = root / "hub/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
            x64 = root / "x64/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
            x64.parent.mkdir(parents=True)
            x64.touch()
            args = Namespace(fsr_path=None, x64_editor_root=root / "x64", port=6508)
            self.assertEqual(matrix.hot_reload_apply_suites(editor, "6000.3.25f1", args, root), {})
            args.fsr_path = root / "fsr"
            suites = matrix.hot_reload_apply_suites(editor, "6000.3.25f1", args, root)
            self.assertEqual(list(suites), ["hot-reload-apply", "hot-reload-apply-x64"])
            native, rosetta = suites.values()
            self.assertIn("UNITY_PATH=" + str(editor), native)
            self.assertIn("UNITY_PATH=" + str(x64), rosetta)
            self.assertEqual(rosetta[rosetta.index("--require-arch") + 1], "X64")
            for command in (native, rosetta):
                self.assertEqual(command[command.index("--expect") + 1], "supported")
                self.assertEqual(command[command.index("--port") + 1], "6509")
            # No x64 build of this version: only the native Editor runs, nothing is reported as skipped.
            self.assertEqual(list(matrix.hot_reload_apply_suites(editor, "2022.3.62f3", args, root)), ["hot-reload-apply"])

    def test_default_selection_appends_real_hot_reload_suites(self):
        self.assertEqual(matrix.selected_suites(None, [])[-1], "all-tools")
        self.assertNotIn("hot-reload-apply", matrix.selected_suites(None, []))
        chosen = matrix.selected_suites(None, ["hot-reload-apply", "hot-reload-apply-x64"])
        self.assertEqual(chosen[-2:], ["hot-reload-apply", "hot-reload-apply-x64"])
        self.assertEqual(matrix.selected_suites("eval,hot-reload-apply", ["hot-reload-apply"]), ["eval", "hot-reload-apply"])
        with self.assertRaisesRegex(ValueError, "hot-reload-apply-x64"):
            matrix.selected_suites("hot-reload-apply-x64", ["hot-reload-apply"])


if __name__ == "__main__":
    unittest.main()
