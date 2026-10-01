#!/usr/bin/env python3
"""Verify custom tools after live recompilation in an isolated macOS Editor."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import socket
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def load_script(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


matrix = load_script('custom_tools_matrix', 'e2e-matrix.py')
auth = load_script('custom_tools_auth', 'e2e-bridge-auth.py')


def contains_object(value, name):
    if isinstance(value, dict):
        return value.get('name') == name or any(contains_object(child, name) for child in value.values())
    return isinstance(value, list) and any(contains_object(child, name) for child in value)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True, choices=('2022.3.62f3', '6000.3.25f1'))
    parser.add_argument('--cli', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    started_at = datetime.now(timezone.utc).isoformat()
    editor = Path(f'/Applications/Unity/Hub/Editor/{args.version}/Unity.app/Contents/MacOS/Unity')
    _, project, _ = matrix.prepare(editor, out)
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    # Exercise the CLI under test directly, without a machine-global daemon.
    (out / 'direct-only').write_text('not a directory\n')
    env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE='1',
               UNITY_CLI_EDITORS_DIR=str(out / 'editors'),
               UNITY_CLI_TOOLS_ROOT=str(out / 'direct-only'),
               UNITY_CLI_CACHE_ROOT=str(out / 'cache'),
               UNITY_CLI_REGISTRY_PATH=str(out / 'instances.json'),
               UNITY_PROJECT_ROOT=str(project), UNITY_CLI_PORT_OVERRIDE=str(port),
               UNITY_CLI_HOST='127.0.0.1', UNITY_CLI_PORT=str(port),
               UNITY_CLI_ALLOW_BATCH_HOST='1',
               UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(out / 'stop'))
    env.pop('UNITY_CLI_ALLOW_UNAUTHENTICATED', None)
    env.pop('UNITY_CLI_AUTH_TOKEN_FILE', None)
    process = subprocess.Popen([str(editor), '-batchmode', '-nographics', '-projectPath', str(project),
        '-executeMethod', 'UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run',
        '-logFile', str(out / 'editor.log')], env=env,
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    lockfile = out / 'editors' / f'{process.pid}.json'
    checks, transcript = [], []
    completed = False
    nunit_result = None

    def check(name, condition):
        checks.append({'name': name, 'passed': bool(condition)})
        print(name, 'PASS' if condition else 'FAIL', flush=True)
        if not condition:
            raise AssertionError(name)

    def bridge(tool, params):
        token = json.loads(lockfile.read_text()).get('authToken')
        return auth.request(port, {'id': 'custom-tools-e2e', 'type': tool, 'params': params, 'authToken': token})

    def invoke(arguments):
        result = subprocess.run([str(args.cli.resolve()), '--project-path', str(project),
            '--host', '127.0.0.1', '--port', str(port), '--timeout-ms', '10000', '--output', 'json'] + arguments,
            env=env, text=True, capture_output=True, timeout=40)
        value = json.loads(result.stdout)
        transcript.append({'args': arguments, 'exit': result.returncode, 'response': value})
        return result.returncode, value

    def cli(arguments, expected=0):
        code, value = invoke(arguments)
        check('CLI ' + ' '.join(arguments[:3]), code == expected and value.get('success') == (expected == 0))
        return value

    def raw(tool, params=None, expected=0):
        return cli(['raw', tool, '--json', json.dumps(params or {})], expected)

    def wait_ready():
        deadline = time.monotonic() + 900
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError('Editor exited; see editor.log')
            try:
                response = bridge('get_editor_state', {})
                value = response.get('result', response.get('data', response))
                if matrix.ready(value.get('state', value)):
                    return
            except (OSError, ValueError, RuntimeError):
                pass
            time.sleep(2)
        raise RuntimeError('Editor readiness timeout')

    def reload_and_wait(name):
        # Refresh may close its TCP connection as the domain reload starts.
        invoke(['raw', 'refresh_assets', '--json', '{}'])
        deadline = time.monotonic() + 180
        while time.monotonic() < deadline:
            wait_ready()
            code, value = invoke(['tool', 'schema', name])
            if code == 0 and value.get('success') and value.get('data', {}).get('name') == name:
                return value['data']
            time.sleep(1)
        raise RuntimeError('Custom tool did not appear after recompilation: ' + name)

    try:
        wait_ready()
        check('macOS Apple Silicon host', platform.system() == 'Darwin' and platform.machine() == 'arm64')
        before = cli(['tool', 'list', '--query', 'spawn_light'])
        check('Project tool absent before source import', before['data'] == [])
        fixture = project / 'Assets/Editor/ProjectTools442.cs'
        source = (ROOT / 'tests/fixtures/custom-tools/ProjectTools.cs').read_text()
        fixture.write_text(source)
        schema = reload_and_wait('spawn_light')
        check('AC-1 schema after live compilation', schema['source'] == 'custom'
              and schema['params_schema']['properties']['name']['type'] == 'string'
              and schema['params_schema']['required'] == ['name'])
        listing = cli(['tool', 'list', '--query', 'spawn_light'])
        check('AC-1 list includes schema and source', listing['data'] == [schema])
        names = cli(['tool', 'list', '--query', 'spawn_light', '--names-only'])
        check('Names-only migration output', names['data'] == ['spawn_light'])
        raw('create_scene', {'sceneName': 'CustomTools442', 'path': 'Assets/Scenes/Generated/E2E', 'loadScene': True})
        created = raw('spawn_light', {'name': 'Sun'})
        check('AC-2 custom invocation main thread', created['data']['name'] == 'Sun' and created['data']['mainThread'])
        hierarchy = raw('get_hierarchy')
        check('AC-2 Sun in hierarchy', contains_object(hierarchy['data'], 'Sun'))
        details = raw('get_gameobject_details', {'path': '/Sun', 'includeComponents': True})
        check('AC-2 Sun has Light component', 'Light' in json.dumps(details['data']))
        for params in ({}, {'name': 42}, {'name': 'Invalid', 'intensity': 'bright'}, {'name': 'Invalid', 'extra': True}):
            value = raw('spawn_light', params, 2)
            check('AC-3 CLI INVALID_ARGUMENT', value['errors'][0]['code'] == 'INVALID_ARGUMENT')
            wire = bridge('spawn_light', params)
            transcript.append({'direct_tool': 'spawn_light', 'params': params, 'response': wire})
            check('AC-3 Bridge INVALID_ARGUMENT', wire.get('code') == 'INVALID_ARGUMENT')
        counts = raw('custom_tool_stats')
        check('AC-3 rejected arguments have no side effects', counts['data']['spawnCalls'] == 1)
        dry = cli(['--dry-run', 'raw', 'spawn_light', '--json', '{"name":"DrySun"}'])
        check('Dry-run skips custom mutating method', dry['data'].get('executed') is False)
        counts = raw('custom_tool_stats')
        check('Dry-run leaves invocation count unchanged', counts['data']['spawnCalls'] == 1)
        failure = raw('throw_custom', expected=6)
        check('AC-4 structured exception', failure['errors'][0]['code'] == 'CUSTOM_TOOL_FAILED'
              and 'custom failure 442' in failure['errors'][0]['message'])
        cli(['system', 'ping'])
        check('AC-4 Editor survives exception', process.poll() is None)
        fixture.write_text(source.replace('"spawn_light"', '"spawn_light_v2"'))
        reload_and_wait('spawn_light_v2')
        after = cli(['tool', 'list', '--query', 'spawn_light', '--names-only'])
        check('Domain reload replaces registry without stale entries', after['data'] == ['spawn_light_v2'])
        raw('spawn_light', {'name': 'StaleSun'}, 2)
        matrix.stop_owned(process)
        nunit = subprocess.run([str(editor), '-batchmode', '-nographics', '-projectPath', str(project),
            '-runTests', '-testPlatform', 'EditMode', '-testFilter', 'CustomToolRegistryTests',
            '-testResults', str(out / 'results.xml'), '-logFile', str(out / 'editmode.log')],
            env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=900)
        nunit_result = ET.parse(out / 'results.xml').getroot().attrib
        check('AC-3/5 focused EditMode tests including Console collision warning', nunit.returncode == 0
              and nunit_result.get('result') == 'Passed' and int(nunit_result.get('passed', '0')) >= 21)
        completed = True
    finally:
        matrix.stop_owned(process)
        source_hashes = {str(path.relative_to(project)): hashlib.sha256(path.read_bytes()).hexdigest()
                         for path in (project / 'Packages/unity-cli-bridge/Editor').rglob('*.cs')}
        (out / 'transcript.json').write_text(json.dumps(transcript, indent=2) + '\n')
        (out / 'summary.json').write_text(json.dumps({
            'version': args.version, 'architecture': platform.machine(), 'started_at': started_at,
            'completed_at': datetime.now(timezone.utc).isoformat(), 'passed': completed,
            'checks': checks, 'nunit': nunit_result, 'source_hashes': source_hashes,
            'cli_sha256': hashlib.sha256(args.cli.resolve().read_bytes()).hexdigest(),
            'script_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        }, indent=2) + '\n')


if __name__ == '__main__':
    main()
