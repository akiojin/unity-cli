---
name: unity-urp-setup
description: Set up URP and verify post-processing with unity-cli. Use when the user asks to install Universal Render Pipeline, assign its pipeline and renderer, configure a Global Volume with Bloom or Color Adjustments, or compare the rendered result before and after. Do not use for material or import edits alone; use `unity-asset-management`. For package management alone use `unity-package-management`; for generic settings or eval use `unity-editor-tools`; for capture alone use `unity-playmode-testing`; for scene-only creation use `unity-scene-create`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.0
  category: editor
  triggers:
    - urp
    - bloom
    - post-processing
  siblings:
    - unity-editor-tools
    - unity-asset-management
    - unity-playmode-testing
    - unity-scene-create
---

# URP Setup

Connect URP installation, persistent pipeline/Volume assets, and a rendered
before/after check using existing unity-cli tools.

## Use When

- Install Universal Render Pipeline and assign its pipeline and renderer assets.
- Configure a Global Volume, Bloom or Color Adjustments and verify the result.
- Diagnose why URP post-processing is invisible using Camera, Volume and pipeline
  state together with a Game-view capture.

## Do Not Use When

- Only create or save a scene: `unity-scene-create`.
- Only edit a material, texture import or animation: `unity-asset-management`.
- Only list, install, update or remove packages: `unity-package-management`.
- Only change generic settings or evaluate C#: `unity-editor-tools`.
- Only capture an existing game or run runtime tests: `unity-playmode-testing`.
- Author shaders, Renderer Features, RenderGraph passes, or perform a project-wide
  Built-in material migration: these need a separately scoped implementation.

## Preferred Flow

1. Follow [runtime prerequisites](references/runtime-checklist.md). Pin the
   project/endpoint, confirm ping and read Editor state. Record the Unity version,
   installed URP version, active scene, Graphics default pipeline, each Quality
   override and target Camera. Stop Play before persistent asset edits.
2. Read [setup and verification](references/urp-workflow.md). Reuse compatible
   URP assets when present. Otherwise install an Editor-compatible
   `com.unity.render-pipelines.universal` through `package_manager`; wait for
   package resolution and compilation, then recheck ping, package list and console.
   Do not downgrade an existing pipeline or switch from HDRP without a scoped
   migration decision. Installing the package alone does not enable URP.
3. Use Editor operations / `editor eval` to create and save the Renderer and URP
   Asset, and assign Graphics plus the intended Quality levels. Report which
   levels changed; preserve unrelated overrides. Inspect the effective pipeline
   after assignment. Use `AssetDatabase` APIs rather than serialized YAML edits.
4. Create or reuse a Global Volume and saved Volume Profile. Persist Bloom / Color
   Adjustments as profile subassets, enable their overrides, assign `sharedProfile`,
   and enable the target Camera's post-processing and matching volume layer mask.
   Save assets and scene. Never mistake a Play-mode profile clone for saved content.
5. With the same Camera, framing, resolution and scene, capture the effect disabled
   and enabled. Use a bright subject for Bloom; an empty or dark scene cannot prove
   it works. Enter Play, allow the renderer to produce frames, then request
   `capture_screenshot` with `captureMode:"game"` and `osFallback:false`.
   Compare the returned images, not just successful command exits or file hashes.
6. Read back the effective pipeline, renderer, Camera flags, Volume weight/layers
   and profile values. Inspect console errors. If there is no visible difference,
   follow the reference diagnostics; do not declare success. Stop Play, restore the
   intended effect value and save. Reload the scene/profile and confirm persistence.
7. Report Unity/URP versions, affected Quality levels, saved asset/scene paths,
   before/after image paths, observed visual difference and console status. If a
   required tool capability is absent, report and link a separately tracked issue;
   do not implement new CLI/bridge tools inside this workflow.

## Examples

- "URP を導入して Global Volume の Bloom を設定し、適用前後を比較して。"
- "Enable URP Color Adjustments and prove that the Game view changes."
- "Only take a screenshot" → `unity-playmode-testing`.
- "Change this material's base color" → `unity-asset-management`.

Inspect before changing, using the same endpoint on every command:

```bash
unity-cli system ping
unity-cli raw get_editor_state --json '{}'
unity-cli raw package_manager --json '{"action":"list"}'
unity-cli editor eval 'new { unity = Application.unityVersion, graphics = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline, quality = QualitySettings.renderPipeline, effective = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline }' --output json
unity-cli raw capture_screenshot --json '{"captureMode":"game","width":1280,"height":720,"osFallback":false}'
unity-cli raw read_console --json '{"count":30}'
```

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection, instance selection and recovery.
- [urp-workflow.md](references/urp-workflow.md): package compatibility, persistent assets and visual checks.
