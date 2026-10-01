---
name: unity-localization
description: Configure Unity Localization with unity-cli. Use when the user asks to create Locale or String Table assets, bind Localize String Event to UI text, or verify localized text after switching locale in Play Mode. Do not use for package installation alone; use `unity-package-management`. For generic eval use `unity-editor-tools`; for UI interaction alone use `unity-ui-automation`; for capture alone use `unity-playmode-testing`; for generic assets use `unity-asset-management`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.0
  category: ui
  triggers:
    - localization
    - locale
    - string table
  siblings:
    - unity-package-management
    - unity-editor-tools
    - unity-ui-automation
    - unity-playmode-testing
    - unity-asset-management
---

# Localization

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Create persistent localization assets and prove that switching locale changes
the actual UI label, using existing unity-cli tools and public Unity APIs.

## Use When

- Create Localization Settings, Locale assets and String Table Collections.
- Bind a table entry to UI text through Localize String Event.
- Verify English/Japanese or another requested locale pair in Play Mode.

## Do Not Use When

- Only manage UPM dependencies: `unity-package-management`.
- Only evaluate generic C# or inspect console: `unity-editor-tools`.
- Only interact with existing controls: `unity-ui-automation`.
- Only capture media or control Play: `unity-playmode-testing`.
- Only edit ordinary assets: `unity-asset-management`.
- Only create a scene: `unity-scene-create`; only build Addressables content:
  `unity-addressables`. Translation authoring, external translation services and
  localized audio are outside this text-localization workflow.

## Preferred Flow

1. Follow [runtime prerequisites](references/runtime-checklist.md). Pin the
   project/endpoint; record Editor and package versions, active scene, existing
   settings, locales and target label. Stop Play before persistent edits.
2. Follow [construction and verification](references/localization-workflow.md).
   If needed, install an Editor-compatible `com.unity.localization` with
   `package_manager`; wait for reload and idle compilation, then inspect errors.
   Reuse the installed compatible version and existing Addressables settings.
3. With `editor eval` / `eval_csharp`, use public Localization Editor APIs to
   create or reuse settings and requested Locale assets. Register locales and
   create/reuse a String Table Collection. Add the requested key and translations
   to its per-locale tables. Save tables, shared data and settings with
   `AssetDatabase`; do not hand-edit serialized YAML or overwrite unrelated keys.
4. On the requested text object, configure `LocalizeStringEvent.StringReference`
   and a persistent `OnUpdateString` listener to the label's text setter. Use a
   font containing all requested glyphs. Preserve existing listeners and layout.
   Save the scene and assets, reload them, and inspect the binding and entries.
5. Enter Play and wait for `LocalizationSettings.InitializationOperation` to
   finish successfully. Set `SelectedLocale` to the first available locale.
   Poll the actual label with `get_ui_element_state` until it equals the expected
   translation; capture and inspect a Game screenshot. Repeat for the second
   locale with unchanged framing, and then switch back to verify reversibility.
   Never assign the label text directly to make this test pass.
6. Compare UI state and visible glyphs, not merely the locale code, table lookup,
   successful commands or different image hashes. Missing keys, fallback to the
   first language, blank labels or missing Japanese glyphs are failures. Inspect
   console errors and stop Play; restore any temporary editor-preview locale.
7. Report resolved versions, saved assets/scene, key and expected/actual strings,
   binding persistence, both screenshots and console results. Link separately
   tracked tool gaps; do not add CLI/bridge functionality inside this workflow.

## Examples

- "英語・日本語の Locale と String Table を作り、Play で表示を切り替えて確認して。"
- "Bind this label to the greeting entry and verify Japanese text is rendered."
- "Only install com.unity.localization" → `unity-package-management`.

Use the same explicit project and endpoint on every call:

```bash
unity-cli raw package_manager --json '{"action":"list"}'
unity-cli editor eval 'UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation.IsDone' --output json
unity-cli raw eval_csharp --json '{"code":"UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales.GetLocale(\"ja\");","mode":"statements"}'
unity-cli raw get_ui_element_state --json '{"elementPath":"/LocalizationCanvas/Greeting"}'
unity-cli raw capture_screenshot --json '{"captureMode":"game","width":1280,"height":720,"osFallback":false}'
unity-cli raw read_console --json '{"count":50,"logTypes":["Error","Exception"]}'
```

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and reload recovery.
- [localization-workflow.md](references/localization-workflow.md): assets, persistent events and checks.
