"""Exercise the Cargo build script and the macOS release CI contract (#553)."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

import yaml

ROOT = Path(__file__).resolve().parents[2]


class MacOSRuntimeLink(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.directory = Path(cls.tmp.name)
        suffix = '.exe' if os.name == 'nt' else ''
        cls.build_script = cls.directory / f'build-script{suffix}'
        subprocess.run(['rustc', str(ROOT / 'build.rs'), '-o', str(cls.build_script)],
                       check=True, capture_output=True, text=True)
        # A real executable works with Command on Windows and Unix alike.
        source = cls.directory / 'fake_xcrun.rs'
        source.write_text('''
fn main() {
    assert_eq!(std::env::args().skip(1).collect::<Vec<_>>(),
               ["--sdk", "macosx", "clang", "--print-resource-dir"]);
    if std::env::var_os("FAIL_XCRUN").is_some() {
        eprintln!("selected Xcode toolchain is unavailable");
        std::process::exit(1);
    }
    println!("{}", std::env::var("FAKE_CLANG_RUNTIME").unwrap());
}
''', encoding='utf-8')
        subprocess.run(['rustc', str(source), '-o', str(cls.directory / f'xcrun{suffix}')],
                       check=True, capture_output=True, text=True)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def run_build(self, target_os='macos', arch='aarch64', **overrides):
        resource_dir = self.directory / 'Xcode with spaces'
        runtime = resource_dir / 'lib' / 'darwin' / 'libclang_rt.osx.a'
        runtime.parent.mkdir(parents=True, exist_ok=True)
        runtime.touch()
        env = dict(os.environ, CARGO_MANIFEST_DIR=str(ROOT), OUT_DIR=str(self.directory),
                   CARGO_CFG_TARGET_OS=target_os, CARGO_CFG_TARGET_ARCH=arch,
                   PATH=str(self.directory) + os.pathsep + os.environ['PATH'],
                   FAKE_CLANG_RUNTIME=str(resource_dir))
        env.update(overrides)
        return subprocess.run([str(self.build_script)], env=env, capture_output=True,
                              text=True)

    def test_arm64_links_selected_xcode_runtime_with_spaces_in_path(self):
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('cargo:rustc-link-lib=static=clang_rt.osx', result.stdout)
        self.assertIn(f'cargo:rustc-link-search=native={self.directory / "Xcode with spaces" / "lib" / "darwin"}',
                      result.stdout)
        self.assertIn('cargo:rerun-if-env-changed=DEVELOPER_DIR', result.stdout)
        self.assertIn('cargo:rerun-if-env-changed=SDKROOT', result.stdout)

    def test_intel_and_non_macos_do_not_query_or_link_apple_runtime(self):
        for target_os, arch in [('macos', 'x86_64'), ('linux', 'aarch64'),
                                ('windows', 'x86_64')]:
            with self.subTest(target_os=target_os, arch=arch):
                result = self.run_build(target_os, arch, FAIL_XCRUN='1')
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertNotIn('cargo:rustc-link-', result.stdout)

    def test_missing_archive_fails_with_actionable_diagnostic(self):
        result = self.run_build(FAKE_CLANG_RUNTIME='libclang_rt.osx.a')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('libclang_rt.osx.a', result.stderr)
        self.assertIn('Xcode', result.stderr)

    def test_toolchain_failure_preserves_xcrun_diagnostic(self):
        result = self.run_build(FAIL_XCRUN='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('selected Xcode toolchain is unavailable', result.stderr)


class MacOSReleaseCI(unittest.TestCase):
    def test_required_rust_gate_rejects_missing_or_failed_macos_links(self):
        workflow = yaml.safe_load((ROOT / '.github/workflows/test.yml').read_text(encoding='utf-8'))
        gate = workflow['jobs']['rust-test']
        self.assertEqual(gate['needs'], 'macos-release')
        self.assertEqual(gate['if'], 'always()')
        self.assertNotIn('continue-on-error', gate)
        step = gate['steps'][0]
        self.assertEqual(step['env']['MACOS_RELEASE_RESULT'], '${{ needs.macos-release.result }}')
        self.assertNotIn('if', step)
        self.assertNotIn('continue-on-error', step)
        # Resolve explicitly: Windows process lookup may otherwise pick WSL's
        # bash launcher instead of the Git Bash already selected on PATH.
        bash = shutil.which('bash')
        self.assertIsNotNone(bash)
        for result in ('success', 'failure', 'cancelled', 'skipped', ''):
            with self.subTest(result=result):
                run = subprocess.run([bash, '-lc', step['run']], capture_output=True,
                                     text=True, env=dict(os.environ, MACOS_RELEASE_RESULT=result))
                self.assertEqual(run.returncode == 0, result == 'success', run.stderr)
                self.assertIn('macOS release matrix:', run.stdout)

    def test_prs_build_both_macos_release_targets_without_skipping(self):
        workflow = yaml.safe_load((ROOT / '.github/workflows/test.yml').read_text(encoding='utf-8'))
        triggers = workflow.get('on', workflow.get(True))
        self.assertIn('develop', triggers['pull_request']['branches'])
        self.assertNotIn('paths', triggers['pull_request'])
        job = workflow['jobs']['macos-release']
        self.assertNotIn('if', job)
        self.assertNotIn('continue-on-error', job)
        self.assertFalse(job['strategy']['fail-fast'])
        targets = {entry['target'] for entry in job['strategy']['matrix']['include']}
        self.assertEqual(targets, {'aarch64-apple-darwin', 'x86_64-apple-darwin'})
        build = next(step for step in job['steps'] if step.get('name') == 'Build release binary')
        self.assertEqual(build['run'], 'cargo build --locked --release --target ${{ matrix.target }}')
        self.assertNotIn('if', build)
        self.assertNotIn('continue-on-error', build)

    def test_release_and_pr_ci_use_same_deployment_targets(self):
        matrices = []
        for filename, job in [('release.yml', 'build'), ('test.yml', 'macos-release')]:
            workflow = yaml.safe_load((ROOT / '.github/workflows' / filename).read_text(encoding='utf-8'))
            build_job = workflow['jobs'][job]
            matrix = build_job['strategy']['matrix']['include']
            matrices.append({entry['target']: entry.get('deployment_target')
                             for entry in matrix if entry['target'].endswith('apple-darwin')})
            build = next(step for step in build_job['steps']
                         if step.get('name') == 'Build release binary')
            self.assertEqual(build['env']['MACOSX_DEPLOYMENT_TARGET'],
                             '${{ matrix.deployment_target }}')
        self.assertEqual(matrices[0], matrices[1])
        self.assertEqual(matrices[0], {'aarch64-apple-darwin': '13.4',
                                      'x86_64-apple-darwin': '10.13'})


if __name__ == '__main__':
    unittest.main()
