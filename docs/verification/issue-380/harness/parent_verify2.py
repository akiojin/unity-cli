"""Independent live readback of the Claude-authored final audio scene."""
import json, os, subprocess, sys, time, hashlib
from pathlib import Path

repo=Path('/Users/akiojin/Workbench/unity-cli/work/issue-380')
version,port=sys.argv[1:]
base=Path('/tmp/issue380-harness')/version
project=base/'Project'
out=base/'final2'
env={**os.environ,'UNITY_PROJECT_ROOT':str(project),'UNITY_CLI_NO_AUTO_UPDATE':'1'}
transcript=[]
def call(*args):
    cmd=[str(repo/'target/debug/unity-cli'),*args,'--host','127.0.0.1','--port',port,'--output','json','--timeout-ms','30000']
    r=subprocess.run(cmd,cwd=project,env=env,capture_output=True,text=True,timeout=45)
    transcript.append({'command':cmd,'stdout':r.stdout,'stderr':r.stderr,'exit_code':r.returncode})
    (out/'parent-transcript.json').write_text(json.dumps(transcript,indent=2))
    assert r.returncode==0,r.stderr
    data=json.loads(r.stdout)
    assert not data.get('error') and data.get('success') is not False,data
    return data
def raw(tool,params): return call('raw',tool,'--json',json.dumps(params))
def evaluate(code):
    r=call('editor','eval',code,'--mode','statements')
    assert r['state']=='completed' and not r.get('exception'),r
    return r['value']
def wait_play(expected):
    for _ in range(40):
        try:
            if raw('get_editor_state',{})['state']['isPlaying']==expected: return
        except (AssertionError, subprocess.TimeoutExpired):
            pass
        time.sleep(.25)
    raise AssertionError('Play transition did not finish')

scene='Assets/Scenes/Generated/E2E/Audio380Final2.unity'
mixer='Assets/Audio/Final2.mixer'
clip='Assets/Audio/tone.wav'
readback='''var s=UnityEngine.GameObject.Find("/Music").GetComponent<UnityEngine.AudioSource>();
return new { scene=s.gameObject.scene.path, clip=UnityEditor.AssetDatabase.GetAssetPath(s.clip),
 mixer=UnityEditor.AssetDatabase.GetAssetPath(s.outputAudioMixerGroup.audioMixer), group=s.outputAudioMixerGroup.name,
 s.isPlaying,s.time,s.timeSamples,s.loop,s.playOnAwake,s.spatialBlend,s.volume,s.mute,s.enabled,
 active=s.gameObject.activeInHierarchy, instance=s.GetInstanceID(), samples=s.clip.samples,
 listeners=UnityEngine.Object.FindObjectsOfType<UnityEngine.AudioListener>().Length };'''
def assert_source(r):
    assert r['scene']==scene and r['clip']==clip and r['mixer']==mixer and r['group']=='Music',r
    assert r['loop'] and not r['playOnAwake'] and r['spatialBlend']==0 and r['volume']==.25,r
    assert not r['mute'] and r['enabled'] and r['active'] and r['listeners']==1,r

wait_play(False)
before=evaluate(readback);assert_source(before)
raw('load_scene',{'scenePath':scene})
after=evaluate(readback);assert_source(after)
assert before['instance']!=after['instance']
imp=raw('manage_asset_import_settings',{'action':'get','assetPath':clip})
assert imp['settings']['compressionFormat']=='PCM' and imp['settings']['loadType']=='DecompressOnLoad'
mix=raw('manage_audio_mixer',{'action':'get','assetPath':mixer})
assert 'Master/Music' in [g['path'] for g in mix['groups']]
assert any(p['name']=='MusicVolume' and p['groupPath']=='Master/Music' and p['parameter']=='Volume' for p in mix['exposedParameters'])
background=evaluate('return new { app=UnityEngine.Application.runInBackground, player=UnityEditor.PlayerSettings.runInBackground };')
evaluate('UnityEngine.Application.runInBackground=true; return UnityEngine.Application.runInBackground;')
raw('play_game',{});wait_play(True)
try:
    evaluate('UnityEngine.GameObject.Find("/Music").GetComponent<UnityEngine.AudioSource>().Play(); return true;')
    first=evaluate(readback);time.sleep(.5);second=evaluate(readback)
    assert_source(first);assert_source(second)
    assert first['isPlaying'] and second['isPlaying']
    assert 0 < (second['timeSamples']-first['timeSamples']) % first['samples'] < first['samples']
finally:
    raw('stop_game',{});wait_play(False)
    evaluate('UnityEngine.Application.runInBackground='+str(background['app']).lower()+'; UnityEditor.PlayerSettings.runInBackground='+str(background['player']).lower()+'; return true;')
restored=evaluate('return new { app=UnityEngine.Application.runInBackground, player=UnityEditor.PlayerSettings.runInBackground };')
assert restored==background
final=evaluate(readback);assert_source(final);assert not final['isPlaying']
console=raw('read_console',{'logTypes':['Error','Exception'],'count':100})
assert console['count']==0,console
files=[scene,scene+'.meta',mixer,mixer+'.meta',clip+'.meta']
assets={name:hashlib.sha256((project/name).read_bytes()).hexdigest() for name in files}
result={'status':'PASS','version':version,'before_reload':before,'after_reload':after,
        'importer':imp,'mixer':mix,'background_before':background,'background_restored':restored,'playback':[first,second],'after_stop':final,'console':console,'asset_sha256':assets}
(out/'parent-verification.json').write_text(json.dumps(result,indent=2))
print(version,'independent PASS',first['time'],second['time'],flush=True)
