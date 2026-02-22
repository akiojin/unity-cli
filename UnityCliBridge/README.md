# Unity CLI Bridge Test Project

This is the Unity test project for `unity-cli`.

## Purpose

- Validate `UnityCliBridge/Packages/unity-cli-bridge` in a real Unity project.
- Run manual and CI EditMode/PlayMode regression scenarios.

## Package Source

The project uses the local package reference defined in `Packages/manifest.json`:

- `com.akiojin.unity-cli-bridge`: `file:unity-cli-bridge`

## Open in Unity

Open this folder in Unity Hub:

- `UnityCliBridge`

## Run EditMode Tests (batch)

```bash
unity -batchmode -nographics \
  -projectPath UnityCliBridge \
  -runTests -testPlatform editmode \
  -testResults test-results/editmode.xml \
  -quit
```
