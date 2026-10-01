# Audio authoring and verification

## Preconditions and import

Inspect `get_editor_state`, `get_editor_info` and `read_console`. Pin the same
project/host/port throughout, including eval. Use `tool schema` to confirm payloads
when the installed CLI/bridge differs. Author outside Play; never replace a dirty
scene without saving it or obtaining a scope decision. Use existing assets unless
new paths are requested. Generated test scenes belong under
`Assets/Scenes/Generated/E2E/`.

Copy a licensed WAV/OGG into the selected Assets folder, or use an authorized test
fixture, then `refresh_assets`. Import settings use `action:"get"` or
`action:"modify"`, `assetPath`, and (for modify) `settings`. A short test tone can
use `loadType:"DecompressOnLoad"`, `compressionFormat:"PCM"`; long music can use
`Streaming` with `Vorbis` and `quality` in [0,1]. Do not force mono on stereo music.
Read back default settings and inspect platform overrides for the intended target;
this workflow does not claim every build target uses the default import settings.

## Mixer and saved source

Use the four `manage_audio_mixer` actions in SKILL.md. Parent/group paths are exact
paths such as `Master/Music`, not ambiguous leaf names. Exposed parameters support
`Volume`; do not invent effect parameters. Reuse an existing group/parameter when
the readback matches, and report conflicting names rather than blindly recreating.

For object references, public-API eval can resolve assets and validate nulls before
assignment. This example assumes a dedicated scene and existing `/Music` object.
Run statements with `unity-cli editor eval '<code>' --mode statements --output json`; qualified
types below avoid depending on implicit imports. Adapt paths, not serialization.

```csharp
var go = UnityEngine.GameObject.Find("/Music");
if (go == null) throw new System.Exception("Missing /Music");
var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>("Assets/Audio/tone.wav");
var mixer = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Audio/Music.mixer");
if (clip == null || mixer == null) throw new System.Exception("Missing audio assets");
var groups = mixer.FindMatchingGroups("Master/Music");
if (groups.Length != 1) throw new System.Exception("Expected exactly one Music group");
var source = go.GetComponent<UnityEngine.AudioSource>();
if (source == null) source = UnityEditor.Undo.AddComponent<UnityEngine.AudioSource>(go);
UnityEditor.Undo.RecordObject(source, "Configure audio");
source.clip = clip;
source.outputAudioMixerGroup = groups[0];
source.playOnAwake = false;
source.loop = true;
source.spatialBlend = 0f;
source.volume = 0.25f;
source.mute = false;
source.enabled = true;
UnityEditor.EditorUtility.SetDirty(source);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
UnityEditor.AssetDatabase.SaveAssets();
return new { clip = source.clip.name, group = source.outputAudioMixerGroup.name, source.volume, source.loop };
```

Inspect active/enabled state and ensure exactly one active AudioListener (usually
on the camera). Save via `save_scene`, reload via `load_scene`, then verify clip
and mixer asset paths and group, not just display names. Do not claim persistence
from an unsaved in-memory object. Include importer readback and mixer `get` output.

## Playback evidence

After `play_game` and readiness, run `GameObject.Find("/Music").GetComponent<AudioSource>().Play()`
through eval using qualified types as needed. Read this expression twice, with a
short real-time interval shorter than the clip duration, without calling Play again:

```csharp
var source = UnityEngine.GameObject.Find("/Music").GetComponent<UnityEngine.AudioSource>();
return new { source.isPlaying, source.time, source.timeSamples, source.loop,
    clip = source.clip.name, samples = source.clip.samples,
    group = source.outputAudioMixerGroup.name,
    mixer = UnityEditor.AssetDatabase.GetAssetPath(source.outputAudioMixerGroup.audioMixer),
    source.enabled, source.mute, source.volume, source.spatialBlend,
    active = source.gameObject.activeInHierarchy, paused = UnityEngine.AudioListener.pause };
```

Require `isPlaying:true` in both samples and progressing sample/time positions.
For a looping clip, use positive modular sample delta and avoid intervals spanning
an entire loop. If the clip is too short, use a longer fixture or shorter interval.
Read `GetFloat("MusicVolume", out value)` from the source's mixer. A `SetFloat`
test must check its boolean result and readback, then restore the original value.
This verifies engine state/routing; it does not prove a person heard the sound.

If playback stalls, inspect Editor pause, AudioListener.pause, AudioSettings output
sample rate, source activation/enabled/mute/volume, clip loadState/length and mixer
attenuation. In a headed Editor, focus the target window and check
`Application.runInBackground`, `Time.frameCount` and `AudioSettings.dspTime`:
an unfocused Editor can report `isPlaying:true` while its clock is stalled. Prefer
focusing the window; if changing runInBackground for a test, record and restore
the original value and verify PlayerSettings did not retain a temporary change.
Headless audio may not advance: use a real headed Editor with audio
enabled for playback acceptance. Do not replace this with a mocked success.

Stop Play with `stop_game`, wait for edit mode and recheck persisted source and
importer values. Read console errors. Keep the saved mixer, scene, clip metadata,
raw commands/results and version/skill hashes with the verification report.
