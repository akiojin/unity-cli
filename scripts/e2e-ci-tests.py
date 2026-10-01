#!/usr/bin/env python3
"""Verify #445 against isolated real Unity Editors (macOS)."""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('matrix', ROOT / 'scripts/e2e-matrix.py')
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--cli', type=Path, default=ROOT / 'target/debug/unity-cli')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--port', type=int, default=6551)
    parser.add_argument('--junit-schema', type=Path, required=True)
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    started = datetime.now(timezone.utc).isoformat()
    editor = Path(f'/Applications/Unity/Hub/Editor/{args.version}/Unity.app/Contents/MacOS/Unity')
    _, project, _ = matrix.prepare(editor, out)
    settings = project / 'ProjectSettings/EditorSettings.asset'
    settings.write_text(re.sub(r'm_EnterPlayModeOptionsEnabled: \d+', 'm_EnterPlayModeOptionsEnabled: 0', settings.read_text()))
    for mode in ('EditMode', 'PlayMode'):
        folder = project / 'Assets' / 'CiTests' / mode
        folder.mkdir(parents=True)
        asm = {'name': f'Ci{mode}', 'optionalUnityReferences': ['TestAssemblies']}
        if mode == 'EditMode':
            asm['includePlatforms'] = ['Editor']
        (folder / f'Ci{mode}.asmdef').write_text(json.dumps(asm))
        (folder / f'Ci{mode}.cs').write_text('''using NUnit.Framework;
public class CiMODE {
    [Test] public void Passing() { Assert.That(2 + 2, Is.EqualTo(4)); }
    [Test] public void Failing() { Assert.Fail("intentional <failure> & detail"); }
}
'''.replace('MODE', mode))
    env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE='1', UNITY_CLI_EDITORS_DIR=str(out / 'editors'),
               UNITY_CLI_TOOLS_ROOT=str(out / 'tools'), UNITY_CLI_CACHE_ROOT=str(out / 'cache'),
               UNITY_CLI_REGISTRY_PATH=str(out / 'instances.json'),
               UNITY_PROJECT_ROOT=str(project), UNITY_CLI_PORT_OVERRIDE=str(args.port),
               UNITY_EDITOR_PATH=str(editor), UNITY_CLI_PORT=str(args.port),
               UNITY_CLI_HOST='127.0.0.1')
    for key in ('UNITY_CLI_AUTH_TOKEN_FILE', 'UNITY_CLI_ALLOW_UNAUTHENTICATED'):
        env.pop(key, None)
    base = [str(args.cli.resolve()), '--project-path', str(project), '--port', str(args.port)]
    checks = []
    gui = None

    def check(name, condition):
        checks.append({'name': name, 'passed': bool(condition)})
        (out / 'checks.json').write_text(json.dumps({'started_at': started, 'version': args.version, 'checks': checks}, indent=2))
        print(name, 'PASS' if condition else 'FAIL', flush=True)
        if not condition:
            raise AssertionError(name)

    def processes():
        lines = subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines()
        return [line.split()[0] for line in lines if str(editor) in line and str(project) in line and '-projectPath' in line and '-batchMode -nographics -name AssetImportWorker' not in line and 'AssetImportWorker' not in line]

    def run(name, mode='editmode', fail=False, extra=(), expected=0, report_format='junit'):
        report = out / f'{name}.xml'
        case = 'Failing' if fail else 'Passing'
        fixture = 'CiEditMode' if mode == 'editmode' else 'CiPlayMode'
        test_filter = 'DomainReloadResultTests' if mode == 'playmode' and not fail else f'{fixture}.{case}'
        expected_tests = 2 if test_filter == 'DomainReloadResultTests' else 1
        command = base + ['test', '--mode', mode, '--filter', test_filter,
                          '--report', report_format, '--output', str(report), '--timeout', '600', *extra]
        with (out / f'{name}.log').open('w') as log:
            child = subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT)
            observed = []
            try:
                deadline = time.monotonic() + 630
                while child.poll() is None and time.monotonic() < deadline:
                    observed.append(processes())
                    time.sleep(.25)
                code = child.wait(timeout=5)
            finally:
                if child.poll() is None:
                    child.kill()
                    child.wait()
        (out / f'{name}-processes.json').write_text(json.dumps(observed))
        check(f'{name}: exit {expected}', code == expected)
        if expected in (0, 8):
            tree = ET.parse(report)
            if report_format == 'junit':
                schema = subprocess.run(['xmllint', '--noout', '--schema', str(args.junit_schema.resolve()), str(report)], capture_output=True, text=True)
                check(f'{name}: JUnit schema', schema.returncode == 0)
                check(f'{name}: JUnit structure', tree.getroot().tag == 'testsuites' and len(tree.findall('.//testcase')) == expected_tests)
            else:
                check(f'{name}: NUnit structure', tree.getroot().tag == 'test-run' and len(tree.findall('./test-suite/test-case')) == 1)
            check(f'{name}: failure count', len(tree.findall('.//failure')) == int(fail))
        check(f'{name}: no owned Editor left', processes() == ([str(gui.pid)] if gui else []))
        if gui:
            check(f'{name}: GUI reused throughout', all(p == [str(gui.pid)] for p in observed))
        return report

    try:
        check('Editor initially stopped', not processes())
        run('edit-pass')
        run('nunit-pass', report_format='nunit')
        run('edit-fail', fail=True, expected=8, extra=['--output-format', 'github'])
        check('GitHub annotation has file and line', '::error file=' in (out / 'edit-fail.log').read_text() and ',line=' in (out / 'edit-fail.log').read_text())
        for disabled in (False, True):
            run(f'play-pass-reload-{not disabled}', mode='playmode', extra=['--disable-domain-reload'] if disabled else [])
            run(f'play-fail-reload-{not disabled}', mode='playmode', fail=True, expected=8, extra=['--disable-domain-reload'] if disabled else [])
        gui = subprocess.Popen([str(editor), '-projectPath', str(project), '-logFile', str(out / 'gui.log')], env=env,
                               stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        run('gui-reuse')
        with (out / 'regression-250.log').open('w') as log:
            regression = subprocess.run(['python3', str(ROOT / 'scripts/e2e-test-domain-reload.py'),
                '--cli', str(args.cli.resolve()), '--port', str(args.port),
                '--output', str(out / 'regression-250.json')], env=env, stdout=log, stderr=subprocess.STDOUT, timeout=600)
        check('#250 raw Domain Reload regression', regression.returncode == 0)
        matrix.stop_owned(gui)
        gui = None
        broken = project / 'Assets/CiTests/EditMode/Broken.cs'
        broken.write_text('public class Broken { syntax error }')
        run('compile-error', expected=6)
    finally:
        matrix.stop_owned(gui)


if __name__ == '__main__':
    main()
