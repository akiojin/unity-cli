---
name: unity-vfx-graph
description: Author and inspect Unity Visual Effect Graph (vfx) assets with unity-cli. Use when the user wants to read, build, or modify a .vfx graph and its systems, contexts, blocks, operators, or particle behavior, or to discover available blocks. Do not use for generic asset, material, or import operations; use `unity-asset-management` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.5.0
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

## Do Not Use When

- The task is generic asset, material, or import work; use `unity-asset-management`.
- The request is about editing a `VisualEffect` component's runtime fields on a scene object; use `unity-gameobject-edit`.
- The work is play-mode runtime verification of particles; use `unity-playmode-testing`.

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
unity-cli raw vfx_apply --json '{"op":"add_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterName":"Rate","type":"Float","value":42.5,"category":"Tuning"}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"node":"parameter","parameterIndex":0,"slot":0},"to":{"node":"block","contextType":"Spawner","blockIndex":0,"slot":0}}'
unity-cli raw get_compilation_state --json '{}'
```

`vfx_apply` ops: `add_block` (descriptor by name), `set_block_setting` (target a block by `contextType`
+ `blockIndex` from describe, set a `[VFXSetting]` field), `add_context` (descriptor by name, with
optional `linkFrom` to flow an existing context into the new one), `add_operator` (descriptor by name,
added to the graph), `add_parameter` (exposed blackboard parameter: `parameterName` = exposed name,
`type` = a parameter descriptor name like `Float`/`Vector3`/`Color`, optional `value`/`tooltip`/
`category`/`exposed`), and `link_slots` (connect a `from` output slot to a `to` input slot; each endpoint
is `{node: operator|parameter|context|block, …address, slot: index}` where an operator uses
`operatorIndex`, a parameter uses `parameterIndex`, a block/context uses `contextType` (+ `blockIndex`
for blocks)). `vfx_describe_graph` reports each block's `settings`, per-context `inputs`/`outputs` flow
links, an `operators` array, and a `parameters` array (each with `exposedName`/`exposed`/`value`/
`category`) — every slot carries `links` (resolved node address + slot index), so confirm changes by
re-describing. Use `vfx_list_library` with `kind` (`block` default, `operator`, `context`, `parameter`)
to discover descriptor names.

## Examples

- "List the contexts and blocks in `Assets/Basic Graphs/Minimal.vfx`."
- "Which force blocks can I add to a particle system?"
- "Add a Turbulence block to the Update context of the minimal graph."
- "Set the Turbulence block's NoiseType to Perlin and confirm it stuck."
- "Add two Add operators and wire the first's output into the second's input."
- "Create an exposed float parameter `Rate` and drive the Constant Spawn Rate block with it."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
