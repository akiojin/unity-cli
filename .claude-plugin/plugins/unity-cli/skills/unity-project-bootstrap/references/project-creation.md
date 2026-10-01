# Project Creation

## Choose the Editor and template

Use the installed Editor version requested by the user. Check Unity Hub's
Installs list and the Editor executable; an absent version or a licensing
failure is a prerequisite failure, not a reason to substitute another version.
Unity 2022.3 and Unity 6 are supported. On Apple Silicon use the native Editor.

Hub path: Projects → New project → choose the Editor version and template →
set the project name/location → Create project. Choose a compatible 2D, URP or
HDRP template when requested; the plain CLI example below creates a minimal
built-in 3D project and does not select a render-pipeline template.

## Editor CLI path

The destination must be absent or empty. Quote paths, including spaces. Keep
the creation log outside the project and inspect both the exit code and log.
On macOS, an example for an installed Editor is:

```bash
UNITY_EDITOR="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
UNITY_NEW_PROJECT="/absolute/path/to/NewGame"
"$UNITY_EDITOR" -batchmode -quit -createProject "$UNITY_NEW_PROJECT" -logFile /tmp/NewGame-create.log
```

For 2022.3 select its installed executable instead. Windows Hub installs use
`C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe`; Linux Hub installs
use `<install-root>/<version>/Editor/Unity`. Execute it with the host's normal
process permission rules; this skill's `allowed-tools` does not automatically
authorize arbitrary shell commands. Do not invent a `unity-cli project create`
command or hand-write a substitute Unity project structure.

After the Editor process exits successfully, verify the project version and
manifest. Run setup while the Editor is closed, then launch a graphics-enabled
Editor for scene authoring and Game captures. `-batchmode -nographics` is not
the final visual verification environment. If a licensed GUI session is needed,
report the concrete launch failure and preserve the log.

## Evidence and recovery

- Save the creation log and setup / ping JSON. Check the returned project path
  (macOS may normalize `/tmp` to `/private/tmp`) and bridge version, not merely
  that some listener responds.
- Preserve `Packages/manifest.json` and `Packages/packages-lock.json`; verify
  `com.akiojin.unity-cli-bridge` and `com.unity.inputsystem` resolve successfully.
- Save the authored `.unity` scene before entering Play. Verify its actual file
  exists and the hierarchy contains the camera, light and visible starter Cube.
- Preserve the `capture_screenshot` result and the image at its reported path.
  Inspect the Game image; a black frame, "No cameras rendering", or OS fallback
  does not meet the check. Read the console and stop Play after capture.
- On partial failure, preserve the created project and retry the failed phase.
  Do not rerun `-createProject` over it, overwrite its manifest, or kill unrelated
  Unity processes. Do not claim a complete bootstrap from ping alone.

## Sources

These steps are independently authored from the Editor/Hub documentation and
the unity-cli command contracts, without using official plugin skill content.

- [Unity Editor command-line arguments](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html)
- [Create a project in Unity Hub](https://docs.unity.com/en-us/hub/project-create)
