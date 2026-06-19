---
name: unity-vfx-graph
description: Author and inspect Unity Visual Effect Graph (vfx) assets with unity-cli. Use when the user wants to read, build, or modify a .vfx graph and its systems, contexts, blocks, operators, or particle behavior, or to discover available blocks. Do not use for generic asset, material, or import operations; use `unity-asset-management` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.12.0
  category: assets
  triggers:
    - vfx
    - visual effect
    - particle
    - spawner
    - effect graph
  siblings:
    - unity-asset-management
---

# Visual Effect Graph

Author and inspect `.vfx` Visual Effect Graph assets: read a graph's contexts and the blocks inside them, discover the available block library, and apply authoring mutations to the graph. The VFX authoring API is internal to Unity, so these operations run through dedicated bridge tools rather than direct component edits. This skill is the VFX complement to `unity-asset-management`, which handles generic asset, material, and import operations.

## Use When

- The user wants to inspect a `.vfx` graph's structure (its contexts and blocks).
- The user wants to discover which blocks are available to add to a graph.
- The user wants to modify a graph, such as adding a block to a context.
- The user is verifying agent control over Visual Effect Graph authoring.
- The user wants to drive an exposed (blackboard) parameter on a live `VisualEffect` via the public API (`vfx_runtime`).

## Do Not Use When

- The task is generic asset, material, or import work; use `unity-asset-management`.
- The request is about editing arbitrary serialized fields on a scene component; use `unity-gameobject-edit`. (`vfx_runtime` is only for VisualEffect public-API calls like `SetFloat`/`SendEvent`.)
- The work is play-mode lifecycle control or input simulation; use `unity-playmode-testing`.

## Preferred Flow

1. Read the current graph with `vfx_describe_graph` before mutating, to capture a baseline of contexts and blocks.
2. Discover valid block names with `vfx_list_library` (optionally filtered) when you are unsure of an exact name.
3. Apply the narrowest mutation with `vfx_apply`.
4. Re-run `vfx_describe_graph` to confirm the change landed, then `get_compilation_state` to confirm the asset recompiled without errors.

```bash
unity-cli raw vfx_describe_graph --json '{"assetPath":"Assets/Basic Graphs/Minimal.vfx"}'
unity-cli raw vfx_list_library --json '{"filter":"turbulence"}'
unity-cli raw vfx_list_library --json '{"kind":"operator","filter":"Add"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockName":"Turbulence"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"NoiseType","value":"Perlin"}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"Output Particle|Point","linkFrom":"Update"}'
unity-cli raw vfx_apply --json '{"op":"add_operator","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorName":"Add"}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"node":"operator","operatorIndex":0,"slot":0},"to":{"node":"operator","operatorIndex":1,"slot":0}}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"Event","settings":{"eventName":"Burst"}}'
unity-cli raw vfx_apply --json '{"op":"link_flow","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"contextType":"Event"},"to":{"contextType":"Spawner"},"toIndex":0}'
unity-cli raw vfx_apply --json '{"op":"add_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterName":"Rate","type":"Float","value":42.5,"category":"Tuning"}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"node":"parameter","parameterIndex":0,"slot":0},"to":{"node":"block","contextType":"Spawner","blockIndex":0,"slot":0}}'
unity-cli raw vfx_apply --json '{"op":"set_bounds","assetPath":"Assets/Basic Graphs/Minimal.vfx","mode":"Manual","center":[0,0,0],"size":[4,4,4]}'
unity-cli raw vfx_apply --json '{"op":"add_sticky_note","assetPath":"Assets/Basic Graphs/Minimal.vfx","title":"TODO","contents":"wire up bursts","position":[10,20,240,120],"colorTheme":2,"textSize":"Medium"}'
unity-cli raw vfx_apply --json '{"op":"set_instancing","assetPath":"Assets/Basic Graphs/Minimal.vfx","mode":"Disabled"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockName":"Custom HLSL"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"m_HLSLCode","value":"void DoIt(inout VFXAttributes a, in float k){a.position *= k;}"}'
unity-cli raw vfx_apply --json '{"op":"create_subgraph_asset","subgraphPath":"Assets/Basic Graphs/MySub.vfxblock","kind":"block"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockName":"Empty Subgraph Block"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"m_Subgraph","value":"Assets/Basic Graphs/MySub.vfxblock"}'
unity-cli raw get_compilation_state --json '{}'
```

`vfx_apply` ops: `add_block` (descriptor by name), `set_block_setting` (target a block by `contextType`
+ `blockIndex` from describe, set a `[VFXSetting]` field), `add_context` (descriptor by name, with
optional `linkFrom` to flow an existing context into the new one), `add_operator` (descriptor by name,
added to the graph, with optional `settings` like an Event context's `eventName`), `add_parameter`
(exposed blackboard parameter: `parameterName` = exposed name, `type` = a parameter descriptor name like
`Float`/`Vector3`/`Color`, optional `value`/`tooltip`/`category`/`exposed`), `link_slots` (connect a
`from` output slot to a `to` input slot; each endpoint is `{node: operator|parameter|context|block,
…address, slot: index}` where an operator uses `operatorIndex`, a parameter uses `parameterIndex`, a
block/context uses `contextType` (+ `blockIndex` for blocks)), and `link_flow` (context→context flow
edge, e.g. an Event context into Spawn: `from`/`to` are `{contextType}` or `{index}`, with optional
`fromIndex`/`toIndex` flow-slot indices), and `set_bounds` (write the Initialize context's particle
bounds: `mode` switches `boundsMode` Manual/Recorded/Automatic; `center`/`size` (Vector3 arrays) write
the bounds AABox when the mode exposes one; `padding` writes `boundsPadding` for Recorded/Automatic),
and `add_sticky_note` (UI metadata: `title`, `contents`, optional `position` (`[x,y,width,height]`),
`colorTheme` int 1–3, `textSize` "Small"/"Medium"/"Large"/"Huge"), and `set_instancing` (write the
asset's `VisualEffectResource.instancingMode` — values include `Auto`/`Disabled`/`ForceOn` — and
optional `capacity` int). Describe surfaces sticky notes via a top-level `stickyNotes` array and the
resource's current instancing via a top-level `instancing: {mode, capacity}` block.

Systems: a full particle system is just the descriptor chain Init→Update→Output sharing one
`VFXDataParticle`. Build a fresh system by `add_context "Initialize Particle"` +
`add_context "Update Particle"` + `add_context "Output Particle|Unlit|Quad"` (or another Output
variant), then `link_flow` them by `{index}` — `VFXContext.LinkTo` auto-merges the contexts'
`VFXData`, so each chain becomes its own system. Describe emits a `dataInstanceId` per context;
equal ids prove system membership, different ids prove disjoint systems. Use this to verify a
from-scratch chain landed in a fresh system rather than accidentally attaching to an existing one.

Subgraph: `create_subgraph_asset` copies a default Block or Operator subgraph template into a target
path (`subgraphPath` + `kind: "block"|"operator"`); the parent graph references it by adding the
matching library node (`add_block "Empty Subgraph Block"` / `add_operator "Empty Subgraph Operator"`)
and then writing the asset path into the `m_Subgraph` setting via `set_block_setting`. The
`set_block_setting` op auto-detects `UnityEngine.Object`-derived fields and loads the value as an asset
path via `AssetDatabase`. Describe surfaces object references as `{type, name, assetPath}`.

Custom HLSL needs no dedicated op: the Custom HLSL block (descriptor `"Custom HLSL"`, category `HLSL`)
and Custom HLSL operator (category `Operator/HLSL`) are discoverable via `vfx_list_library` and instantiate
through `add_block`/`add_operator`. Write the inline HLSL function via `set_block_setting` with
`setting:"m_HLSLCode"` (other settings: `m_BlockName` for the displayed name, `m_ShaderFile` to switch
to an external `ShaderInclude`). Describe surfaces these as block `settings` (the oracle reports every
`[VFXSetting]`, including `ReadOnly` fields like `m_HLSLCode`), and the block's input slots are
re-parsed from the HLSL signature.
`vfx_describe_graph` reports each context's `settings` (including `boundsMode` on Init), `inputSlots`
(each slot's resolved `value` for unlinked slots — e.g. the bounds AABox center/size), each block's
`settings`, per-context `inputs`/`outputs` flow links, an `operators` array, and a `parameters` array
(each with `exposedName`/`exposed`/`value`/`category`) — every slot carries `links` (resolved node
address + slot index), so confirm changes by re-describing. Use `vfx_list_library` with `kind` (`block`
default, `operator`, `context`, `parameter`) to discover descriptor names.

To verify an exposed parameter at runtime, put the `.vfx` on a scene `VisualEffect` and drive it via
`vfx_runtime` (the public `UnityEngine.VFX.VisualEffect` API; no play-mode required for the value
round-trip). Build the rig with `create_gameobject` + `add_component` (`UnityEngine.VFX.VisualEffect`),
then:

```bash
unity-cli raw vfx_runtime --json '{"op":"set_asset","gameObject":"VfxRig","assetPath":"Assets/Basic Graphs/Minimal.vfx"}'
unity-cli raw vfx_runtime --json '{"op":"set_float","gameObject":"VfxRig","name":"Rate","value":7.5}'
unity-cli raw vfx_runtime --json '{"op":"get_state","gameObject":"VfxRig","name":"Rate"}'
```

`vfx_runtime` ops target a named scene object's `VisualEffect`: `set_asset` (load + bind a
`VisualEffectAsset`, then `Reinit`), `set_float`/`set_int`/`set_bool`/`set_vector2`/`set_vector3`/
`set_vector4` (`name` = the exposed parameter name), `send_event` (`eventName`), `reinit`, and
`get_state` (reports `hasAsset`, `aliveParticleCount`, `pause`, `playRate`, and — when `name` is given —
`hasFloat`/`floatValue`). Set ops echo `get_state` so you can confirm the round-trip in one call.

## Examples

- "List the contexts and blocks in `Assets/Basic Graphs/Minimal.vfx`."
- "Which force blocks can I add to a particle system?"
- "Add a Turbulence block to the Update context of the minimal graph."
- "Set the Turbulence block's NoiseType to Perlin and confirm it stuck."
- "Add two Add operators and wire the first's output into the second's input."
- "Create an exposed float parameter `Rate` and drive the Constant Spawn Rate block with it."
- "Put the graph on a VisualEffect and set its exposed `Rate` to 7.5 at runtime, then read it back."
- "Add a custom `Burst` Event context, wire it into Spawn, and trigger it at runtime with `send_event`."
- "Switch the Initialize context to Manual bounds with center (0,0,0) and size (4,4,4)."
- "Drop a sticky note on the graph titled 'TODO' explaining the burst plan."
- "Disable instancing on this VFX asset so each VisualEffect runs as an individual draw."
- "Add a Custom HLSL block to the Update context and inline a function that scales position by a float."
- "Create a Block subgraph asset next to the main graph and reference it from an Empty Subgraph Block in Update."
- "Build a second particle system in this graph from scratch — Init→Update→Output — and confirm it's disjoint from the first."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
