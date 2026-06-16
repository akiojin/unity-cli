# Unity CLI Bridge Test Project

This is the Unity test project for `unity-cli`.

## Purpose

- Validate `UnityCliBridge/Packages/unity-cli-bridge` in a real Unity project.
- Run manual and CI EditMode/PlayMode regression scenarios.

## Package Source

The project uses the local package reference defined in `Packages/manifest.json`:

- `com.akiojin.unity-cli-bridge`: `file:unity-cli-bridge`

## Unity Version Profiles

`Packages/manifest.json` defaults to the Unity 6 profile. Switch profiles before validating a specific editor line:

```bash
./scripts/switch-unity-manifest.sh 6
./scripts/switch-unity-manifest.sh 2022
```

## Open in Unity

Open this folder in Unity Hub:

- `UnityCliBridge`

## Run EditMode Tests (batch)

```bash
unity -batchmode -nographics \
  -projectPath UnityCliBridge \
  -runTests -testPlatform editmode \
  -testResults test-results/editmode.xml
```
