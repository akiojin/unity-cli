---
name: unity-vfx-graph
description: Author and inspect Unity Visual Effect Graph (vfx) assets with unity-cli. Use when the user wants to read, build, or modify a .vfx graph and its systems, contexts, blocks, operators, or particle behavior, or to discover available blocks. Do not use for generic asset, material, or import operations; use `unity-asset-management` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.33.0
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
- The user wants to bake a Mesh into a Signed Distance Field (SDF) Texture3D for use in a VFX (`vfx_bake_sdf`).

## Do Not Use When

- The task is generic asset, material, or import work; use `unity-asset-management`.
- The request is about editing arbitrary serialized fields on a scene component; use `unity-gameobject-edit`. (`vfx_runtime` is only for VisualEffect public-API calls like `SetFloat`/`SendEvent`.)
- The work is play-mode lifecycle control or input simulation; use `unity-playmode-testing`.

## Preferred Flow

1. Read the current graph with `vfx_describe_graph` before mutating, to capture a baseline of contexts and blocks.
2. Discover valid block names with `vfx_list_library` (optionally filtered) when you are unsure of an exact name.
3. Apply the narrowest mutation with `vfx_apply`.
4. Re-run `vfx_describe_graph` to confirm the change landed, then `get_compilation_state` to confirm the asset recompiled without errors.

Invocation: every tool runs as `unity-cli raw <tool> --json '<json>'`. Add `--output json` for a structured (parseable) result, and `--port <N>` to target a specific bridge (default `6400`).

```bash
unity-cli raw vfx_describe_graph --json '{"assetPath":"Assets/Basic Graphs/Minimal.vfx"}'
unity-cli raw vfx_list_library --json '{"filter":"turbulence"}'
unity-cli raw vfx_list_library --json '{"kind":"operator","filter":"Add"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockName":"Turbulence"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextIndex":4,"blockName":"Single Burst"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"NoiseType","value":"Perlin"}'
unity-cli raw vfx_apply --json '{"op":"set_block_enabled","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"enabled":false}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"node":"parameter","parameterIndex":0,"slot":0},"to":{"node":"block","contextType":"Update","blockIndex":0,"activation":true}}'
unity-cli raw vfx_apply --json '{"op":"reorder_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"toIndex":1}'
unity-cli raw vfx_apply --json '{"op":"move_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"toContextType":"Init"}'
unity-cli raw vfx_apply --json '{"op":"duplicate_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"Output Particle|Point","linkFrom":"Update"}'
unity-cli raw vfx_apply --json '{"op":"add_operator","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorName":"Add"}'
unity-cli raw vfx_apply --json '{"op":"duplicate_operator","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0}'
unity-cli raw vfx_apply --json '{"op":"set_operator_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"setting":"m_HLSLCode","value":"float MyScale(in float k){return k*2.0f;}"}'
unity-cli raw vfx_apply --json '{"op":"add_operator_input","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"operandType":"Vector3"}'
unity-cli raw vfx_apply --json '{"op":"remove_operator_input","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0}'
unity-cli raw vfx_apply --json '{"op":"set_operator_operand_type","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"operandType":"Vector2"}'
unity-cli raw vfx_apply --json '{"op":"rename_operator_input","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"index":0,"name":"Alpha"}'
unity-cli raw vfx_apply --json '{"op":"reorder_operator_input","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"index":0,"toIndex":2}'
unity-cli raw vfx_apply --json '{"op":"set_context_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Spawner","setting":"loopDuration","value":"Constant"}'
unity-cli raw vfx_apply --json '{"op":"set_context_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Init","setting":"capacity","value":256}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"node":"operator","operatorIndex":0,"slot":0},"to":{"node":"operator","operatorIndex":1,"slot":0}}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"Event","settings":{"eventName":"Burst"}}'
unity-cli raw vfx_apply --json '{"op":"link_flow","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"contextType":"Event"},"to":{"contextType":"Spawner"},"toIndex":0}'
unity-cli raw vfx_apply --json '{"op":"unlink_flow","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"index":2},"to":{"index":3}}'
unity-cli raw vfx_apply --json '{"op":"add_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterName":"Rate","type":"Float","value":42.5,"min":0,"max":100,"category":"Tuning"}'
unity-cli raw vfx_apply --json '{"op":"add_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterName":"Tint","type":"Color","value":[1,0,0,1]}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"node":"parameter","parameterIndex":0,"slot":0},"to":{"node":"block","contextType":"Spawner","blockIndex":0,"slot":0}}'
unity-cli raw vfx_apply --json '{"op":"set_slot_value","assetPath":"Assets/Basic Graphs/Minimal.vfx","target":{"node":"block","contextType":"Spawner","blockIndex":0,"slot":0},"value":42.5}'
unity-cli raw vfx_apply --json '{"op":"set_slot_value","assetPath":"Assets/Basic Graphs/Minimal.vfx","target":{"node":"context","contextType":"Init","slot":0},"subPath":["center"],"value":[1,2,3]}'
unity-cli raw vfx_apply --json '{"op":"set_slot_value","assetPath":"Assets/Basic Graphs/Minimal.vfx","target":{"node":"context","contextType":"Init","slot":0},"subPath":["size","x"],"value":9}'
unity-cli raw vfx_apply --json '{"op":"unlink_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","target":{"node":"operator","operatorIndex":1,"slot":0}}'
unity-cli raw vfx_apply --json '{"op":"remove_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0}'
unity-cli raw vfx_apply --json '{"op":"remove_operator","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0}'
unity-cli raw vfx_apply --json '{"op":"remove_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterIndex":0}'
unity-cli raw vfx_apply --json '{"op":"rename_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterIndex":0,"exposedName":"SpawnRate"}'
unity-cli raw vfx_apply --json '{"op":"set_parameter_category","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterIndex":0,"category":"Tuning"}'
unity-cli raw vfx_apply --json '{"op":"rename_category","assetPath":"Assets/Basic Graphs/Minimal.vfx","category":"Tuning","newCategory":"Spawning"}'
unity-cli raw vfx_apply --json '{"op":"reorder_category","assetPath":"Assets/Basic Graphs/Minimal.vfx","category":"Spawning","toIndex":0}'
unity-cli raw vfx_apply --json '{"op":"reorder_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterIndex":0,"order":2}'
unity-cli raw vfx_apply --json '{"op":"duplicate_parameter","assetPath":"Assets/Basic Graphs/Minimal.vfx","parameterIndex":0}'
unity-cli raw vfx_apply --json '{"op":"remove_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Output"}'
unity-cli raw vfx_apply --json '{"op":"delete_system","assetPath":"Assets/Basic Graphs/Minimal.vfx","index":4}'
unity-cli raw vfx_apply --json '{"op":"set_context_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Init","setting":"space","value":"World"}'
unity-cli raw vfx_apply --json '{"op":"add_custom_attribute","assetPath":"Assets/Basic Graphs/Minimal.vfx","attributeName":"Heat","attributeType":"Float","description":"per-particle heat"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Init","blockName":"|Set|_Color"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Init","blockIndex":0,"setting":"attribute","value":"Heat"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Init","blockIndex":0,"setting":"channels","value":"XY"}'
unity-cli raw vfx_apply --json '{"op":"set_bounds","assetPath":"Assets/Basic Graphs/Minimal.vfx","mode":"Manual","center":[0,0,0],"size":[4,4,4]}'
unity-cli raw vfx_apply --json '{"op":"add_sticky_note","assetPath":"Assets/Basic Graphs/Minimal.vfx","title":"TODO","contents":"wire up bursts","position":[10,20,240,120],"colorTheme":2,"textSize":"Medium"}'
unity-cli raw vfx_apply --json '{"op":"update_sticky_note","assetPath":"Assets/Basic Graphs/Minimal.vfx","index":0,"title":"DONE","contents":"bursts wired"}'
unity-cli raw vfx_apply --json '{"op":"remove_sticky_note","assetPath":"Assets/Basic Graphs/Minimal.vfx","index":0}'
unity-cli raw vfx_apply --json '{"op":"reorder_sticky_note","assetPath":"Assets/Basic Graphs/Minimal.vfx","index":0,"toIndex":2}'
unity-cli raw vfx_apply --json '{"op":"set_instancing","assetPath":"Assets/Basic Graphs/Minimal.vfx","mode":"Disabled"}'
unity-cli raw vfx_apply --json '{"op":"set_initial_event_name","assetPath":"Assets/Basic Graphs/Minimal.vfx","eventName":"Launch"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockName":"Trigger Event|On Die"}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"GPU Event"}'
unity-cli raw vfx_apply --json '{"op":"link_slots","assetPath":"Assets/Basic Graphs/Minimal.vfx","from":{"node":"block","contextType":"Update","blockIndex":0,"slot":0},"to":{"node":"context","contextType":"SpawnerGPU","slot":0}}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Spawner","blockName":"Set SpawnEvent Color"}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"Output Event"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockName":"Custom HLSL"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"m_HLSLCode","value":"void DoIt(inout VFXAttributes a, in float k){a.position *= k;}"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"m_ShaderFile","value":"Assets/Basic Graphs/MyInclude.hlsl"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"m_AvailableFunction","value":"FuncB"}'
unity-cli raw vfx_apply --json '{"op":"add_operator","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorName":"Custom HLSL"}'
unity-cli raw vfx_apply --json '{"op":"set_operator_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"setting":"m_AvailableFunctions","value":"OpB"}'
unity-cli raw vfx_apply --json '{"op":"create_subgraph_asset","subgraphPath":"Assets/Basic Graphs/MySub.vfxblock","kind":"block"}'
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockName":"Empty Subgraph Block"}'
unity-cli raw vfx_apply --json '{"op":"set_block_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"setting":"m_Subgraph","value":"Assets/Basic Graphs/MySub.vfxblock"}'
unity-cli raw vfx_apply --json '{"op":"create_subgraph_asset","subgraphPath":"Assets/Basic Graphs/MyOpSub.vfxoperator","kind":"operator"}'
unity-cli raw vfx_apply --json '{"op":"add_operator","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorName":"Empty Subgraph Operator"}'
unity-cli raw vfx_apply --json '{"op":"set_operator_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"setting":"m_Subgraph","value":"Assets/Basic Graphs/MyOpSub.vfxoperator"}'
unity-cli raw vfx_apply --json '{"op":"create_subgraph_asset","subgraphPath":"Assets/Basic Graphs/MySysSub.vfx","kind":"system"}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"Subgraph","subgraphPath":"Assets/Basic Graphs/MySysSub.vfx"}'
unity-cli raw vfx_list_library --json '{"kind":"template"}'
unity-cli raw vfx_apply --json '{"op":"create_from_template","targetPath":"Assets/Basic Graphs/Burst.vfx","template":"03_Simple_Burst"}'
unity-cli raw vfx_apply --json '{"op":"insert_template","assetPath":"Assets/Basic Graphs/Minimal.vfx","template":"03_Simple_Burst"}'
unity-cli raw vfx_apply --json '{"op":"designate_template","assetPath":"Assets/Basic Graphs/Minimal.vfx","name":"My Burst","category":"My Custom","description":"reusable burst"}'
unity-cli raw get_compilation_state --json '{}'
```

Addressing a context for a block op: every block op (`add_block`, `set_block_setting`, `set_block_enabled`,
`remove_block`, `reorder_block`, `move_block`, `duplicate_block`) targets its context by `contextType` (the
**first** context of that type) **or** by `contextIndex` (the absolute position in the graph's context list,
from describe). Use `contextIndex` when a graph has two contexts of the same type — e.g. two Spawners across
two systems — since `contextType` alone always resolves to the first match. `move_block`/`duplicate_block`
likewise accept `toContextIndex` alongside `toContextType` for the destination. The same `contextIndex` works
on `link_slots`/`unlink_slots`/`set_slot_value` block and context endpoints.

`vfx_apply` ops: `add_block` (descriptor by name), `set_block_setting` (target a block by `contextType`
(or `contextIndex`) + `blockIndex` from describe, set a `[VFXSetting]` field), `set_block_enabled` (toggle a block's
`enabled` state by `contextType`+`blockIndex`+`enabled` bool — describe surfaces `blocks[].enabled`;
for *dynamic* per-particle/frame activation instead of a static toggle, `link_slots`/`unlink_slots` a bool
output into the block's activation port by adding `"activation":true` to the `to`/`target` block endpoint
— describe surfaces it as `blocks[].activationSlot` with its `links`),
`reorder_block` (move a block to `toIndex` within its context) and `move_block` (relocate a block to a
compatible `toContextType` — validated via `VFXContext.Accept`, so an incompatible target returns a
clear error rather than corrupting the graph), `duplicate_block` (clone a block by `contextType`+`blockIndex`
— same `[VFXSetting]`s + slot values, fresh GUIDs, slots unlinked; optional `index` insert position and
`toContextType` to copy into another compatible context, also `Accept`-validated) and its operator twin
`duplicate_operator` (clone a graph operator by `operatorIndex`), `set_operator_setting` (symmetrical to
`set_block_setting` for operators: target by `operatorIndex` from describe, set a `[VFXSetting]` field —
e.g. a Custom HLSL operator's `m_HLSLCode`/`m_OperatorName`, or an Operator subgraph's `m_Subgraph`
asset path; some settings reshape the operator's ports, and `operators[].settings` in describe reflects
the write), the operator-input ops for **dynamic numeric operators** (`add_operator_input` /
`remove_operator_input` / `set_operator_operand_type`, all by `operatorIndex`): **cascaded** operators
(`Add`/`Multiply`/… — the `+`/`−` in the UI) take `add_operator_input` (optional `operandType`, default
the operator's current type) and `remove_operator_input` (optional `index`, default last; refuses to drop
below the operator's minimum, normally 2); `set_operator_operand_type` retypes operands — **uniform**
operators (`Sine`/`Distance`/… one shared type) take just `operandType`, **unified/cascaded** operators
take an optional `index` (else all operands change). `operandType` must be one of the operator's valid
types (`Float`/`Vector2`/`Vector3`/`Vector4`/…); describe's `inputSlots[].valueType` reflects the result
and `inputSlots` grow/shrink. `rename_operator_input` (`index`+`name` → renames a cascaded operand; the name
drives `inputSlots[].name`) and `reorder_operator_input` (`index`+`toIndex` → moves an operand, links survive)
are cascaded-only. Non-dynamic operators return a clear error), `set_context_setting` (set a `[VFXSetting]` on a context by `contextType` or `index` — Spawn
loop settings (`loopDuration`/`loopCount`/`delayBeforeLoop`), Update toggles (`ageParticles`/
`reapParticles`), Output blend/UV/shader knobs (`blendMode`/`uvMode`). **Flipbook:** setting
`uvMode:"Flipbook"` surfaces a `flipBookSize` input slot on the Output (a FlipBook x/y
grid — set via `set_slot_value` `target:{node:"context",contextType:"Output",slot:0}` + `subPath:["x"|"y"]`).
Frame blending and motion vectors are NOT uvMode variants — they are the separate plain `[VFXSetting]` bools
`flipbookBlendFrames`/`flipbookMotionVectors`. **shaderGraph asset:** use a
**dedicated Shader Graph output** (`add_context "Output Particle|Shader Graph|Quad"` → `VFXComposedParticleOutput`;
assigning a shaderGraph to a plain Unlit output raises `WrongOutputShaderGraph`), then
`set_context_setting index:<sgOutput> setting:"shaderGraph" value:"<path>.shadergraph"`. The `shaderGraph`
field lives on the composed output's nested shading sub-object, so the op resolves it via the model's
`GetSetting` (response `via:"context-composed"`); the `.shadergraph` must be authored with **Support VFX
Graph** enabled (a URP target toggle) so it imports as a `ShaderGraphVfxAsset`, else it's rejected. The op
also reaches the context's
particle **data** as a fallback, so Init `capacity`/`stripCapacity` work too — the response's `via` field
reports `context` vs `data`; it also falls back to a writable **property** (`via:"…-property"`) for
settings that aren't `[VFXSetting]` fields, notably **simulation space** (`setting:"space"`,
`value:"Local"|"World"` — applies to the whole system since space lives on the shared particle data,
surfaced in describe as `contexts[].simulationSpace`); `contexts[].settings` in describe reflects field
writes), `delete_system` (delete a whole particle system in one op — every context sharing the addressed
context's `VFXData`, i.e. the Init/Update/Output of one system; address any member by `contextType` or
`index`; the cascade matches `remove_context` so a disjoint system is left intact — the response reports
`removedContexts`/`removedContextTypes`/`remainingContexts`), `set_system_name` (set a system's display
label; address any member context by `contextType`/`index` + `name` — for a particle system the name is
written to the shared `VFXData.title` so every Init/Update/Output member reports it, for a Spawner it is
the context label; surfaced in describe as `contexts[].systemName`), `add_custom_attribute` (declare a
blackboard-managed custom attribute: `attributeName` + `attributeType` = one of
`Float`/`Vector2`/`Vector3`/`Vector4`/`Bool`/`Uint`/`Int`, optional `description`/`isReadOnly`; describe
surfaces them in a top-level `customAttributes` array. To USE it, add a Set/Get attribute block/operator
for any built-in attribute then repoint it with `set_block_setting setting:"attribute" value:"<Name>"`
(custom attributes have no `|Set|_<Name>` library descriptor — the block class is the same generic
`SetAttribute`). Built-in/duplicate names and unknown types return a clear error), `add_context` (descriptor by name, with
optional `linkFrom` to flow an existing context into the new one), `add_operator` (descriptor by name,
added to the graph, with optional `settings` like an Event context's `eventName`), `add_parameter`
(blackboard parameter: `parameterName` = exposed name, `type` = a parameter descriptor name —
`Bool`/`Int`/`Uint`/`Float`/`Vector2`/`Vector3`/`Vector4`/`Color`/`Texture2D`/`Texture3D`/`Cubemap`/
`Gradient`/`Animation Curve`/`Mesh` (spaces optional: `Vector3`≡`Vector 3`); `value` is coerced to the
type — number/bool, `[x,y,z]` vector, `[r,g,b,a]` color, or an asset-path string for Texture/Mesh;
optional `exposed:false` for a constant (non-exposed) param, `min`/`max` for a numeric Range (sets
`valueFilter=Range`, surfaced in describe as `parameters[].valueFilter`/`min`/`max`),
`tooltip`/`category` — note the `category` string groups the param immediately (`parameters[].category`),
but the top-level `categories[]` array stays empty until a category op runs (see `reorder_category` below),
so grade grouping off `parameters[].category`, not `categories[]`), `link_slots` (connect a
`from` output slot to a `to` input slot; each endpoint is `{node: operator|parameter|context|block,
…address, slot: index}` where an operator uses `operatorIndex`, a parameter uses `parameterIndex`, a
block/context uses `contextType` (+ `blockIndex` for blocks); either endpoint also accepts an optional
`subPath` of descriptor-named **child sub-slots** to descend into a compound slot — e.g. link a float
into a Sphere slot's `radius` with `to.subPath:["radius"]`, or a nested `["transform","position"]`;
`unlink_slots` `target` takes the same `subPath`), `set_slot_value` (write a constant into
an unlinked input slot: `target` is the same `{node, …address, slot}` shape as a `link_slots` endpoint;
the bare op coerces `value` to the slot's type — number, bool, `[x,y,z]` vector, `[r,g,b,a]` color, or
an **asset path string for an Object-typed slot** (Texture2D/Texture3D/Cubemap/Mesh — loaded by path,
e.g. an Output's `mainTexture` slot), a **curve** (`{"keys":[{"time":0,"value":0},{"time":1,"value":5}]}`
for an AnimationCurve slot), or a **gradient** (`{"colorKeys":[{"color":{"r":1,"g":0,"b":0,"a":1},"time":0},…],
"alphaKeys":[{"alpha":1,"time":0},…]}` for a Gradient slot) — while an optional `subPath` walks into a
compound value struct, e.g. `["center"]` sets a sub-vector and
`["size","x"]` sets one nested component, leaving the rest untouched; describe re-reads it via
`inputSlots[].value` (gradients surface as `{colorKeys,alphaKeys,mode}`). `link_slots` applies VFX's
implicit type conversion automatically (e.g. a float output into a Vector3 input broadcasts)),
`set_slot_space` (set the coordinate `space` — `World`/`Local`/`None` — of a spaceable slot
(Position/Vector/Direction-style); `target` is the same `{node, …address, slot}` shape; non-spaceable
slots return a clear error; describe surfaces it as `inputSlots[].space`), `unlink_slots` (break a slot connection: `target` is the input-slot endpoint
`{node, …address, slot}` whose link(s) to remove — by default all of them, or pass a specific `from`
output-slot endpoint to remove just that edge; returns `linksRemoved`/`remainingLinks`, and describe
re-reads it via `inputSlots[].hasLink`/`links`), `convert_to_property` (promote an inline-constant
operator — `target` `{node:"operator", operatorIndex}` of an Operator/Inline node like `float`/`Vector2`
— into a blackboard parameter, carrying its value + output links; `name` sets the exposed name and
`exposed` (default false) toggles blackboard exposure) and `convert_to_inline` (the inverse: bake a
parameter — `target` `{node:"parameter", parameterIndex}` — back into an inline-constant operator,
value + links intact), the `remove_*` family (`remove_block` by
`contextType`+`blockIndex`, `remove_operator` by `operatorIndex`, `remove_parameter` by
`parameterIndex`, `remove_context` by `contextType` or `index` — each unlinks the node's slots (and a
context's flow edges) before deleting, so no dangling links remain; describe's counts/arrays shrink and
the response reports `remaining*`), the **blackboard-management** ops (all by `parameterIndex`):
`rename_parameter` (`exposedName` = new name; the node + its slot links survive — same VFXParameter;
rejects a duplicate name), `set_parameter_category` (`category` string — a new string creates that
category), `rename_category` (`category` → `newCategory` across every parameter in it; reports
`parametersMoved`), `reorder_category` (`category`+`toIndex` — moves a whole category within the
blackboard's display order; it lazily syncs `VFXUI.categories` from the params first, so describe's
top-level `categories[]` array — empty until the first category op — reflects the order), `reorder_parameter`
(`order` int — position within the category), and
`duplicate_parameter` (clones type/default/category with `order`+1 and name `"<name> (1)"`, or an explicit
`exposedName`); describe's `parameters[]` carries `exposedName`/`category`/`order` to confirm each. And
`link_flow` (context→context flow
edge, e.g. an Event context into Spawn: `from`/`to` are `{contextType}` or `{index}`, with optional
`fromIndex`/`toIndex` flow-slot indices) and its companion `unlink_flow` (same `from`/`to` endpoints;
removes just that single flow edge — sibling edges and the rest of the chain stay; re-add with
`link_flow`), and `set_bounds` (write the Initialize context's particle
bounds: `mode` switches `boundsMode` Manual/Recorded/Automatic; `center`/`size` (Vector3 arrays) write
the bounds AABox when the mode exposes one; `padding` writes `boundsPadding` for Recorded/Automatic),
and `add_sticky_note` (UI metadata: `title`, `contents`, optional `position` (`[x,y,width,height]`),
`colorTheme` int 1–3, `textSize` "Small"/"Medium"/"Large"/"Huge"), `update_sticky_note` (edit an
existing note by `index` — only the supplied fields change, the rest stay), `remove_sticky_note`
(delete by `index`; describe's `stickyNotes[]` array shrinks) and `reorder_sticky_note` (move the note at
`index` to `toIndex` — the array position is the note's order; describe's `stickyNotes[]` reflects the new
order). *(Fit-to-text is a UI text-measurement with no model-level size, so it's out of scope headless.)*
And `set_instancing` (write the
asset's `VisualEffectResource.instancingMode` — values are `Auto`/`Custom`/`Disabled` (`Custom` =
explicit force-enable with a custom batch capacity) — and optional `capacity` int; describe surfaces
`instancing` as `{mode, capacity, disabledReason}`, where `disabledReason` is the graph-level
force-disable validation (`OutputEvent`/`MeshOutput`/`None`)), and `set_initial_event_name` (`eventName` = the asset's default play event,
default `"OnPlay"`; stored on the resource's `m_Infos.m_InitialEventName`; describe surfaces it as a
top-level `initialEventName`). Describe surfaces sticky notes via a top-level `stickyNotes` array and the
resource's current instancing via a top-level `instancing: {mode, capacity}` block.

**Events** are mostly compose-only. *GPU events:* a `Trigger Event|<Mode>` block (`On Die`/`Over Time`/
`Always`/…) in Update has a `evt` GPU-event output slot; `link_slots` it into a `GPU Event` context's
`evt` input (the context's `contextType` is `SpawnerGPU`), then `link_flow` that context into a second
system's Initialize — particles spawn particles. *Event payloads:* a `Set SpawnEvent <Attribute>` block
on the Spawner carries an attribute on the spawn event (readable in Initialize via a Source attribute,
see Attributes). *Output events:* `add_context "Output Event"` (`contextType` `OutputEvent`) is the CPU
callback endpoint — authoring is headless; the C# callback fires only in play mode.

Templates: `vfx_list_library kind:"template"` enumerates the VFX package's built-in starter templates
(`01_Minimal_System` … `06_Firework`) with their on-disk paths. `create_from_template`
(`targetPath` = new `.vfx`, `template` = a template name or explicit `.vfx` path) instantiates a fresh
graph by copying the template's serialized graph (`VisualEffectAssetEditorUtility.CreateTemplateAsset`)
— the result is a real describable graph, not an empty asset. `insert_template` (`assetPath` = an
*existing* graph, `template` = name/path) **merges** the template's nodes into that graph instead of
making a new asset: it clones every top-level context/operator/parameter (with the template's internal
flow + slot links and nested blocks intact) via `VFXMemorySerializer.DuplicateObjects` and adds them
as a new disjoint system alongside the existing ones (response `addedNodes`/`addedTypes`).
`designate_template` (`assetPath` = an existing `.vfx`, `name` required, optional `category`/`description`/
`icon`/`thumbnail` by Texture2D path) marks that asset as a **custom template** so it appears in the
Templates window — it writes the VFX importer's template metadata + `useAsTemplate` flag via the package's
`VFXTemplateHelperInternal.TrySetTemplateStatic` and reimports. Describe surfaces it as the top-level
`template` field (`{name, category, description}`; null when the asset isn't a template).

Systems: a full particle system is just the descriptor chain Init→Update→Output sharing one
`VFXDataParticle`. Build a fresh system by `add_context "Initialize Particle"` +
`add_context "Update Particle"` + `add_context "Output Particle|Unlit|Quad"` (or another Output
variant), then `link_flow` them by `{index}` — `VFXContext.LinkTo` auto-merges the contexts'
`VFXData`, so each chain becomes its own system. Describe emits a `dataInstanceId` per context;
equal ids prove system membership, different ids prove disjoint systems. Use this to verify a
from-scratch chain landed in a fresh system rather than accidentally attaching to an existing one.
Variants compose the same way: a **particle-strip** system is `Initialize Particle Strip` →
the shared `Update Particle` → `Output ParticleStrip|Shader Graph|Quad` (Init Strip seeds
`ParticleStrip` data); a **mesh-output** system swaps the output for `Output Particle|Unlit|Mesh`,
and a standalone `Output Single Mesh` (`VFXStaticMeshOutput`) is its own single-context system that
force-disables instancing (`instancing.disabledReason:"MeshOutput"`). Name any system with
`set_system_name` (see above) — describe surfaces `contexts[].systemName`. **To CHANGE an existing
system's output type** (e.g. quad → strip or mesh) there is no convert op: `remove_context` the old
Output (and, for a strip, the old `Initialize Particle` too), then `add_context` the variant with
`linkFrom`, and `link_flow` the chain back together. **Verify the variant landed** with describe's
`contexts[].type` (the underlying class): a quad output is `VFXPlanarPrimitiveOutput`, a strip output
`VFXComposedParticleStripOutput`, a per-particle mesh output `VFXMeshOutput`, a static single mesh
`VFXStaticMeshOutput`.

Subgraph: `create_subgraph_asset` makes a subgraph asset (`subgraphPath` + `kind`). For
`kind:"block"|"operator"` it copies the package's default `.vfxblock`/`.vfxoperator` template; the
parent references it by adding the matching library node (`add_block "Empty Subgraph Block"` /
`add_operator "Empty Subgraph Operator"`) and writing the asset path into `m_Subgraph` via
`set_block_setting`/`set_operator_setting`. For `kind:"system"` it creates a plain `.vfx`
(`VisualEffectAssetEditorUtility.CreateNewAsset`) — author content in it like any graph — and the parent
references it differently: `add_context "Subgraph"` + `subgraphPath` (the System subgraph node, a
`VFXSubgraphContext`, is NOT in the node library, so add_context instantiates it directly and points
`m_Subgraph` at the `.vfx`). All three surface the reference as `{type, name, assetPath}` under the
node's `settings.m_Subgraph` (verified end-to-end). **Expose inputs:** add an exposed parameter inside
the subgraph asset (`add_parameter ... exposed:true` against the subgraph's path) — it automatically
surfaces as an input slot on the parent's subgraph node (verified for the operator kind; describe shows
the new `inputSlots[].name`). **Define outputs:** `add_parameter ... isOutput:true` makes the param a
subgraph OUTPUT — it surfaces as an `outputSlots[]` port on the parent's subgraph operator
(`isOutput` forces the param non-exposed and is reported in describe `parameters[].isOutput`).
**Block Suitable Contexts:** a block subgraph asset holds a single `BlockSubgraph` context whose
`m_SuitableContexts` `[VFXSetting]` (flags enum: `Spawner`/`Init`/`Update`/`Output` + combos like
`UpdateAndOutput`, default `InitAndUpdateAndOutput`) controls which contexts accept the block — set it
with `set_context_setting contextType:"BlockSubgraph" setting:"m_SuitableContexts" value:"…"` (surfaced
in describe `contexts[].settings.m_SuitableContexts`). (Convert-selection is UI-coupled and not wired.)

**Set/Get Attribute** also needs no dedicated op. Every `Set <Attribute>` ships as descriptor
`|Set|_<AttrName>` (e.g. `|Set|_Color`, `|Set|_Position`, `|Set|_Lifetime`) — all instantiate the
single `SetAttribute` block class with the `attribute` `[VFXSetting]` pre-wired to the right name.
Every `Get <Attribute>` ships as operator descriptor `Get|_<AttrName>` (e.g. `Get|_Position`,
`Get|_Direction`) and instantiates `VFXAttributeParameter`. Composition mode (Overwrite/Add/
Multiply/Blend), Random (Off/PerComponent/Uniform), Source (Slot/Source), and per-component
`channels` are ordinary `[VFXSetting]` fields writable via `set_block_setting`. Always look up the
exact descriptor with `vfx_list_library kind:"block" filter:"Color"` (or `kind:"operator"
filter:"Position"`) — the leading `|` and embedded `|_` separators are load-bearing.

Custom HLSL needs no dedicated op: the Custom HLSL block (descriptor `"Custom HLSL"`, category `HLSL`)
and Custom HLSL operator (category `Operator/HLSL`) are discoverable via `vfx_list_library` and instantiate
through `add_block`/`add_operator`. Write the inline HLSL function via `set_block_setting`/
`set_operator_setting` with `setting:"m_HLSLCode"` (other settings: `m_BlockName`/`m_OperatorName` for the
displayed name). For an **external file**, set `m_ShaderFile` to the path of a `.hlsl` imported as a
`ShaderInclude` (the Object-by-path coercion loads it; the node sources from the file and ignores inline
code). When the source defines **multiple functions** (each with a `void/scalar Name(...)` signature),
pick which one the node exposes by setting the **function selector** to the function name — `m_AvailableFunction`
on the block, `m_AvailableFunctions` (plural) on the operator; pass the bare name string (the
`MultipleValuesChoice` selection is coerced for you). Describe surfaces these as `settings` (the oracle
reports every `[VFXSetting]`, including `ReadOnly` fields like `m_HLSLCode`; the selector shows as
`{selection, values}`), and the node's input slots are re-parsed from the selected function's signature —
so confirm a source/file/selector change by re-describing the reshaped `inputSlots`. The HLSL parser also
handles **buffer/texture parameter types** (`VFXSampler2D`/`VFXSampler3D` → Texture2D/3D slots,
`StructuredBuffer<T>`/`RWStructuredBuffer<T>` → GraphicsBuffer slots) and **multi-file `#include`**
resolution (an `#include "Other.hlsl"` in the `m_ShaderFile` resolves relative to that file) — both are
automatic, no extra params; just write source/point at a file that uses them and re-describe the slots.
`vfx_describe_graph` reports each context's `settings` (including `boundsMode` on Init), `inputSlots`
(each slot's resolved `value` for unlinked slots — e.g. the bounds AABox center/size), each block's
`settings`, per-context `inputs`/`outputs` flow links, an `operators` array, and a `parameters` array
(each with `exposedName`/`exposed`/`value`/`category`) — every slot carries `links` (resolved node
address + slot index), so confirm changes by re-describing. Use `vfx_list_library` with `kind` (`block`
default, `operator`, `context`, `parameter`, `template`) to discover descriptor/template names.

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
`set_vector4` (`name` = the exposed parameter name), `set_texture`/`set_mesh` (`name` = the exposed
Texture2D/Mesh parameter, `assetPath` = the asset to bind; the exposed param must be USED in the graph
to survive into the runtime sheet — e.g. wire it into an Output's `mainTexture`/`mesh` slot),
`send_event` (`eventName`, plus an optional `attributes` payload object — e.g.
`{"lifetime":2.0,"position":[0,1,0]}` — carried as a `VFXEventAttribute`: numbers become
`SetFloat`, 2–4-element arrays `SetVector2/3/4`, booleans `SetBool`. The payload seeds spawn-event
source attributes that spawned particles inherit when Init reads them with `Source = Source`),
`set_initial_event_name` (`name` = the per-instance
`VisualEffect.initialEventName` override of the asset default; `""` suppresses auto-play; then
`Reinit`), `reinit`, `simulate` (advance the live sim headlessly via `VisualEffect.Simulate` — params
`deltaTime` default 0.05, `steps` default 1), and `get_state` (reports `hasAsset`, `aliveParticleCount`,
`pause`, `playRate`, `initialEventName`, and — when `name` is given — `hasFloat`/`floatValue`,
`hasTexture`/`textureName`, `hasMesh`/`meshName`). Set ops echo `get_state` so you can confirm the
round-trip in one call. **`hasFloat`/`hasTexture`/`hasMesh` are scoped to the single queried `name`** —
e.g. a `set_texture` echo shows `hasFloat:false` (it queried the texture name, not a float); query each
param by its own name. The value round-trips (`set_*` → `get_state`) need NO play mode. **Spawning is
different:** `aliveParticleCount` only rises for a *rendered* effect advanced *per frame* (a Camera
framing it + repeated `simulate`/`Simulate` with frame yields, HANDOFF §6b) — a single edit-mode
`simulate` call won't spawn, so spawn/aliveParticleCount verification belongs in a play-mode harness.

For **VFX environment settings** (not a graph), use `vfx_settings` with a `scope`:

```bash
# Project settings — ProjectSettings/VFXManager.asset (shared with the team)
unity-cli raw vfx_settings --json '{"op":"get"}'
unity-cli raw vfx_settings --json '{"op":"set","setting":"fixedTimeStep","value":0.02}'
unity-cli raw vfx_settings --json '{"op":"set","setting":"maxCapacity","value":50000000}'
# Object-ref plumbing (compute shaders + runtime resources) — get surfaces them as {type,name,assetPath};
# settable by asset path (usually Unity-managed defaults — only override if you know why):
unity-cli raw vfx_settings --json '{"op":"set","setting":"m_SortShader","value":"Packages/com.unity.visualeffectgraph/Shaders/Sort.compute"}'

# Per-machine preferences — EditorPrefs via UnityEditor.VFX.VFXViewPreference
unity-cli raw vfx_settings --json '{"op":"get","scope":"preferences"}'
unity-cli raw vfx_settings --json '{"op":"set","scope":"preferences","setting":"instancingEnabled","value":false}'
unity-cli raw vfx_settings --json '{"op":"set","scope":"preferences","setting":"displayExperimentalOperator","value":true}'
unity-cli raw vfx_settings --json '{"op":"set","scope":"preferences","setting":"allowShaderExternalization","value":true}'
```

`scope:project` (default) returns a `properties` block (public static `UnityEngine.VFX.VFXManager`
props — `fixedTimeStep`/`maxDeltaTime`, round-trip immediately on re-read) and a `serialized` block
(asset fields `m_MaxCapacity`/`m_MaxScrubTime`/`m_BatchEmptyLifetime`). `set` writes through the
static property when one exists (`via:"property"`), else the matching serialized field
`m_PascalCase` (`via:"serialized"`).

`scope:preferences` returns the canonical VFX editor preferences read via `VFXViewPreference` static
properties — `instancingEnabled` (the **Instancing master gate** for #16's 3-gate reconciliation),
`displayExperimentalOperator`, `multithreadUpdateEnabled`, `forceEditionCompilation`,
`generateShadersWithDebugSymbols`, `advancedLogs`, `cameraBuffersFallback` (enum surfaced by name),
`authoringPrewarmStepCountPerSeconds`/`authoringPrewarmMaxTime`, plus `displayExtraDebugInfo` and
`visualEffectTargetListed`, and `allowShaderExternalization` (this one has no public getter property,
so it's read/written via `EditorPrefs` directly by its key constant). `set` writes the matching
`EditorPrefs.SetBool/SetInt/SetFloat` and calls `VFXViewPreference.SetDirty()` so the next re-read
returns the new value; the result echoes the resolved `editorPrefsKey` (e.g. `VFX.InstancingEnabled`,
`VFX.allowShaderExternalization`).

To **bake a Mesh into a Signed Distance Field Texture3D** (the programmatic SDF Bake Tool), use `vfx_bake_sdf`:

```bash
unity-cli raw vfx_bake_sdf --json '{"meshPath":"Assets/Models/Statue.fbx","outputPath":"Assets/SDF/Statue.asset","maxResolution":64}'
unity-cli raw vfx_bake_sdf --json '{"meshPath":"Assets/Models/Statue.fbx","outputPath":"Assets/SDF/Statue.asset","maxResolution":32,"center":[0,1,0],"size":[2,2,2],"signPassCount":2,"threshold":0.5,"overwrite":true}'
```

`vfx_bake_sdf` consumes a *Mesh asset* and produces a *Texture3D `.asset`* — it does NOT touch a `.vfx`
graph. It uses the package's public `MeshToSDFBaker` (construct → `BakeSDF()` → read back the 3D SDF
RenderTexture → save as Texture3D). Params: `meshPath` + `outputPath` (required; output must be under
`Assets/` and end in `.asset`), `maxResolution` (voxels on the longest axis, default 64), `center`/`size`
(`[x,y,z]` baking box; default = the mesh's bounds — fit-to-mesh), `signPassCount` (default 1),
`threshold` (default 0.5), `sdfOffset` (default 0), `overwrite` (default false). Returns the actual
`resolution` (grid), `actualBoxSize`, and the asset `guid`. To then drive a VFX with it, wire an exposed
Texture3D parameter into a Distance Field input (e.g. a Collision/Conform-to-SDF block) and at runtime
`vfx_runtime set_texture`. **Requires compute shader support** (baking is GPU work) — returns a clear
error if unavailable.

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
- "Add a Set Color block to Init in Add composition mode, plus a Get|_Position operator."
- "Build a second particle system in this graph from scratch — Init→Update→Output — and confirm it's disjoint from the first."
- "List the built-in VFX templates and create a new graph from the Simple Burst template."
- "Read the VFX project settings, then set the fixed time step to 0.02 and confirm it stuck."
- "Turn off the global VFX instancing-enabled preference, then turn it back on."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
