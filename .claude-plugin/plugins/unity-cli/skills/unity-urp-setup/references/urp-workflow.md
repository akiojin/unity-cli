# URP asset and visual verification workflow

## Package and pipeline

- Inspect `package_manager` `list` / `info` and the installed package API before
  choosing a version. Unity 2022.3 uses URP 14; Unity 6000.3 uses URP 17.3.
  Resolve the patch from the Editor's supported package information. Do not install
  another Editor's package or hardcode one patch for every project.
- Install with `package_manager` `{"action":"add","packageId":"com.unity.render-pipelines.universal@<compatible-version>"}`.
  Domain Reload may disconnect the bridge: wait and inspect state rather than
  blindly repeating the install. Read the actual installed version afterward.
- For an existing URP Asset, inspect its renderer and preserve Renderer Features.
  For a new asset use `UniversalRenderPipelineAsset.Create(rendererData)` with a
  `UniversalRendererData`. Initialize its post-processing resources using the
  installed package's default `PostProcessData` and Editor resource reloader;
  merely constructing a blank ScriptableObject may leave shaders unassigned.
  Save Renderer and pipeline as separate `.asset` files with `AssetDatabase`.
- `GraphicsSettings.defaultRenderPipeline` chooses the default. A non-null
  `QualitySettings.renderPipeline` overrides it. Inspect all Quality levels before
  choosing whether to replace their overrides; preserve the active level. Assign
  only the levels included in the request and report that scope.
- Enable HDR on the pipeline and Camera for a Bloom test with HDR emission.
  For a Built-in scene, incompatible materials can render pink; use a separate
  URP-compatible test material rather than automatically converting the project.

## Persistent Volume and Camera

Use `editor eval --mode statements` for small synchronous Editor API operations.
URP namespaces are not implicit; use fully qualified names. Evaluate only after
the package has compiled. Check `state:"completed"` and diagnostics, even if the
CLI exit code is zero. Give mutations unique `--request-id` values; query
`editor eval-status` after a timeout before deciding to retry.

Create a `UnityEngine.Rendering.VolumeProfile` asset and add
`UnityEngine.Rendering.Universal.Bloom` via `profile.Add<T>(true)`. Add the returned
component to the profile asset with `AssetDatabase.AddObjectToAsset`; mark both
dirty and save. Do the same for `ColorAdjustments` when requested. On an existing
profile use `TryGet<T>` first; do not duplicate overrides or overwrite user values.

For Bloom, set `active`, `intensity.overrideState`, `intensity.value`,
`threshold.overrideState` and `threshold.value`. A test value such as intensity 2
and threshold 1 needs a visible subject brighter than threshold. Color Adjustments
can use `postExposure.Override(...)`; avoid changing it during a Bloom-only A/B
comparison. Keep all other variables fixed.

Add or reuse a `UnityEngine.Rendering.Volume` on the intended GameObject:
`isGlobal = true`, nonzero `weight`, and `sharedProfile = savedProfile`. Use a
deliberate priority when other volumes overlap. Accessing `volume.profile` may
create an instance; edit `sharedProfile` for content that must persist.

On the target Camera, add or reuse `UniversalAdditionalCameraData`, enable
`renderPostProcessing`, and include the Volume object's layer in `volumeLayerMask`.
Check camera stacking and renderer selection if the scene uses multiple cameras.
Mark changed objects dirty, mark the scene dirty and save it through Editor APIs.

## Before/after evidence

1. Record paths and GUIDs for the pipeline, renderer and profile. Capture their
   assignment and values plus the active Camera and Quality level in JSON.
2. Save a stationary test scene. Repository E2E fixtures belong under
   `Assets/Scenes/Generated/E2E/`. Use the same scene for both images; do not
   compare a Built-in baseline with different geometry to a URP result.
3. Set only Bloom intensity to zero (or disable that override), enter Play, wait
   for stable rendered frames, and call `capture_screenshot` in `game` mode with
   fixed width/height and `osFallback:false`. Keep the returned image path.
4. Set the intended Bloom intensity, let frames settle and capture again. Inspect
   both images: the bright subject should gain a visible glow while framing stays
   fixed. A byte/hash difference alone could be animation or a capture artifact.
5. Read console errors and the live values. Stop Play, restore the desired saved
   value, save assets/scene, reload and confirm assignments and overrides survive.
   Include the assets and both images with the verification report.

If the images look identical, inspect in this order: effective Quality/Graphics
pipeline, Camera `renderPostProcessing`, assigned renderer/post-processing
resources, Volume layer mask and weight/priority, active override flags and
threshold versus subject brightness. Confirm Game capture succeeded; an OS or
Scene-view capture does not demonstrate the target Camera's post-processing.

## API sources

- Unity's URP package API, not third-party plugin skill text, defines the types:
  `UniversalRenderPipelineAsset`, `UniversalRendererData`, `PostProcessData`,
  `UniversalAdditionalCameraData`, `VolumeProfile`, `Bloom`, `ColorAdjustments`.
- Read installed package sources when an API differs between URP 14 and 17.
  Rendering resources and RenderGraph internals should not be guessed or replaced
  with custom reflection-based bridge extensions.
