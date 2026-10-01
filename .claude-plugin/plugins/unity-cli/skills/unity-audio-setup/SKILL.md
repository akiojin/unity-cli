---
name: unity-audio-setup
description: Configure Unity audio with unity-cli. Use when the user asks to connect AudioClip import settings, AudioMixer groups and exposed volume, and AudioSource routing with Play Mode playback verification. Do not use for import settings alone; use `unity-asset-management`. For individual component edits use `unity-gameobject-edit`; for scene-only creation use `unity-scene-create`; for existing runtime tests or capture alone use `unity-playmode-testing`; for console or generic eval alone use `unity-editor-tools`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.0
  category: assets
  triggers:
    - audio
    - audioclip
    - audiomixer
    - audiosource
  siblings:
    - unity-asset-management
    - unity-gameobject-edit
    - unity-scene-create
    - unity-playmode-testing
    - unity-editor-tools
---

# Audio Setup

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Connect persistent clip import, mixer routing and source configuration to measured
Play Mode state. Playback state proves engine playback, not audible speaker output.

## Use When

- Configure an AudioClip, AudioMixer/Group/exposed Volume and AudioSource together.
- Verify that the saved source routes through the intended mixer and plays in Play.
- Diagnose a complete audio setup using import, listener, source and mixer state.

## Do Not Use When

- Only edit import settings or refresh assets: `unity-asset-management`.
- Only change an existing component field: `unity-gameobject-edit`.
- Only create, load or save a scene: `unity-scene-create`.
- Only run runtime tests, enter Play or capture media: `unity-playmode-testing`.
- Only read console messages or run generic eval: `unity-editor-tools`.
- Implement DSP, custom importers or missing bridge operations: track separately.

## Preferred Flow

1. Follow [runtime prerequisites](references/runtime-checklist.md). Pin the project
   and endpoint; record Editor/bridge versions, scene and initial Play state. Stop
   Play before persistent edits. Inspect the existing clip, mixer and source before
   choosing paths; preserve unrelated assets and dirty scenes.
2. Follow [audio authoring and verification](references/audio-workflow.md). Use an
   existing licensed clip or an explicitly authorized generated test tone. Refresh
   assets, read its importer, then modify load type/compression for its use case.
   Read settings back after reimport; do not assume a requested value persisted.
3. Use `manage_audio_mixer` to create/reuse the mixer, add the named group and expose
   its Volume. Inspect the returned full group paths and parameter names before
   retrying mutations. Do not use internal-API reflection or write mixer YAML.
4. Inspect the target scene and object; create a missing source object with
   `create_gameobject` (for example, `{"name":"Music"}`). Assign the imported
   AudioClip and the exact AudioMixerGroup to the AudioSource
   using existing component tools or public-API `editor eval`. Ensure one enabled
   AudioListener, active source, nonzero volume and suitable spatial blend. Save
   assets and scene; reload the saved scene and verify both references persisted.
5. Enter Play with `play_game`, wait for readiness, then explicitly play the source
   if needed. Sample `isPlaying`, `time` and `timeSamples` at two separated times;
   require active playback and progress, accounting for loop wrap. Confirm clip and
   mixer routing at runtime. A successful play command alone is insufficient.
6. Read back the exposed volume through `AudioMixer.GetFloat`; if changing it in
   Play, record the previous value, check `SetFloat`/`GetFloat` and restore it. Read
   console errors. Stop Play, then verify persistent asset/source settings again.
   Never save temporary Play changes as the intended authoring result.
7. Report versions, clip/import settings, mixer/group/parameter, saved scene,
   source settings, both playback samples and console status. Retain tool outputs
   and asset evidence. If capability is missing, link a separate issue instead of
   adding a workaround implementation here; audio authoring tools are owned by
   [#431](https://github.com/akiojin/unity-cli/issues/431).

## Examples

- "AudioClip を取り込み、Mixer 経由で AudioSource を再生して状態を確認して。"
- "Create a Music mixer group and exposed volume, route my clip and verify Play."
- "Only change this clip's compression" → `unity-asset-management`.
- "Only set this AudioSource's volume" → `unity-gameobject-edit`.

Use the same explicit project/host/port on each command:

```bash
unity-cli raw manage_asset_import_settings --json '{"action":"modify","assetPath":"Assets/Audio/tone.wav","settings":{"loadType":"DecompressOnLoad","compressionFormat":"PCM"}}'
unity-cli raw manage_audio_mixer --json '{"action":"create","assetPath":"Assets/Audio/Music.mixer"}'
unity-cli raw manage_audio_mixer --json '{"action":"add_group","assetPath":"Assets/Audio/Music.mixer","parentGroup":"Master","name":"Music"}'
unity-cli raw manage_audio_mixer --json '{"action":"expose_parameter","assetPath":"Assets/Audio/Music.mixer","groupPath":"Master/Music","parameter":"Volume","parameterName":"MusicVolume"}'
unity-cli raw manage_audio_mixer --json '{"action":"get","assetPath":"Assets/Audio/Music.mixer"}'
unity-cli raw play_game --json '{}'
unity-cli raw stop_game --json '{}'
```

## References

- [Runtime checklist](references/runtime-checklist.md): connection and recovery.
- [Audio workflow](references/audio-workflow.md): saved references and measured playback.
