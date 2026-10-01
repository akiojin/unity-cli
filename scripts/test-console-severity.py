#!/usr/bin/env python3
"""Run console regressions in an isolated real Unity Editor; retain NUnit evidence."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    project = Path(tempfile.mkdtemp(prefix='unity-console-426-'))
    source = ROOT / 'UnityCliBridge/Packages/unity-cli-bridge'
    package = project / 'Packages/unity-cli-bridge'
    shutil.copytree(source, package, ignore=shutil.ignore_patterns('Tests', 'Tests.meta'))
    tests = package / 'Tests/Editor'
    tests.mkdir(parents=True)
    for name in ('ConsoleSeverityTests.cs', 'CompilationHandlerTests.cs'):
        shutil.copy2(source / 'Tests/Editor' / name, tests / name)
    (tests / 'Console.Tests.asmdef').write_text(json.dumps({
        'name': 'Console.Tests', 'references': ['UnityCliBridge.Editor'],
        'includePlatforms': ['Editor'], 'optionalUnityReferences': ['TestAssemblies'],
        'overrideReferences': True, 'precompiledReferences': ['nunit.framework.dll', 'Newtonsoft.Json.dll'],
    }))
    (project / 'Assets').mkdir()
    (project / 'ProjectSettings').mkdir()
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + args.version + '\n')
    deps = json.loads((ROOT / 'UnityCliBridge/Packages/manifest.json').read_text())['dependencies']
    deps = {k: v for k, v in deps.items() if k.startswith('com.unity.modules.')}
    legacy = args.version.startswith('2022.')
    if legacy:
        for name in ('accessibility', 'adaptiveperformance', 'vectorgraphics'):
            deps.pop('com.unity.modules.' + name, None)
    deps.update({'com.akiojin.unity-cli-bridge': 'file:unity-cli-bridge',
                 'com.unity.test-framework': '1.1.33' if legacy else '1.6.0',
                 'com.unity.inputsystem': '1.14.2' if legacy else '1.19.0'})
    (project / 'Packages/manifest.json').write_text(json.dumps({
        'dependencies': deps, 'testables': ['com.akiojin.unity-cli-bridge']}, indent=2))
    editor = Path('/Applications/Unity/Hub/Editor') / args.version / 'Unity.app/Contents/MacOS/Unity'
    command = [str(editor), '-batchmode', '-nographics', '-projectPath', str(project),
               '-runTests', '-testPlatform', 'EditMode',
               '-testFilter', 'ConsoleSeverityTests;CompilationHandlerTests',
               '-testResults', str(output / 'results.xml'), '-logFile', str(output / 'editor.log')]
    (output / 'command.json').write_text(json.dumps(command, indent=2))
    print('Running Unity', args.version, 'project:', project, flush=True)
    result = subprocess.run(command, timeout=900)
    xml = output / 'results.xml'
    if not xml.exists():
        raise RuntimeError('Unity did not emit results; see ' + str(output / 'editor.log'))
    root = ET.parse(xml).getroot()
    summary = {k: root.get(k) for k in ('result', 'total', 'passed', 'failed', 'skipped')}
    summary.update(version=args.version, exit_code=result.returncode, project=str(project))
    (output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    print(json.dumps(summary), flush=True)
    for test in root.iter('test-case'):
        if test.get('result') != 'Passed':
            print(test.get('fullname'), test.findtext('failure/message'))
    return 0 if result.returncode == 0 and root.get('result') == 'Passed' else 1


if __name__ == '__main__':
    raise SystemExit(main())
