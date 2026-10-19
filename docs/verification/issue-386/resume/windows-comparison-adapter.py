import ctypes
from ctypes import wintypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import queue
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from types import SimpleNamespace

root = Path(__file__).resolve().parents[2]
cache = root / '.cache/issue-386'
out = cache / 'resume/official-windows'
project = cache / 'staff-project'
assert project.resolve().is_relative_to(cache.resolve())
sys.path.insert(0, str(root / 'scripts'))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('comparison', root / 'scripts/bench-compare-official.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)
bench = comparison.load_editor_bench()
comparison.load_editor_bench = lambda: bench
bench.READ_PATH = 'Packages/com.akiojin.unity-cli-bridge/Editor/Core/UnityCliBridgeHost.cs'
args = SimpleNamespace(project=project.resolve(), port=6487, unity_cli=str(root / 'target/debug/unity-cli.exe'), official=str(out / 'unity.exe'), timeout=60, iterations=100, warmup=3, focus='frontmost')
env = comparison.benchmark_environment(args.project, args.port)
env['UNITY_CLI_HOME'] = str(out / 'install')
env['PYTHONUTF8'] = '1'
(project / '.unity/perf-owned-project').write_text('Issue386-owned Windows comparison fixture\n', encoding='utf-8')
editor_pid = json.loads((cache / 'resume/staff-editor-open.json').read_text(encoding='utf-8'))['data']['pid']
observed = subprocess.check_output(['powershell', '-NoProfile', '-Command', 'Get-NetTCPConnection -LocalPort 6487 -State Listen | Select-Object -ExpandProperty OwningProcess'], encoding='utf-8').split()
assert set(observed) == {str(editor_pid)}, 'Actual Bridge listener must be the owned Editor'
user = ctypes.WinDLL('user32', use_last_error=True)
user.GetForegroundWindow.restype = wintypes.HWND
user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user.SetForegroundWindow.argtypes = [wintypes.HWND]
user.SwitchToThisWindow.argtypes = [wintypes.HWND, wintypes.BOOL]
user.IsWindowVisible.argtypes = [wintypes.HWND]
user.IsWindow.argtypes = [wintypes.HWND]
user.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
user.ShowWindowAsync.argtypes = [wintypes.HWND, ctypes.c_int]
user.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user.BringWindowToTop.argtypes = [wintypes.HWND]
user.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
user.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT]
original_window = user.GetForegroundWindow()

def pid_for_window(window):
    pid = wintypes.DWORD()
    user.GetWindowThreadProcessId(window, ctypes.byref(pid))
    return pid.value

def foreground_pid():
    return pid_for_window(user.GetForegroundWindow())

callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
windows = []
background_windows = []
@callback_type
def collect(window, parameter):
    if user.IsWindowVisible(window):
        title = ctypes.create_unicode_buffer(512)
        user.GetWindowTextW(window, title, len(title))
        if pid_for_window(window) == editor_pid and 'staff-project' in title.value:
            windows.append(window)
        elif pid_for_window(window) != editor_pid and 'gwt' in title.value.lower():
            background_windows.append(window)
    return True
user.EnumWindows(collect, 0)
assert windows, 'Owned graphical Editor window must exist'
editor_window = windows[0]
if pid_for_window(original_window) == editor_pid and background_windows:
    original_window = background_windows[0]
assert original_window and pid_for_window(original_window) != editor_pid, 'Need a real background window distinct from Editor'

def activate_window(window):
    user.ShowWindowAsync(window, 9)
    user.SwitchToThisWindow(window, True)
    user.SetForegroundWindow(window)

def ensure_focus(focus, pid):
    assert pid == editor_pid
    deadline = time.monotonic() + 5
    while not bench.focus_matches(focus, pid, foreground_pid()):
        if time.monotonic() >= deadline:
            raise RuntimeError('Unable to establish actual Windows ' + focus + ' focus')
        if focus == 'frontmost':
            activate_window(editor_window)
        else:
            user.ShowWindowAsync(editor_window, 6)
        time.sleep(0.05)

bench.ensure_focus = ensure_focus
bench._focus.frontmost_pid = foreground_pid

class WindowsOfficialShell(comparison.OfficialShell):
    def __init__(self, argv, environment, timeout):
        super().__init__(argv, environment, timeout)
        self.lines = queue.Queue()
        def read():
            for line in iter(self.process.stdout.readline, b''):
                self.lines.put(line)
            self.lines.put(None)
        self.reader = threading.Thread(target=read, daemon=True)
        self.reader.start()

    def request(self, argv):
        self.sequence += 1
        identity = str(self.sequence)
        self.process.stdin.write((json.dumps({'id': identity, 'argv': argv}) + '\n').encode())
        self.process.stdin.flush()
        try:
            line = self.lines.get(timeout=self.timeout)
        except queue.Empty:
            raise TimeoutError('Official native Windows NDJSON timed out')
        if line is None:
            raise RuntimeError('Official native Windows NDJSON closed')
        result = json.loads(line)
        if result.get('id') != identity or result.get('exitCode') != 0:
            raise ValueError('Invalid official NDJSON response: ' + str(result))
        envelope = result.get('envelope', {})
        if envelope.get('success') is not True:
            raise ValueError('Official request failed: ' + str(envelope))
        return envelope

comparison.OfficialShell = WindowsOfficialShell
editor = bench.Editor(args, env)
metadata = {'started_at': datetime.now(timezone.utc).isoformat(), 'os': platform.platform(), 'arch': platform.machine(), 'logical_cpus': os.cpu_count(), 'unity': '6000.4.4f1', 'editor_pid': editor_pid, 'iterations': 100, 'warmup': 3, 'unity_cli_build': 'native Windows debug 0.18.1', 'official_cli_version': '1.0.0-beta.11', 'pipeline_version': '0.8.0-exp.1', 'unity_cli_sha256': hashlib.sha256(Path(args.unity_cli).read_bytes()).hexdigest(), 'official_cli_sha256': hashlib.sha256(Path(args.official).read_bytes()).hexdigest(), 'benchmark_sha256': hashlib.sha256((root / 'scripts/bench-compare-official.py').read_bytes()).hexdigest(), 'windows_adapter_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'windows_adapter': 'Original benchmark operations, validation, focus-discard and alternating order; Windows HWND foreground inspection and bounded reader thread replace macOS focus and POSIX select. No platform spoofing.', 'background_condition': 'Owned Editor minimized, because Windows refused activation of the existing GWT background window. This differs from macOS visible-background measurements.', 'host_contention': 'Other project canonical verification was active on this host; results are observations, no Windows budget or prior baseline was enforced.'}
reports = []
try:
    info = editor.setup()
    result = subprocess.run([args.official, 'command', 'editor_status', '--project-path', str(project), '--format', 'json'], cwd=root, env=env, capture_output=True, text=True, encoding='utf-8', timeout=60)
    assert result.returncode == 0, result.stdout + result.stderr
    comparison.check_official('editor_status', result.stdout)
    metadata['coexistence'] = comparison.verify_coexistence(args.project, info, json.loads(result.stdout)['data']['result'])
    metadata['play_settings'] = editor.raw('get_project_settings', {'includeEditor': True})['editor']
    daemon_before = editor.command(['unityd', 'status'])
    assert daemon_before['running'] and daemon_before.get('connections', 0) > 0
    metadata['unityd_pid'] = daemon_before['pid']
    for mode in ['process', 'resident']:
        for focus in ['frontmost', 'background']:
            if mode == 'process':
                previous = json.loads((out / (mode + '-' + focus + '.json')).read_text(encoding='utf-8'))
                assert previous['status'] == 'PASS'
                reports.append({'mode': mode, 'focus': focus, 'status': 'PASS', 'source': 'previous completed run, original conditions preserved'})
                continue
            args.focus = focus
            report = {'mode': mode, 'focus': focus, 'started_at': datetime.now(timezone.utc).isoformat(), 'conditions': metadata}
            try:
                measure = comparison.measure_process if mode == 'process' else comparison.measure_resident
                samples, details = measure(args, env, editor, bench, editor_pid)
                assert editor.command(['unityd', 'status'])['pid'] == daemon_before['pid'], 'Same warm unityd must survive'
                assert all(len(values) == 100 for by_operation in samples.values() for values in by_operation.values())
                report.update(status='PASS', samples_ms=samples, details=details, results={name: {operation: comparison.summarize(values) for operation, values in operations.items()} for name, operations in samples.items()})
            except Exception as error:
                report.update(status='FAIL', error=str(error))
            report['finished_at'] = datetime.now(timezone.utc).isoformat()
            (out / (mode + '-' + focus + '.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
            reports.append({'mode': mode, 'focus': focus, 'status': report['status'], 'error': report.get('error')})
            (out / 'summary.json').write_text(json.dumps({'conditions': metadata, 'runs': reports}, indent=2), encoding='utf-8')
            print(mode, focus, report['status'], report.get('error', ''), flush=True)
            if report['status'] != 'PASS':
                raise RuntimeError(report['error'])
finally:
    try:
        if editor.raw('get_editor_state', {})['state']['isPlaying']:
            bench.transition(editor.raw, False)
        editor.restore_play_focus()
    finally:
        user.ShowWindowAsync(editor_window, 9)
        if user.IsWindow(original_window):
            activate_window(original_window)
