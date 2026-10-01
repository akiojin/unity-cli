#!/usr/bin/env bash
# CLI-through-bridge VFX acceptance against a real Editor listener.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export VFX_E2E_REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
python3 - "$@" <<'PY'
import argparse,json,os,pathlib,re,subprocess,sys,time,uuid
root=pathlib.Path(os.environ['VFX_E2E_REPO_ROOT'])
p=argparse.ArgumentParser()
p.add_argument('--port',default='6484')
p.add_argument('--unity-cli',default=str(root/'target/debug/unity-cli'))
p.add_argument('--project',default=str(root/'UnityCliBridge'))
p.add_argument('--artifacts',required=True)
p.add_argument('--without-vfx',action='store_true')
args=p.parse_args()
artifacts=pathlib.Path(args.artifacts)
artifacts.mkdir(parents=True,exist_ok=True)
passed=0
sequence=0
play_requested=False

def check(value,message):
    global passed
    assert value,message
    passed+=1
    print('PASS',message,flush=True)

def call(tool,payload,expected_code=None,timeout_ms=120000):
    global sequence
    sequence+=1
    proc=subprocess.run([args.unity_cli,'raw',tool,'--json',json.dumps(payload),
        '--host','127.0.0.1','--port',args.port,'--timeout-ms',str(timeout_ms),'--output','json'],
        text=True,capture_output=True,timeout=timeout_ms/1000+5,
        env={**os.environ,'UNITY_PROJECT_ROOT':args.project})
    (artifacts/f'{sequence:03}-{tool}.json').write_text(proc.stdout)
    (artifacts/f'{sequence:03}-{tool}.stderr').write_text(proc.stderr)
    data=json.loads(proc.stdout)["data"]
    if expected_code:
        def errors(value):
            if isinstance(value,dict):
                if value.get('code')==expected_code:
                    return True
                return any(errors(v) for v in value.values())
            if isinstance(value,list):
                return any(errors(v) for v in value)
            return False
        check(errors(data),f'{tool}: structured {expected_code} response: {data}')
        return data
    check(proc.returncode==0 and not data.get('error') and data.get('success') is not False,
          f'{tool}: successful response ({proc.stderr})')
    compile_result=data.get('compile')
    if compile_result and not compile_result.get('deferred'):
        check(compile_result.get('success') is True,f'{tool}: graph compilation succeeds')
    return data

def clean_console():
    result=call('read_console',{'logTypes':['All'],'count':10000})
    check(isinstance(result.get('logs'),list),'Console response includes structured log entries')
    # Unity 6000.4 changed native console mode bits. The bridge currently labels
    # compiler warnings as Exception and Debug.LogWarning as Error. Match only
    # positive warning evidence; preserve every unrecognized error entry.
    def proven_warning(entry):
        message=entry.get('message','')
        return bool(re.match(r'^.+\(\d+,\d+\): warning (?:CS|UAC|UAL)\d+:',message)
                    or re.match(r'^[^\n]*\nUnityEngine.Debug:LogWarning \(object\)\n',message))
    def is_error(entry):
        message=entry.get('message','')
        return (entry.get('logType') in ('Error','Assert','Exception')
                or bool(re.match(r'^.+\(\d+,\d+\): error CS\d+:',message))
                or bool(re.search(r'\nUnityEngine.Debug:Log(?:Error|Exception) ',message)))
    errors=[entry for entry in result.get('logs',[]) if is_error(entry) and not proven_warning(entry)]
    warnings=sum(proven_warning(entry) for entry in result.get('logs',[]))
    if warnings:
        print(f'Console native-mode compatibility: {warnings} proven warnings retained in evidence',flush=True)
    check(not errors,f'Console contains no Error/Assert/Exception logs: {errors}')

def compilation():
    state=call('get_compilation_state',{})
    check(state.get('errorCount')==0,'C# compilation errorCount is zero')

def wait_playing(expected):
    deadline=time.monotonic()+90
    last=None
    while time.monotonic()<deadline:
        try:
            last=call('get_editor_state',{},timeout_ms=2000).get('state',{})
            if last.get('isPlaying') is expected and not last.get('isCompiling') and not last.get('isUpdating'):
                check(True,f'Editor reached isPlaying={expected}')
                return
        except (AssertionError,ValueError,subprocess.TimeoutExpired) as exc:
            # Domain reload may temporarily disconnect the listener.
            last=repr(exc)
        time.sleep(1)
    raise AssertionError(f'Editor did not reach isPlaying={expected}: {last}')

def run():
    global play_requested
    info=call('get_editor_info',{})
    print('Editor:',json.dumps(info),flush=True)
    version_file=pathlib.Path(args.project)/'ProjectSettings/ProjectVersion.txt'
    expected_version=re.search(r'^m_EditorVersion: (.+)$',version_file.read_text(),re.MULTILINE).group(1)
    check(info['unity']['unityVersion']==expected_version,f'Unity version is {expected_version}')
    compilation()
    # Inspect startup failures too; clearing the Console would hide import errors.
    clean_console()
    if args.without_vfx:
        calls={
            'vfx_apply':{'op':'compile','assetPath':'Assets/Absent.vfx'},
            'vfx_describe_graph':{'assetPath':'Assets/Absent.vfx'},
            'vfx_list_library':{},
            'vfx_runtime':{'op':'get_state','gameObject':'Absent'},
            'vfx_settings':{'op':'get'},
            'vfx_bake_sdf':{'meshPath':'Assets/Absent.obj','outputPath':'Assets/Absent.asset'},
        }
        for tool,payload in calls.items():
            call(tool,payload,'VFX_PACKAGE_MISSING')
        clean_console()
        compilation()
        return
    token=uuid.uuid4().hex[:12]
    generated='Assets/Scenes/Generated/E2E/Vfx'
    asset=f'{generated}/E2E-{token}.vfx'
    sdf=f'{generated}/E2E-{token}-SDF.asset'
    scene=f'{generated}/E2E-{token}.unity'
    rig=f'VfxE2E-{token}'
    initial_state=call('get_editor_state',{}).get('state',{})
    check(initial_state.get('isPlaying') is False,'Editor starts outside PlayMode')
    initial_scene=call('get_scene_info',{'includeGameObjects':False})
    check(initial_scene.get('isDirty') is False,'Active scene has no unsaved changes')
    # create_scene creates missing directories, then saves a dedicated generated scene.
    call('create_scene',{'sceneName':f'E2E-{token}','path':generated,
                         'loadScene':True,'addToBuildSettings':False})
    def apply(op,**params):
        return call('vfx_apply',{'op':op,'assetPath':asset,**params})
    templates=call('vfx_list_library',{'kind':'template'})
    names={item['name'] for item in templates['items']}
    template=next((name for name in ('01_Minimal_System','Minimal_System','SimpleParticleSystem') if name in names),None)
    check(template is not None,'installed VFX package exposes a particle-system template')
    created=apply('create_from_template',template=template,targetPath=asset)
    check(created.get('assetType')=='VisualEffectAsset','template creates VisualEffectAsset')
    before=call('vfx_describe_graph',{'assetPath':asset})
    apply('add_context',contextName='Spawn')
    block=apply('add_block',contextType='Spawner',blockName='Constant Spawn Rate')
    parameter=apply('add_parameter',parameterName='E2ERate',type='Float',value=12.5,exposed=True)
    apply('link_slots',**{
        'from':{'node':'parameter','parameterIndex':parameter['parameterIndex'],'slot':0},
        'to':{'node':'block','contextIndex':block['contextIndex'],
              'blockIndex':block['blockIndex'],'slot':0}})
    compiled=apply('compile')
    check(compiled.get('compile',{}).get('success') is True,'explicit graph compile has successful summary')
    after=call('vfx_describe_graph',{'assetPath':asset})
    check(after.get('compile',{}).get('success') is True,'describe confirms successful graph compile')
    check(after['contextCount']==before['contextCount']+1,'describe confirms added context')
    check(after['parameterCount']==before['parameterCount']+1,'describe confirms added parameter')
    check(any(p.get('exposedName')=='E2ERate' for p in after['parameters']),
          'describe confirms exposed parameter name')
    ctx=after['contexts'][block['contextIndex']]
    check(len(ctx['blocks'])>block['blockIndex'],'describe confirms added block')
    check(not [e for e in after['errors'] if e.get('type')=='Error'],'VFX graph error count is zero')
    call('create_gameobject',{'name':rig})
    call('add_component',{'gameObjectPath':'/'+rig,'componentType':'UnityEngine.VFX.VisualEffect'})
    def runtime(op,**params):
        return call('vfx_runtime',{'op':op,'gameObject':rig,**params})
    runtime('set_asset',assetPath=asset)
    call('save_scene',{'scenePath':scene})
    check((pathlib.Path(args.project)/scene).is_file(),'Runtime rig saved in generated test scene')
    play_requested=True
    call('play_game',{})
    wait_playing(True)
    runtime('send_event',eventName='OnPlay')
    runtime('set_float',name='E2ERate',value=7.5)
    state=runtime('get_state',name='E2ERate')
    check(state.get('hasFloat') is True and abs(state['floatValue']-7.5)<0.001,
          'PlayMode parameter value round-trips')
    runtime('send_event',eventName='E2ECustomEvent',attributes={'lifetime':2.0})
    runtime('simulate',deltaTime=0.05,steps=3)
    call('stop_game',{})
    wait_playing(False)
    play_requested=False
    dependencies=json.loads((pathlib.Path(args.project)/'Packages/packages-lock.json').read_text())['dependencies']
    legacy_sdf=dependencies['com.unity.visualeffectgraph']['version'].startswith('14.')
    if legacy_sdf:
        call('vfx_bake_sdf',{'meshPath':'Assets/VfxFixtures/Cube.obj','outputPath':sdf,'maxResolution':16},
             'VFX_SDF_EDIT_MODE_UNSUPPORTED')
        check(not (pathlib.Path(args.project)/sdf).exists(),'unsupported EditMode bake creates no asset')
        play_requested=True
        call('play_game',{})
        wait_playing(True)
    baked=call('vfx_bake_sdf',{'meshPath':'Assets/VfxFixtures/Cube.obj',
        'outputPath':sdf,'maxResolution':16})
    check(bool(baked.get('guid')) and all(n>0 for n in baked['resolution']),
          'SDF bake returns asset GUID and nonzero resolution')
    if legacy_sdf:
        call('stop_game',{})
        wait_playing(False)
        play_requested=False
    metadata=call('manage_asset_database',{'action':'get_asset_info','assetPath':sdf})
    check('Texture3D' in json.dumps(metadata),'SDF asset is a Texture3D')
    check((pathlib.Path(args.project)/sdf).is_file(),'SDF asset exists on disk')
    compilation()
    clean_console()
    (artifacts/'generated-assets.json').write_text(json.dumps({
        'project':args.project,'scene':scene,'vfx':asset,'sdf':sdf,'runtimeGameObject':rig},indent=2))
    print('Generated assets:',asset,sdf,flush=True)

try:
    run()
except Exception as exc:
    print('FAIL:',repr(exc),file=sys.stderr)
    print(f'VFX E2E: passed={passed} failed=1',flush=True)
    sys.exit(1)
finally:
    if play_requested:
        try:
            call('stop_game',{})
            wait_playing(False)
        except Exception as cleanup_error:
            print('PlayMode cleanup failed:',repr(cleanup_error),file=sys.stderr)
print(f'VFX E2E: passed={passed} failed=0',flush=True)
PY
