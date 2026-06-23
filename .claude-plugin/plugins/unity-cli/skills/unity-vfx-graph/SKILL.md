---
name: unity-vfx-graph
description: Author and inspect Unity Visual Effect Graph (vfx) assets with unity-cli. Use when the user wants to read, build, or modify a .vfx graph and its systems, contexts, blocks, operators, or particle behavior, or to discover available blocks. Do not use for generic asset, material, or import operations; use `unity-asset-management` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.13.0
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
unity-cli raw vfx_apply --json '{"op":"set_block_enabled","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"enabled":false}'
unity-cli raw vfx_apply --json '{"op":"reorder_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"toIndex":1}'
unity-cli raw vfx_apply --json '{"op":"move_block","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextType":"Update","blockIndex":0,"toContextType":"Init"}'
unity-cli raw vfx_apply --json '{"op":"add_context","assetPath":"Assets/Basic Graphs/Minimal.vfx","contextName":"Output Particle|Point","linkFrom":"Update"}'
unity-cli raw vfx_apply --json '{"op":"add_operator","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorName":"Add"}'
unity-cli raw vfx_apply --json '{"op":"set_operator_setting","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"setting":"m_HLSLCode","value":"float MyScale(in float k){return k*2.0f;}"}'
unity-cli raw vfx_apply --json '{"op":"add_operator_input","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"operandType":"Vector3"}'
unity-cli raw vfx_apply --json '{"op":"remove_operator_input","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0}'
unity-cli raw vfx_apply --json '{"op":"set_operator_operand_type","assetPath":"Assets/Basic Graphs/Minimal.vfx","operatorIndex":0,"operandType":"Vector2"}'
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
unity-cli raw vfx_list_library --json '{"kind":"template"}'
unity-cli raw vfx_apply --json '{"op":"create_from_template","targetPath":"Assets/Basic Graphs/Burst.vfx","template":"03_Simple_Burst"}'
unity-cli raw get_compilation_state --json '{}'
```

`vfx_apply` ops: `add_block` (descriptor by name), `set_block_setting` (target a block by `contextType`
+ `blockIndex` from describe, set a `[VFXSetting]` field), `set_block_enabled` (toggle a block's
`enabled` state by `contextType`+`blockIndex`+`enabled` bool — describe surfaces `blocks[].enabled`),
`reorder_block` (move a block to `toIndex` within its context) and `move_block` (relocate a block to a
compatible `toContextType` — validated via `VFXContext.Accept`, so an incompatible target returns a
clear error rather than corrupting the graph), `set_operator_setting` (symmetrical to
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
and `inputSlots` grow/shrink. Non-dynamic operators return a clear error), `set_context_setting` (set a `[VFXSetting]` on a context by `contextType` or `index` — Spawn
loop settings (`loopDuration`/`loopCount`/`delayBeforeLoop`), Update toggles (`ageParticles`/
`reapParticles`), Output blend/UV/shader knobs (`blendMode`/`uvMode`); also reaches the context's
particle **data** as a fallback, so Init `capacity`/`stripCapacity` work too — the response's `via` field
reports `context` vs `data`; it also falls back to a writable **property** (`via:"…-property"`) for
settings that aren't `[VFXSetting]` fields, notably **simulation space** (`setting:"space"`,
`value:"Local"|"World"` — applies to the whole system since space lives on the shared particle data,
surfaced in describe as `contexts[].simulationSpace`); `contexts[].settings` in describe reflects field
writes), `delete_system` (delete a whole particle system in one op — every context sharing the addressed
context's `VFXData`, i.e. the Init/Update/Output of one system; address any member by `contextType` or
`index`; the cascade matches `remove_context` so a disjoint system is left intact — the response reports
`removedContexts`/`removedContextTypes`/`remainingContexts`), `add_custom_attribute` (declare a
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
`tooltip`/`category`), `link_slots` (connect a
`from` output slot to a `to` input slot; each endpoint is `{node: operator|parameter|context|block,
…address, slot: index}` where an operator uses `operatorIndex`, a parameter uses `parameterIndex`, a
block/context uses `contextType` (+ `blockIndex` for blocks)), `set_slot_value` (write a constant into
an unlinked input slot: `target` is the same `{node, …address, slot}` shape as a `link_slots` endpoint;
the bare op coerces `value` to the slot's type — number, bool, `[x,y,z]` vector, `[r,g,b,a]` color —
while an optional `subPath` walks into a compound value struct, e.g. `["center"]` sets a sub-vector and
`["size","x"]` sets one nested component, leaving the rest untouched; describe re-reads it via
`inputSlots[].value`), `unlink_slots` (break a slot connection: `target` is the input-slot endpoint
`{node, …address, slot}` whose link(s) to remove — by default all of them, or pass a specific `from`
output-slot endpoint to remove just that edge; returns `linksRemoved`/`remainingLinks`, and describe
re-reads it via `inputSlots[].hasLink`/`links`), the `remove_*` family (`remove_block` by
`contextType`+`blockIndex`, `remove_operator` by `operatorIndex`, `remove_parameter` by
`parameterIndex`, `remove_context` by `contextType` or `index` — each unlinks the node's slots (and a
context's flow edges) before deleting, so no dangling links remain; describe's counts/arrays shrink and
the response reports `remaining*`), the **blackboard-management** ops (all by `parameterIndex`):
`rename_parameter` (`exposedName` = new name; the node + its slot links survive — same VFXParameter;
rejects a duplicate name), `set_parameter_category` (`category` string — a new string creates that
category), `rename_category` (`category` → `newCategory` across every parameter in it; reports
`parametersMoved`), `reorder_parameter` (`order` int — position within the category), and
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
existing note by `index` — only the supplied fields change, the rest stay) and `remove_sticky_note`
(delete by `index`; describe's `stickyNotes[]` array shrinks), and `set_instancing` (write the
asset's `VisualEffectResource.instancingMode` — values include `Auto`/`Disabled`/`ForceOn` — and
optional `capacity` int), and `set_initial_event_name` (`eventName` = the asset's default play event,
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
— the result is a real describable graph, not an empty asset.

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
and then writing the asset path into the `m_Subgraph` setting — via `set_block_setting` for the block
kind, `set_operator_setting` for the operator kind. Both ops auto-detect `UnityEngine.Object`-derived
fields and load the value as an asset path via `AssetDatabase`, so describe surfaces the reference as
`{type, name, assetPath}` under the node's `settings.m_Subgraph` (verified end-to-end for both kinds).

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
so confirm a source/file/selector change by re-describing the reshaped `inputSlots`.
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
`set_vector4` (`name` = the exposed parameter name), `set_texture` (`name` = the exposed Texture2D
parameter, `assetPath` = the texture to bind; the exposed param must be USED in the graph to survive
into the runtime sheet), `send_event` (`eventName`), `set_initial_event_name` (`name` = the
per-instance `VisualEffect.initialEventName` override of the asset default; `""` suppresses auto-play;
then `Reinit`), `reinit`, and `get_state` (reports `hasAsset`, `aliveParticleCount`, `pause`,
`playRate`, `initialEventName`, and — when `name` is given — `hasFloat`/`floatValue` plus
`hasTexture`/`textureName`). Set ops echo `get_state` so you can confirm the round-trip in one call.

For **VFX environment settings** (not a graph), use `vfx_settings` with a `scope`:

```bash
# Project settings — ProjectSettings/VFXManager.asset (shared with the team)
unity-cli raw vfx_settings --json '{"op":"get"}'
unity-cli raw vfx_settings --json '{"op":"set","setting":"fixedTimeStep","value":0.02}'
unity-cli raw vfx_settings --json '{"op":"set","setting":"maxCapacity","value":50000000}'

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
