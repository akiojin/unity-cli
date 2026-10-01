#!/usr/bin/env python3
"""Exercise authentication against an isolated real Editor; never record tokens."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import socket
import struct
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('matrix', ROOT / 'scripts/e2e-matrix.py')
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)


def request(port, command):
    with socket.create_connection(('127.0.0.1', port), timeout=20) as sock:
        return request_on_socket(sock, command)


def request_on_socket(sock, command):
    def read_exact(sock, size):
        data = b''
        while len(data) < size:
            chunk = sock.recv(size - len(data))
            if not chunk:
                raise RuntimeError('unexpected EOF')
            data += chunk
        return data
    data = json.dumps(command).encode() if isinstance(command, dict) else command.encode()
    sock.sendall(struct.pack('>I', len(data)) + data)
    size = struct.unpack('>I', read_exact(sock, 4))[0]
    return json.loads(read_exact(sock, size))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--cli', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--port', type=int, default=6540)
    parser.add_argument('--opt-out', action='store_true')
    parser.add_argument('--editmode', action='store_true', help='Also run authentication and connection NUnit tests')
    args = parser.parse_args()
    started_at = datetime.now(timezone.utc).isoformat()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    editor = Path(f'/Applications/Unity/Hub/Editor/{args.version}/Unity.app/Contents/MacOS/Unity')
    _, project, _ = matrix.prepare(editor, out)
    env = dict(os.environ, UNITY_CLI_EDITORS_DIR=str(out / 'editors'),
               UNITY_CLI_TOOLS_ROOT=str(out / 'tools'), UNITY_CLI_CACHE_ROOT=str(out / 'cache'),
               UNITY_CLI_REGISTRY_PATH=str(out / 'instances.json'),
               UNITY_PROJECT_ROOT=str(project), UNITY_CLI_PORT_OVERRIDE=str(args.port),
               UNITY_CLI_HOST='127.0.0.1', UNITY_CLI_PORT=str(args.port),
               UNITY_CLI_ALLOW_BATCH_HOST='1',
               UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(out / 'stop'))
    env.pop('UNITY_CLI_ALLOW_UNAUTHENTICATED', None)
    env.pop('UNITY_CLI_AUTH_TOKEN_FILE', None)
    if args.opt_out:
        env['UNITY_CLI_ALLOW_UNAUTHENTICATED'] = '1'
    process = subprocess.Popen([str(editor), '-batchmode', '-projectPath', str(project),
        '-executeMethod', 'UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run',
        '-logFile', str(out / 'editor.log')], env=env,
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    checks = []
    completed = False
    def check(name, condition):
        checks.append({'name': name, 'passed': bool(condition)})
        print(name, 'PASS' if condition else 'FAIL', flush=True)
        if not condition:
            raise AssertionError(name)
    try:
        token = None
        lockfile = out / 'editors' / f'{process.pid}.json'
        deadline = time.monotonic() + 900
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError('Editor exited; see editor.log')
            try:
                token = json.loads(lockfile.read_text()).get('authToken')
                state = request(args.port, {'id': 'ready', 'type': 'get_editor_state',
                    'authToken': token, 'params': {}})
                state = state.get('result', state.get('data', state))
                state = state.get('state', state)
                if matrix.ready(state):
                    break
            except (OSError, ValueError):
                pass
            time.sleep(2)
        else:
            raise RuntimeError('Editor readiness timeout')
        marker = 'UnauthorizedEvalMustNotExist440'
        for supplied in (None, 'invalid-token'):
            for tool, params in [('ping', {}), ('eval_csharp', {
                    'code': f'new UnityEngine.GameObject("{marker}")', 'mode': 'expression'})]:
                command = {'id': 'unauthorized', 'type': tool, 'params': params}
                if supplied is not None:
                    command['authToken'] = supplied
                response = request(args.port, command)
                allowed = args.opt_out and supplied is None
                check(f'{tool} token={"missing" if supplied is None else "wrong"}',
                      response.get('status') == 'success' if allowed else response.get('code') == 'UNAUTHORIZED')
        response = request(args.port, 'ping')
        check('legacy raw ping', response.get('code') == 'UNAUTHORIZED' if not args.opt_out
              else response.get('status') != 'error')
        if not args.opt_out:
            with socket.create_connection(('127.0.0.1', args.port), timeout=20) as sock:
                response = request_on_socket(sock, {'id': 'authenticated', 'type': 'ping',
                    'authToken': token, 'params': {}})
                check('persistent socket authenticated ping', response.get('status') == 'success')
                response = request_on_socket(sock, {'id': 'missing-next-token', 'type': 'eval_csharp',
                    'params': {'code': f'new UnityEngine.GameObject("{marker}")', 'mode': 'expression'}})
                check('each request requires its own token', response.get('code') == 'UNAUTHORIZED')
        hierarchy = request(args.port, {'id': 'hierarchy', 'type': 'get_hierarchy',
                                      'authToken': token, 'params': {}})
        check('eval side effect absent' if not args.opt_out else 'opt-out eval executed',
              (marker in json.dumps(hierarchy)) == args.opt_out)
        check('POSIX token file mode 0600', lockfile.stat().st_mode & 0o777 == 0o600)
        check('256-bit token', isinstance(token, str) and len(token) >= 43)
        cli = [str(args.cli.resolve()), '--host', '127.0.0.1', '--port', str(args.port), '--output', 'json']
        for command in [('system', 'ping'), ('editor', 'eval', '1+2')]:
            result = subprocess.run(cli + list(command), env=env, capture_output=True, text=True, timeout=40)
            check('CLI ' + ' '.join(command), result.returncode == 0)
            if command[0] == 'editor':
                check('CLI eval result is 3', json.loads(result.stdout).get('value') == 3)
            if args.opt_out:
                check('CLI next minor deprecation warning', 'next minor' in result.stderr)
        # A separate daemon process must resolve the same lockfile and authenticate.
        subprocess.run(cli + ['unityd', 'start'], env=env, check=True, capture_output=True, timeout=40)
        result = subprocess.run(cli + ['system', 'ping'], env=env, capture_output=True, timeout=40)
        check('unityd authenticated ping', result.returncode == 0)
        if args.editmode:
            matrix.stop_owned(process)
            result = subprocess.run([str(editor), '-batchmode', '-nographics', '-projectPath', str(project),
                '-runTests', '-testPlatform', 'EditMode', '-testFilter',
                'BridgeAuthenticationTests;EditorLockfileTests;UnityCliBridgeHostConnectionTests',
                '-testResults', str(out / 'results.xml'), '-logFile', str(out / 'editmode.log')],
                env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=900)
            nunit = ET.parse(out / 'results.xml').getroot()
            check('EditMode authentication and connection tests', result.returncode == 0
                  and nunit.get('result') == 'Passed' and int(nunit.get('passed', '0')) >= 32)
        completed = True
    finally:
        subprocess.run([str(args.cli.resolve()), 'unityd', 'stop'], env=env,
                       capture_output=True, timeout=30)
        matrix.stop_owned(process)
        sources = {str(p.relative_to(project)): hashlib.sha256(p.read_bytes()).hexdigest()
                   for p in (project / 'Packages/unity-cli-bridge/Editor').rglob('*.cs')}
        (out / 'summary.json').write_text(json.dumps({'version': args.version,
            'status': 'PASS' if completed else 'FAIL', 'started_at': started_at,
            'opt_out': args.opt_out, 'checks': checks, 'bridge_sources': sources,
            'cli_sha256': hashlib.sha256(args.cli.read_bytes()).hexdigest()}, indent=2) + '\n')


if __name__ == '__main__':
    main()
