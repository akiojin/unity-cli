"""Validate retained Windows matrix results and their measured source fingerprint."""
from datetime import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
VERSIONS = ('6000.3.28f1', '6000.6.8f1', '7000.0.0a16')
SUITES = ('compile', 'input', 'timeline', 'vfx', 'eval', 'hot-reload', 'reload', 'all-tools', 'prefab-tests')


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def source_fingerprint():
    paths = subprocess.check_output([
        'git', 'ls-files', '-z', '--', 'src', 'lsp', 'scripts',
        'UnityCliBridge/Assets', 'UnityCliBridge/ProjectSettings',
        'UnityCliBridge/Packages/unity-cli-bridge', 'tests/fixtures', 'Cargo.toml', 'Cargo.lock',
    ], cwd=ROOT).decode('utf-8').split('\0')
    paths = set(filter(None, paths)) | {'scripts/e2e-shell-windows.sh'}
    hashes = {name: hashlib.sha256((ROOT / name).read_bytes().replace(b'\r\n', b'\n')).hexdigest()
              for name in sorted(paths) if (ROOT / name).is_file()}
    digest = hashlib.sha256(json.dumps(hashes, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    return len(hashes), digest


def main():
    metadata = read(HERE / 'metadata.json')
    assert source_fingerprint() == (metadata['source_file_count'], metadata['source_digest_sha256']), 'stale source evidence'
    assert metadata['user_verification_result'] == 'n/a (autonomous)'
    assert metadata['agent_visual_check'] == 'n/a (no UI surface)'
    reset = read(HERE / 'lspd-native-reset.json')
    assert reset['status'] == 'PASS' and reset['exit_code'] == 0
    assert '10054' in reset['connection_error']
    assert reset['subsequent_ping']['ok'] and reset['subsequent_ping']['result']['pong']
    assert reset['stop']['ok']
    for case in read(HERE / 'port-contract.json'):
        assert case['exit_code'] != 0 and case['response']['success'] is False
        assert any('UNITY_CLI_UNITYD_PORT' in error['message']
                   and '1 and 65535' in error['message'] for error in case['response']['errors'])
    matrix = read(HERE / 'matrix.json')
    assert matrix['status'] == 'PASS' and matrix['full_matrix'] is False
    ports = list(matrix['daemon_ports'].values())
    assert len(ports) == 2 and len(set(ports)) == 2 and all(0 < port <= 65535 for port in ports)
    assert sorted(row['version'] for row in matrix['editors']) == sorted(VERSIONS)
    package_version = read(ROOT / 'UnityCliBridge/Packages/unity-cli-bridge/package.json')['version']
    for row in matrix['editors']:
        version = row['version']
        assert row['status'] == 'PASS' and 'error' not in row
        assert datetime.fromisoformat(row['finished_at']) > datetime.fromisoformat(row['started_at'])
        state = row['compilation']
        assert state['isCompiling'] is False and state['isUpdating'] is False and state.get('errorCount', 0) == 0
        ping = row['ping']['versionCheck']
        assert ping['status'] == 'match' and ping['cliVersion'] == ping['bridgeVersion'] == package_version
        assert row['runtime_probe']['state'] == 'completed'
        if row['runtime'] == 'Mono':
            assert row['runtime_probe']['value'] is True
        else:
            assert row['runtime'] == 'CoreCLR' and row['runtime_probe']['value'] is False
            assert row['corelib_probe']['state'] == 'completed'
            assert row['corelib_probe']['value'] == 'System.Private.CoreLib'
        assert tuple(suite['name'] for suite in row['suites']) == SUITES
        for suite in row['suites']:
            assert suite['status'] == 'PASS'
            if suite['name'] != 'compile':
                assert suite['steps'] and all(step['returncode'] == 0 and step['status'] == 'PASS' for step in suite['steps'])
        folder = HERE / version
        logs = {name: (folder / (name + '.txt')).read_text(encoding='utf-8') for name in SUITES[1:]}
        assert 'Input simulation E2E passed.' in logs['input']
        for name, pattern in (
            ('timeline', r'Timeline E2E: passed=(\d+) failed=0'),
            ('vfx', r'VFX E2E: passed=(\d+) failed=0'),
            ('eval', r'Eval E2E: (\d+) passed, 0 failed'),
            ('hot-reload', r'RESULT pass=(\d+) fail=0'),
        ):
            match = re.search(pattern, logs[name])
            assert match and int(match[1]) > 0, (version, name)
        soak = json.loads(re.search(r'^soak (\{.+\})$', logs['eval'], re.MULTILINE)[1])
        assert soak['iterations'] == soak['evaluations'] == 1000
        assert soak['emittedAssemblies'] <= 8 and soak['loadedAssemblies'] <= 16
        assert soak['managedMemoryGrowthBytes'] <= 64 * 1024 * 1024 and soak['storedResults'] <= 256
        sweep = logs['all-tools']
        assert re.search(r'Invocations FAILED:\s+0\b', sweep)
        assert re.search(r'Unique tools failed:\s+0\b', sweep)
        assert re.search(r'lsp_performance_check\s+\.\.\. PASS', sweep)
        called = int(re.search(r'Unique tools called:\s+(\d+)', sweep)[1])
        passed = int(re.search(r'Unique tools passed:\s+(\d+)', sweep)[1])
        assert called == passed and called > 0
        performance = read(folder / 'lsp-performance.json')
        thresholds = dict(get_symbols=2500, find_symbol=10000, find_refs=4000,
                          get_symbols_giga=20000, find_symbol_giga=12000, find_refs_giga=20000)
        assert performance['success'] and performance['failed_cases'] == 0
        assert performance['thresholds_ms'] == thresholds
        assert sorted(case['case'] for case in performance['results']) == sorted(thresholds)
        for case in performance['results']:
            assert case['pass'] and case['runs'] == 5 and case['warmup'] == 1
            assert 0 < case['p95_ms'] <= case['threshold_ms'] == thresholds[case['case']]
        reload = read(folder / 'reload.json')
        assert reload['status'] == 'pass' and len(reload['cases']) == 2
        assert [case['domainReloadEnabled'] for case in reload['cases']] == [True, False]
        for case in reload['cases']:
            assert case['status'] == 'pass' and case['passed'] == 2 and case['failed'] == 0
            assert case['result']['totalTests'] == case['result']['passedTests'] == 2
            assert case['result']['failedTests'] == 0
        for fixture in ('PrefabWorkflowHandlerTests', 'ObjectIdentityTests'):
            tests = ET.parse(folder / (fixture + '.xml')).getroot()
            assert tests.attrib['result'] == 'Passed'
            assert int(tests.attrib['passed']) > 0 and int(tests.attrib['failed']) == 0
            assert int(tests.attrib.get('skipped', 0)) == 0
            assert int(tests.attrib['total']) == int(tests.attrib['passed'])
            if fixture == 'PrefabWorkflowHandlerTests':
                assert any(case.attrib['name'] == 'ListedPropertyIdentityCanBeUsedToRevertTheSameComponent'
                           and case.attrib['result'] == 'Passed' for case in tests.iter('test-case'))
        print(f'PASS {version}: compilation, matching ping, six baseline suites, all-tools ({called} tools)')
    print('PASS source fingerprint: measured Bridge, CLI, LSP, fixtures and suite sources match checkout')


if __name__ == '__main__':
    main()
