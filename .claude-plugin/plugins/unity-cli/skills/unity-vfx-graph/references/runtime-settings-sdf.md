# Runtime, Project Settings, SDF Baking

## `vfx_runtime` — drive a live VisualEffect

Build the rig with `create_gameobject` + `add_component` (`UnityEngine.VFX.VisualEffect`), bind the
asset, then drive it through the public `UnityEngine.VFX.VisualEffect` API. Value round-trips need no
play mode.

```bash
unity-cli raw vfx_runtime --json '{"op":"set_asset","gameObject":"VfxRig","assetPath":"Assets/FX/Burst.vfx"}'
unity-cli raw vfx_runtime --json '{"op":"set_float","gameObject":"VfxRig","name":"Rate","value":7.5}'
unity-cli raw vfx_runtime --json '{"op":"send_event","gameObject":"VfxRig","eventName":"Burst","attributes":{"lifetime":2.0,"position":[0,1,0]}}'
unity-cli raw vfx_runtime --json '{"op":"get_state","gameObject":"VfxRig","name":"Rate"}'
```

Ops (all take `gameObject` = a scene object name): `set_asset` (load + bind + `Reinit`; other ops fail
until bound), `set_float`/`set_int`/`set_bool`/`set_vector2`/`set_vector3`/`set_vector4` (`name` =
exposed parameter name, `value`), `set_texture`/`set_mesh` (`name` + `assetPath`), `send_event`
(`eventName`, optional `attributes` payload: numbers → `SetFloat`, 2–4-element arrays →
`SetVector2/3/4`, bools → `SetBool`; seeds spawn-event source attributes), `set_initial_event_name`
(`name`; `""` suppresses auto-play; then `Reinit`), `reinit`, `simulate` (`deltaTime` default 0.05,
`steps` default 1), `get_state` (`hasAsset`, `aliveParticleCount`, `pause`, `playRate`,
`initialEventName`, and with `name`: `hasFloat`/`floatValue`, `hasTexture`/`textureName`,
`hasMesh`/`meshName`). Set ops echo `get_state`.

Caveats: `hasFloat`/`hasTexture`/`hasMesh` are scoped to the one queried `name`. An exposed parameter
must be **used** in the graph to survive into the runtime sheet. `aliveParticleCount` only rises for a
rendered effect advanced per frame (a Camera framing it + repeated `simulate` with frame yields) —
a single edit-mode `simulate` does not spawn; spawn verification belongs in a play-mode harness (see the runtime checklist reference).

## `vfx_settings` — environment settings (not a graph)

```bash
unity-cli raw vfx_settings --json '{"op":"get"}'
unity-cli raw vfx_settings --json '{"op":"set","setting":"fixedTimeStep","value":0.02}'
unity-cli raw vfx_settings --json '{"op":"set","setting":"maxCapacity","value":50000000}'
unity-cli raw vfx_settings --json '{"op":"get","scope":"preferences"}'
unity-cli raw vfx_settings --json '{"op":"set","scope":"preferences","setting":"instancingEnabled","value":false}'
```

- `scope:"project"` (default) = `ProjectSettings/VFXManager.asset`: a `properties` block (public
  static `VFXManager` props `fixedTimeStep`/`maxDeltaTime`, round-trip immediately) and a `serialized`
  block (`m_MaxCapacity`/`m_MaxScrubTime`/`m_BatchEmptyLifetime`, plus the Object-ref plumbing
  `m_IndirectShader`/`m_CopyBufferShader`/`m_SortShader`/`m_StripUpdateShader`/`m_RuntimeResources`,
  settable by asset path — Unity-managed defaults, override only deliberately). `set` writes the
  static property when one exists (`via:"property"`), else the serialized field (`via:"serialized"`).
- `scope:"preferences"` = per-machine EditorPrefs via `UnityEditor.VFX.VFXViewPreference`:
  `instancingEnabled` (the instancing master gate), `displayExperimentalOperator`,
  `multithreadUpdateEnabled`, `forceEditionCompilation`, `generateShadersWithDebugSymbols`,
  `advancedLogs`, `cameraBuffersFallback` (enum by name), `authoringPrewarmStepCountPerSeconds`,
  `authoringPrewarmMaxTime`, `displayExtraDebugInfo`, `visualEffectTargetListed`,
  `allowShaderExternalization`. `set` writes EditorPrefs and calls `VFXViewPreference.SetDirty()`;
  the result echoes the resolved `editorPrefsKey`.

## `vfx_bake_sdf` — Mesh → Signed Distance Field Texture3D

```bash
unity-cli raw vfx_bake_sdf --json '{"meshPath":"Assets/Models/Statue.fbx","outputPath":"Assets/SDF/Statue.asset","maxResolution":64}'
unity-cli raw vfx_bake_sdf --json '{"meshPath":"Assets/Models/Statue.fbx","outputPath":"Assets/SDF/Statue.asset","maxResolution":32,"center":[0,1,0],"size":[2,2,2],"signPassCount":2,"threshold":0.5,"overwrite":true}'
```

Consumes a Mesh asset and produces a Texture3D `.asset` via the package's public `MeshToSDFBaker`; it
does not touch a `.vfx`. Params: `meshPath`, `outputPath` (under `Assets/`, ending in `.asset`),
`maxResolution` (default 64), `center`/`size` (default = mesh bounds), `signPassCount` (1),
`threshold` (0.5), `sdfOffset` (0), `overwrite` (false). Returns `resolution`, `actualBoxSize`, `guid`.
Requires compute shader support. To use it, wire an exposed Texture3D parameter into a distance-field
input (Collision/Conform to SDF blocks) and bind it with `vfx_runtime set_texture`.
