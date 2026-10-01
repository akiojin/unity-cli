# Issue #431 — Audio authoring verification

Launch mode: autonomous (Issue Monitor).
User Verification Result: n/a (autonomous).
Agent Visual Check: n/a (no UI surface).

## Acceptance evidence

| Criterion | Evidence |
| --- | --- |
| AC-1 | `manage_audio_mixer` creates a mixer, adds nested groups, exposes group Volume; reflection is isolated in `AudioMixerHandler.cs`. |
| AC-2 | AudioImporter default sample settings and flags are validated before mutation, saved, reimported and read back. |
| AC-3 | Every successful mixer action returns the group hierarchy and exposed parameter names/GUIDs/Volume group paths. |
| AC-4 | Rust action-specific argument tests and catalog parity; 16 Editor regression cases on each version. |
| AC-5 | Both macOS Editors passed the complete CLI sequence below. Raw transcripts and saved assets are attached in this directory. |
| AC-6 | [Parent SPEC #160 record](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5928347853). |
| AC-7 | PR CI is the remaining delivery gate; PR creation belongs to the PM per the Issue ruling. |

## Executed

- `cargo test --all-targets -- --test-threads=1`: 589 passed, 0 failed.
- `cargo +stable clippy --all-targets -- -D warnings`: passed.
- `cargo fmt --all -- --check`: passed.
- `cargo run -- skills lint --severity error`: 20 skills, 0 violations.
- `bash scripts/e2e-audio-batch-host.sh --unity-version 2022.3.62f3 --port 65431`: passed.
- `bash scripts/e2e-audio-batch-host.sh --unity-version 6000.3.25f1 --port 65432`: passed.

Each Editor used a separate disposable project containing the current embedded
bridge, built-in modules, Input System and Test Framework. Both processes were
stopped after verification. No shared user project was changed.

The live sequence creates `Test.mixer`, adds `Master/Music/Ambient`, exposes
`AmbientVolume`, changes a WAV importer's load type to Streaming, compression
to Vorbis, quality to 0.42 and mono/background flags to true. It reimports the
mixer and checks the hierarchy, exposed name and importer settings again.

The Editor suite also checks duplicate assets/groups/exposed names, duplicate
parameter exposure, missing parents, path traversal, wrong extensions,
recreation after deletion, invalid importer batches without partial flag
changes, and sample-rate readback. `tests.json` contains every test name and
result; `transcript.json` retains CLI input/output and exit codes.

Evidence: [2022.3.62f3](issue-431/2022.3.62f3/results.txt),
[6000.3.25f1](issue-431/6000.3.25f1/results.txt),
[Rust tests](issue-431/rust-tests.txt).
Each version's `source-hashes.json` records the tested source hashes. The
embedded C# files were byte-compared with this checkout before copying evidence.

## RED and fixes

The initial Rust regression failed because the unknown tool accepted `{}`.
Before production edits, all 12 initial Editor cases failed: AudioImporter
reported success without changing settings, and the mixer handler was absent.
After implementation, 11/12 passed. The remaining failure exposed a real path
reuse bug: default `AssetPathToGUID` includes recently deleted assets. Selecting
`OnlyExistingAssets` fixed it, and an explicit recreation test now covers it.
The final expanded suite passed 16/16 in both Editors.

Internal signatures were checked against Unity's
[2022.3 reference](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Editor/Mono/Audio/Mixer/Bindings/AudioMixerController.cs).
The deletion behavior is documented by
[AssetDatabase.AssetPathToGUID](https://docs.unity3d.com/ja/current/ScriptReference/AssetDatabase.AssetPathToGUID.html).
The bridge adapter is an independent implementation; it does not distribute
Unity reference source.

## Scope and limits

Only group Volume exposure is authored. Existing parameters of other types are
listed with their names/GUIDs and null Volume-specific fields. AudioImporter
changes target default sample settings; platform overrides are independent.
This validates editor authoring and persistence, not audible playback or a
Player build. The audio workflow skill itself remains owned by #380.
