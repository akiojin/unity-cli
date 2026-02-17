# Unity CLI Bridge Test Project

This is the migrated Unity test project for `unity-cli`.

## Purpose

- Validate `UnityCliBridge/Packages/unity-cli-bridge` in a real Unity project.
- Run manual and CI EditMode/PlayMode regression scenarios.

## Package Source

The project uses the local package reference defined in `Packages/manifest.json`:

- `com.akiojin.unity-cli-bridge`: `file:../../Packages/unity-cli-bridge`

## Open in Unity

Open this folder in Unity Hub:

- `UnityCliBridge/TestProject`

## Run EditMode Tests (batch)

```bash
unity -batchmode -nographics \
  -projectPath UnityCliBridge/TestProject \
  -runTests -testPlatform editmode \
  -testResults test-results/editmode.xml \
  -quit
```
