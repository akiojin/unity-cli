import os,sys,json,subprocess,hashlib
from pathlib import Path
version=sys.argv[1]
base=Path('/tmp/issue380-harness')/version
out=base/'final2'
out.mkdir(exist_ok=True)
repo=Path('/Users/akiojin/Workbench/unity-cli/work/issue-380')
project=base/'Project'
skill=repo/'.claude-plugin/plugins/unity-cli/skills/unity-audio-setup'
hashes={str(p.relative_to(skill)):hashlib.sha256(p.read_bytes()).hexdigest() for p in skill.rglob('*') if p.is_file()}
(out/'skill-hashes.json').write_text(json.dumps(hashes,indent=2))
prompt=(base/'request.txt').read_text().replace('under '+str(base), 'under '+str(out)).replace(str(base)+'/evidence/',str(out)+'/evidence/')
prompt=prompt.replace('Assets/Audio/Music.mixer','Assets/Audio/Final2.mixer').replace('Audio380.unity','Audio380Final2.unity')
prompt+='''
This is a fresh final-skill verification. The earlier test is complete; do not copy its PASS or results. Invoke Skill again and read the updated SKILL.md. Its final corrections add explicit create_gameobject for a missing source object and headed-Editor focus/runInBackground diagnostics. Follow both. Record and restore any temporary runInBackground setting; use focused Editor for Play verification. Use NEW mixer Assets/Audio/Final2.mixer and NEW scene Assets/Scenes/Generated/E2E/Audio380Final2.unity. Existing tone.wav may be reused, but execute importer modify/readback again. Existing earlier scene is saved; it may be replaced by the new scene. Make the /Music object with create_gameobject and then use the recipe to add/configure source. Copy skill hashes from the final directory into evidence. Record all raw commands/output and measured assertions, including two playback samples and scene reload. Editor has already completed Input System activation/restart. Leave it running in Edit mode. Run no helper that overwrites original evidence. Final evidence must be generated only from this fresh run. You can reuse earlier evaluation code as source but never reuse old output as evidence. No new feature or product edits are authorized. Do not demand a screenshot or audible confirmation; those are not this skill's acceptance criteria.
'''
prompt=prompt.replace('0.3-1 seconds apart', '0.5-3 seconds apart (the 30-second clip allows CLI latency; no strict subsecond requirement)')
(out/'request.txt').write_text(prompt)
env={k:v for k,v in os.environ.items() if not k.startswith('GWT_') and k not in ['CLAUDECODE','CLAUDE_CONFIG_DIR']}
env.update(PATH=str(repo/'target/debug')+os.pathsep+env['PATH'],UNITY_PROJECT_ROOT=str(project),UNITY_CLI_NO_AUTO_UPDATE='1')
cmd=['claude','-p',prompt,'--output-format','stream-json','--verbose','--no-session-persistence',
     '--setting-sources','project','--settings','{"disableAllHooks":true}',
     '--strict-mcp-config','--mcp-config','{"mcpServers":{}}','--no-chrome',
     '--tools','Bash,Read,Write,Edit,Glob,Grep,Skill','--allowedTools','Bash','Read','Write','Edit','Glob','Grep','Skill',
     '--permission-mode','dontAsk','--max-turns','65']
with (out/'claude-stream.jsonl').open('w') as stdout,(out/'claude-stderr.log').open('w') as stderr:
 r=subprocess.run(cmd,cwd=project,env=env,stdout=stdout,stderr=stderr,timeout=1800)
after={str(p.relative_to(skill)):hashlib.sha256(p.read_bytes()).hexdigest() for p in skill.rglob('*') if p.is_file()}
assert after==hashes,'Skill changed during final run'
print(version,'final Claude exit',r.returncode,flush=True)
sys.exit(r.returncode)
