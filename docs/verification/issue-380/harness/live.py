import os, sys, json, shutil, subprocess, time, wave, math, struct, hashlib
from pathlib import Path

repo = Path('/Users/akiojin/Workbench/unity-cli/work/issue-380')
version, port = sys.argv[1:]
out = Path('/tmp/issue380-harness') / version
project = out / 'Project'
out.mkdir(exist_ok=True)
project.mkdir(exist_ok=True)
for d in ['Assets/Audio', 'Packages', 'ProjectSettings', '.claude/skills']:
    (project / d).mkdir(parents=True, exist_ok=True)
bridge = project / 'Packages/unity-cli-bridge'
if not bridge.exists():
    shutil.copytree(repo/'UnityCliBridge/Packages/unity-cli-bridge', bridge)
source = json.loads((repo/'UnityCliBridge/Packages/manifest.json').read_text())
deps = {k:v for k,v in source['dependencies'].items() if k.startswith('com.unity.modules.')}
if version.startswith('2022'):
    for name in ['accessibility','adaptiveperformance','vectorgraphics']:
        deps.pop('com.unity.modules.'+name,None)
deps.update({'com.akiojin.unity-cli-bridge':'file:unity-cli-bridge',
             'com.unity.inputsystem':'1.7.0' if version.startswith('2022') else '1.14.2'})
(project/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+version+'\n')
for skill in (repo/'.claude-plugin/plugins/unity-cli/skills').iterdir():
    if skill.is_dir() and not (project/'.claude/skills'/skill.name).exists():
        (project/'.claude/skills'/skill.name).symlink_to(skill)
with wave.open(str(project/'Assets/Audio/tone.wav'),'wb') as f:
    f.setnchannels(1); f.setsampwidth(2); f.setframerate(44100)
    f.writeframes(b''.join(struct.pack('<h',int(1600*math.sin(2*math.pi*440*i/44100))) for i in range(44100*30)))
cli = repo/'target/debug/unity-cli'
env = {k:v for k,v in os.environ.items() if not k.startswith('GWT_') and k not in ['CLAUDECODE','CLAUDE_CONFIG_DIR']}
env.update(PATH=str(cli.parent)+os.pathsep+env['PATH'], UNITY_CLI_PORT=port,
           UNITY_CLI_PORT_OVERRIDE=port, UNITY_PROJECT_ROOT=str(project), UNITY_CLI_NO_AUTO_UPDATE='1',
           UNITY_CLI_TOOLS_ROOT=str(out/'tools'))
editor = f'/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity'
process = subprocess.Popen([editor,'-projectPath',str(project),'-logFile',str(out/'editor.log')],cwd=project,env=env,
                           stdout=(out/'editor-stdout.log').open('w'),stderr=subprocess.STDOUT)
(out/'editor.pid').write_text(str(process.pid))
print(f'{version}: Editor {process.pid} started',flush=True)
for attempt in range(300):
    if process.poll() is not None: raise RuntimeError('Editor exited')
    r=subprocess.run([str(cli),'system','ping','--port',port,'--timeout-ms','2000','--output','json'],cwd=project,env=env,capture_output=True,text=True)
    if r.returncode==0:
        (out/'ping.json').write_text(r.stdout); break
    time.sleep(2)
else: raise RuntimeError('Editor did not become ready')
print(f'{version}: bridge ready',flush=True)
skill=repo/'.claude-plugin/plugins/unity-cli/skills/unity-audio-setup'
(out/'skill-hashes.json').write_text(json.dumps({str(p.relative_to(skill)):hashlib.sha256(p.read_bytes()).hexdigest() for p in skill.rglob('*') if p.is_file()},indent=2))
prompt=f'''Use /unity-audio-setup via the Skill tool to execute its complete representative workflow in the already-running isolated real headed Unity {version} project {project}. Invoke Skill; if unavailable report failure. Read its references. Existing test fixture Assets/Audio/tone.wav is a 30-second generated tone, authorized for this test. Use the built CLI {cli} and explicit --host 127.0.0.1 --port {port} on EVERY CLI call, with UNITY_PROJECT_ROOT={project}. You may create assets and scenes only inside this test project and evidence only under {out}. No repository changes, git, gwtd, other projects, other Editor processes, or subagents. All shell commands must use bash -lc and project cwd. This is an authorized automated acceptance test, no human confirmation available.

Configure clip DecompressOnLoad/PCM, create Assets/Audio/Music.mixer, add Master/Music group and expose Volume as MusicVolume. Create a dedicated scene Assets/Scenes/Generated/E2E/Audio380.unity with /Music AudioSource and exactly one AudioListener. Assign saved clip/group using public-API eval, nonzero volume 0.25, spatialBlend0, loop=true, playOnAwake=false. Save/reload and measure persistence; record asset paths/GUIDs. Enter Play, explicitly Play source once and measure TWO real samples of isPlaying,time,timeSamples 0.3-1 seconds apart (not mock or generated success); require both playing and time advancing. Measure exposed MusicVolume with GetFloat, SetFloat to -12dB and verify then restore its original value. Inspect console errors, stop Play and recheck saved source/importer/mixer state. Follow skill diagnostics if playback fails. Do not claim audible output. Leave Editor open in Edit mode for parent's independent verification.

Preserve ALL commands/raw outputs, saved mixer/scene/clip metadata and manifest/lock copies under {out}/evidence/. Write result.json containing actual versions, assertions, source/importer/routing readbacks before/after reload, both playback samples, volume readbacks, console errors and failures. PASS only if actual assertions pass. Inspect eval state (must be completed) and value, not just CLI exit status. Do not modify skill files. Report skill recipe errors for parent correction. The CLI was built from current repository including #434; use its binary, not installed older binary. Preserve all evidence of failures/recovery. Finish with concise outcome and evidence paths.'''
(out/'request.txt').write_text(prompt)
cmd=['claude','-p',prompt,'--output-format','stream-json','--verbose','--no-session-persistence',
     '--setting-sources','project','--settings','{"disableAllHooks":true}',
     '--strict-mcp-config','--mcp-config','{"mcpServers":{}}','--no-chrome',
     '--tools','Bash,Read,Write,Edit,Glob,Grep,Skill','--allowedTools','Bash','Read','Write','Edit','Glob','Grep','Skill',
     '--permission-mode','dontAsk','--max-turns','75']
with (out/'claude-stream.jsonl').open('w') as stdout, (out/'claude-stderr.log').open('w') as stderr:
    result=subprocess.run(cmd,cwd=project,env=env,stdout=stdout,stderr=stderr,timeout=1800)
print(f'{version}: Claude exit={result.returncode}, evidence={out}',flush=True)
sys.exit(result.returncode)
