# Localization construction and verification

Use public APIs from the installed package, through `unity-cli editor eval`.
Keep calls synchronous and short; query async completion in subsequent calls.
Inspect `unity-cli tool schema eval_csharp` and `get_ui_element_state` before
constructing payloads. A transport success containing a tool error is failure.

## Package and assets

Inspect package metadata before choosing a version. The Unity 2022.3 manual lists
Localization 1.4.4; the registry can recommend a later compatible version.
Do not assume a Unity 6 package works on an older Editor.
Use `package_manager install`, then wait for ping, `isCompiling:false`,
`isUpdating:false`, resolved package version and console checks.

Choose a scoped folder, such as `Assets/Localization/Demo`. In repository E2E,
use `Assets/Scenes/Generated/E2E/Localization` for generated scenes/assets.
Inspect existing settings before creating any asset. Use
`LocalizationEditorSettings.ActiveLocalizationSettings` to select an existing
settings asset, or persist a new `LocalizationSettings` asset and assign it.
Do not replace an existing project's startup selectors or Addressables groups.

For each requested code, use `Locale.CreateLocale("en")` / `("ja")` only when
that locale is absent, save it with `AssetDatabase.CreateAsset`, then register
it with `LocalizationEditorSettings.AddLocale`. Verify both are available.

Use `LocalizationEditorSettings.GetStringTableCollection(name)` to detect an
existing collection. Otherwise call `CreateStringTableCollection(name, folder,
locales)`. This handles shared data and Addressables registration; creating loose
`StringTable` objects does not establish the complete collection. Use
`collection.GetTable(locale.Identifier)`, add a missing locale table when needed,
and call `StringTable.AddEntry(key, translation)`. Mark changed tables and
`SharedData` dirty, then `AssetDatabase.SaveAssets`. Read back matching shared key
IDs and each exact translation. Example values: key `greeting`, English
`Hello, world!`, Japanese `こんにちは、世界！`.

## Persistent UI binding

Use the requested UGUI Text or TextMeshPro label. Assign its compatible font
asset with Japanese glyph coverage before judging the translation. A correct
`text` property with square replacement glyphs is not a successful visual check.

Add/reuse `UnityEngine.Localization.Components.LocalizeStringEvent`. Configure
`StringReference.TableReference` and `TableEntryReference` with the persisted
collection and key. Wire its `OnUpdateString` to the text property setter using
`UnityEditor.Events.UnityEventTools.AddPersistentListener<string>` and a
`UnityEngine.Events.UnityAction<string>` targeting that setter. A temporary
`AddListener` lambda disappears on scene reload. Do not remove other listeners
or add a duplicate when retrying. Mark the component/scene dirty and save.

Reload the scene in Edit Mode. Read the persistent event target/method, table
and key, and saved translations before entering Play. Do not replace a dirty
user scene without preserving its changes.

## Runtime proof

1. Enter Play. Wait for initialization completion and successful operation status;
   if it fails, inspect missing settings, locales and Addressables diagnostics.
   Do not block the main thread with a sleep or an async wait inside eval.
2. Set `LocalizationSettings.SelectedLocale` using
   `AvailableLocales.GetLocale("en")`; fail if it returns null.
3. Poll `get_ui_element_state` for the specific label until its actual `text`
   equals `Hello, world!`. Use a bounded deadline and allow frames between polls.
4. Capture Game view with `osFallback:false`; inspect the returned image.
5. Set the selected locale to `ja`, wait for `こんにちは、世界！`, and capture
   with identical camera/resolution. Inspect that the text is visible and legible.
6. Switch back to `en` and verify the original label text. Read console errors,
   stop Play and verify the saved assets and persistent event again.

Do not use direct text assignment, editor preview alone or a String Database
lookup as proof of the live binding. On `GAME_VIEW_NOT_FOCUSED`, focus the Game
view through `unity-editor-tools` and retry. If the Editor cannot take foreground
focus, or a Game capture omits overlay UI, retain the failure (#422) and use the
actual Game-frame fallback. Through `editor eval`,
open the Game view with `EditorApplication.ExecuteMenuItem("Window/General/Game")`
and call `UnityEngine.ScreenCapture.CaptureScreenshot(absolutePngPath)` in Play.
Wait for a rendered frame and the file to finish writing before reading it.
Keep Game resolution fixed for both locales and inspect the resulting images;
this captures actual Game UI, not a constructed image or OS-window fallback.
Persist image paths, label state, versions,
saved Locale/table assets and the tool transcript as evidence.

## API references and authorship

This workflow is independently authored from the repository tool contract and
Unity's public package APIs, without official unity-agent-plugin skill content.

- [Localization Editor APIs](https://docs.unity3d.com/Packages/com.unity.localization@1.4/api/UnityEditor.Localization.LocalizationEditorSettings.html)
- [Localize String Event](https://docs.unity3d.com/Packages/com.unity.localization@1.4/api/UnityEngine.Localization.Components.LocalizeStringEvent.html)
- [Unity 2022.3 package compatibility](https://docs.unity3d.com/2022.3/Documentation/Manual/com.unity.localization.html)
