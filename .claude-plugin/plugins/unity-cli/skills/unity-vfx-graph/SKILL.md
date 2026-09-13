---
name: unity-vfx-graph
description: Author and inspect Unity Visual Effect Graph (vfx) assets with unity-cli. Use when the user wants to read, build, or modify a .vfx graph and its systems, contexts, blocks, operators, or particle behavior, or to discover available blocks. Do not use for generic asset, material, or import operations; use `unity-asset-management` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.34.0
  category: assets
  triggers:
    - vfx
    - visual effect
    - particle
    - spawner
    - effect graph
    - custom hlsl
  siblings:
    - unity-asset-management
---

# Visual Effect Graph

Author and inspect `.vfx` Visual Effect Graph assets: read a graph's contexts, blocks, operators and parameters, discover the node library, and apply authoring mutations. The VFX authoring API is internal to Unity, so these operations run through dedicated bridge tools (`vfx_*`) rather than direct component edits. This skill is the VFX complement to `unity-asset-management`, which handles generic asset, material, and import operations.

## Use When

- The user wants to inspect a `.vfx` graph's structure (contexts, blocks, operators, parameters, links, layout).
- The user wants to discover which blocks, operators, contexts, or templates are available.
- The user wants to build or modify a graph: add/remove/link nodes, set values and settings, manage the blackboard, systems, events, subgraphs, sticky notes, groups, or canvas layout.
- The user wants to verify a graph compiles, or find out why it does not.
- The user wants to drive an exposed parameter on a live `VisualEffect` (`vfx_runtime`), read/write VFX project settings or editor preferences (`vfx_settings`), or bake a Mesh into an SDF Texture3D (`vfx_bake_sdf`).

## Do Not Use When

- The task is generic asset, material, or import work; use `unity-asset-management`.
- The request is about editing arbitrary serialized fields on a scene component; use `unity-gameobject-edit`. (`vfx_runtime` is only for VisualEffect public-API calls like `SetFloat`/`SendEvent`.)
- The work is play-mode lifecycle control or input simulation; use `unity-playmode-testing`.

## Preferred Flow

1. **Baseline.** `vfx_describe_graph` before mutating. Note the three oracles it carries: `errors` (validation + compile errors, on by default), `compile` (outcome of the last recompile), and `layout` (`overlapCount` + overlapping node pairs).
2. **Discover names.** `vfx_list_library` with `kind` (`block` default, `operator`, `context`, `parameter`, `template`) and a `filter` whenever you are not certain of an exact descriptor name — the leading `|` and `|_` separators in names like `|Set|_Color` are load-bearing.
3. **Mutate narrowly** with `vfx_apply`, one op at a time. **Every op response carries `compile.success`; read it.** If it is `false`, the graph no longer compiles — `compile.exception` / `compile.errors` / `compile.logs` say why. Fix that before doing anything else; never stack more ops on a broken graph. An op that returns `error` was not applied at all.
4. **Lay the canvas out — mandatory, scoped to your work.** Auto-placement on add ops only prevents nodes from stacking at the origin; it does not produce a readable graph. Before you report a graph as done, run `vfx_apply op:"auto_layout"`. Its default scope is **only the systems you touched this session** (tracked automatically per op) — untouched systems and their feeders stay exactly where the person left them. Pass `scope:"all"` only when the user asks for the whole graph to be tidied, or `contexts:[…]` to pick systems. If the user placed nodes by hand and wants them kept, use `move_node` to resolve overlaps instead. A graph a human cannot read in the editor is not finished.
5. **Verify and only then report.** Re-run `vfx_describe_graph` and confirm all three: `compile.success == true`, no `errors[]` entry with `type: "Error"`, and `layout.overlapCount == 0`. Quote those three facts in your completion report. (`get_compilation_state` is C# script compilation and says nothing about a VFX graph; use `compile` / describe instead.)

Invocation: every tool runs as `unity-cli raw <tool> --json '<json>'`. Add `--output json` for a structured (parseable) result, and `--port <N>` to target a specific bridge (default `6400`).

```bash
unity-cli raw vfx_describe_graph --json '{"assetPath":"Assets/FX/Burst.vfx"}'
unity-cli raw vfx_list_library --json '{"kind":"block","filter":"turbulence"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/FX/Burst.vfx","contextType":"Update","blockName":"Turbulence","settings":{"NoiseType":"Perlin"}}'
unity-cli raw vfx_apply --json '{"op":"add_operator","assetPath":"Assets/FX/Burst.vfx","operatorName":"Multiply"}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/FX/Burst.vfx","from":{"node":"operator","operatorIndex":0,"slot":0},"to":{"node":"block","contextType":"Update","blockIndex":0,"slot":0}}'
unity-cli raw vfx_apply --json '{"op":"add_parameter","assetPath":"Assets/FX/Burst.vfx","parameterName":"Rate","type":"Float","value":42.5,"min":0,"max":100}'
unity-cli raw vfx_apply --json '{"op":"compile","assetPath":"Assets/FX/Burst.vfx"}'
unity-cli raw vfx_apply --json '{"op":"auto_layout","assetPath":"Assets/FX/Burst.vfx"}'
```

## Hard Rules

- **Custom HLSL operator: at most 4 inputs.** A VFX expression takes at most 4 parents, so a Custom HLSL *operator* whose function has 5+ parameters makes the asset stop compiling. `set_operator_setting`/`add_operator` refuse such a function with a clear error; pack inputs into `float2/3/4` or split the function. Custom HLSL *blocks* have no such limit.
- **Addressing.** Block ops take `contextType` (first context of that type) **or** `contextIndex` (absolute index from describe). Context ops (`set_context_setting`, `remove_context`, `delete_system`, `set_system_name`, `set_bounds`, `link_flow`/`unlink_flow` endpoints) take `contextType` or `index` — `contextIndex` is accepted as an alias everywhere. Prefer the index whenever a graph has two contexts of the same type.
- **Systems never move sideways.** Contexts of a system that existed before your session keep their x; `auto_layout` and `move_node` only move them vertically (a horizontal `move_node` on such a context keeps x and says so in `note`). Only a system you created this session is placed in a fresh column. Operators and parameter nodes move freely.
- **Short edges over shared nodes.** Do not route one parameter node or operator across the canvas to many consumers. Give each consuming context its own parameter canvas node and put cheap operators beside the system they feed; `auto_layout` does both automatically (`duplicateShared`, `splitParameters`), so operator indices can change after it — re-describe before addressing by index.
- **Linked slots ignore constants.** `set_slot_value` on a linked input warns (`warning`) — `unlink_slots` first if you meant the constant.
- **Parameters must be used to survive.** An exposed parameter that feeds nothing is stripped at compile and invisible at runtime (`vfx_runtime` `hasFloat:false`).
- **`|Set|_X` / `Get|_X` descriptors** compose attribute blocks/operators; custom attributes are declared with `add_custom_attribute` and then targeted with `set_block_setting setting:"attribute"`.

## Op Index

| Area | Ops (all `vfx_apply`) | Reference |
|---|---|---|
| Blocks | `add_block`, `set_block_setting`, `set_block_enabled`, `reorder_block`, `move_block`, `duplicate_block`, `remove_block` | [ops-graph.md](references/ops-graph.md) |
| Contexts / systems | `add_context`, `set_context_setting`, `remove_context`, `delete_system`, `set_system_name`, `link_flow`, `unlink_flow`, `set_bounds` | [ops-graph.md](references/ops-graph.md), [systems-and-events.md](references/systems-and-events.md) |
| Operators | `add_operator`, `set_operator_setting`, `duplicate_operator`, `remove_operator`, `add_operator_input`, `remove_operator_input`, `set_operator_operand_type`, `rename_operator_input`, `reorder_operator_input` | [ops-graph.md](references/ops-graph.md) |
| Slots / links | `link_slots`, `unlink_slots`, `set_slot_value`, `set_slot_space`, `convert_to_property`, `convert_to_inline` | [ops-graph.md](references/ops-graph.md) |
| Blackboard | `add_parameter`, `set_parameter`, `rename_parameter`, `set_parameter_category`, `rename_category`, `reorder_category`, `reorder_parameter`, `duplicate_parameter`, `remove_parameter`, `add_custom_attribute` | [ops-graph.md](references/ops-graph.md) |
| Compile / verify | `compile`; describe `errors` / `compile` | [ops-graph.md](references/ops-graph.md) |
| Layout / canvas | `auto_layout`, `move_node`, `group_nodes`, `remove_group`, `add_sticky_note`, `update_sticky_note`, `remove_sticky_note`, `reorder_sticky_note`; describe `layout` | [layout.md](references/layout.md) |
| Custom HLSL | `add_block "Custom HLSL"`, `add_operator "Custom HLSL"`, `m_HLSLCode` / `m_ShaderFile` / function selector | [custom-hlsl.md](references/custom-hlsl.md) |
| Events, subgraphs, templates, asset | GPU/output events, `create_subgraph_asset`, `create_from_template`, `insert_template`, `designate_template`, `set_instancing`, `set_initial_event_name` | [systems-and-events.md](references/systems-and-events.md) |
| Runtime, settings, SDF | `vfx_runtime`, `vfx_settings`, `vfx_bake_sdf` | [runtime-settings-sdf.md](references/runtime-settings-sdf.md) |

## Examples

- "List the contexts and blocks in `Assets/FX/Burst.vfx`."
- "Add a Turbulence block to the Update context, set NoiseType to Perlin, and confirm it compiles."
- "Create an exposed float `Rate`, drive the Constant Spawn Rate block with it, then tidy the canvas."
- "Build a second particle system Init→Update→Output in this graph and confirm it is disjoint from the first."
- "Add a Custom HLSL operator that combines three floats and a float3 into a float3."
- "Why does this graph not compile?"
- "Put the graph on a VisualEffect, set `Rate` to 7.5 at runtime, and read it back."
- "Set the fixed time step to 0.02 in the VFX project settings."

## References

- [ops-graph.md](references/ops-graph.md): blocks, contexts, operators, slots/links, blackboard, compile/verify — parameters and semantics for every op.
- [layout.md](references/layout.md): the readable-canvas rules, `auto_layout`, `move_node`, groups, sticky notes, and the `layout` oracle.
- [custom-hlsl.md](references/custom-hlsl.md): Custom HLSL blocks and operators, the 4-input operator limit, external files, function selectors, buffer/texture types.
- [systems-and-events.md](references/systems-and-events.md): building systems and variants, events (GPU/spawn/output), subgraphs, templates, instancing and initial event.
- [runtime-settings-sdf.md](references/runtime-settings-sdf.md): `vfx_runtime`, `vfx_settings`, `vfx_bake_sdf`.
- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites, runtime verification caveats.
