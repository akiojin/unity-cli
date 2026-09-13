# Graph Ops Reference

## Table of Contents

- [Addressing](#addressing)
- [Compile / verify](#compile--verify)
- [Blocks](#blocks)
- [Contexts and systems](#contexts-and-systems)
- [Operators](#operators)
- [Slots and links](#slots-and-links)
- [Blackboard](#blackboard)

Every op is `unity-cli raw vfx_apply --json '{"op":"<name>","assetPath":"<.vfx>", …}'`. Every op that
persists the graph triggers a recompile and returns a `compile` block:

```json
"compile": { "success": true, "exception": null, "errors": [], "logs": [] }
```

`success:false` means the asset no longer compiles: `exception` is the compiler exception (e.g. `An
expression can only take up to 4 parent expressions`), `errors` are Compilation-tier reports, `logs`
are the Error/Exception console lines emitted during the import. Fix before continuing. An op that
returns `{"error": …}` instead was **not applied**.

## Addressing

- **Block:** `contextType` (the *first* context of that type: `Spawner`/`Init`/`Update`/`Output`/
  `Event`/`SpawnerGPU`/`OutputEvent`/…) **or** `contextIndex` (absolute index from describe), plus
  `blockIndex`. `move_block`/`duplicate_block` address the destination with `toContextType`/`toContextIndex`.
- **Context:** `contextType` or `index` (alias `contextIndex`).
- **Operator:** `operatorIndex`. **Parameter:** `parameterIndex`. All indices come from describe and
  shift when nodes are removed — re-describe after removals.
- **Slot endpoint** (link/unlink/set_slot_value/set_slot_space): `{node: operator|parameter|context|
  block, …address, slot: <top-level index>}` with optional `subPath: [<child name>…]` to descend into
  a compound slot (`["radius"]`, `["transform","position"]`), and `activation: true` to target a block's
  activation port instead of an input slot.

## Compile / verify

- `compile` — force a recompile (reimport) and report: `{compile:{…}, validationErrors:[…], ok}`. `ok`
  is true only when the compile succeeded **and** there is no Error-tier validation entry.
- Describe (`vfx_describe_graph`) always carries `errors` (Invalidate + Compilation tiers, plus a
  synthetic `CompileException` entry when the last import threw), `compile` (the last summary for the
  asset this editor session, `null` if it has not been compiled since the editor started — run
  `compile`), and `layout` (see the layout reference). Pass `includeErrors:false` to skip error
  collection. A `Warning` such as `NeedsRecording` on Init is benign.

## Blocks

- `add_block` — `blockName` (descriptor name, exact then contains), context address (default `Update`),
  optional `index` (insert position) and `settings` `{name: value}` (same coercion as
  `set_block_setting`; an unknown setting fails and the block is not added). Response carries
  `contextIndex`/`blockIndex`.
- `set_block_setting` — `setting` + `value`: writes a `[VFXSetting]` field (describe `blocks[].settings`).
  Enum fields take the enum name, Object fields take an asset path, the Custom HLSL function selector
  takes the function name. Some settings reshape the block's slots — re-describe.
- `set_block_enabled` — `enabled` bool (static toggle; describe `blocks[].enabled`). For *dynamic*
  per-particle/frame activation link a bool output into the block's activation port instead:
  `link_slots … "to":{…block address…,"activation":true}` (describe `blocks[].activationSlot`).
- `reorder_block` — `toIndex` within the context.
- `move_block` — `toContextType`/`toContextIndex` (+ optional `toIndex`); validated with
  `VFXContext.Accept`, an incompatible target returns an error.
- `duplicate_block` — clone (same settings + slot values, fresh GUIDs, slots unlinked); optional `index`
  and `toContextType`/`toContextIndex`.
- `remove_block` — unlinks the block's slots first so nothing dangles.

**Set/Get attribute.** Every `Set <Attribute>` block is the descriptor `|Set|_<Name>` (`|Set|_Color`,
`|Set|_Position`, `|Set|_Lifetime`, …) instantiating the generic `SetAttribute` block; every
`Get <Attribute>` operator is `Get|_<Name>`. Composition (`Overwrite`/`Add`/`Multiply`/`Blend`),
`Random` (`Off`/`PerComponent`/`Uniform`), `Source` (`Slot`/`Source`) and `channels` (`X`/`XY`/…) are
ordinary settings. Custom attributes: `add_custom_attribute` (`attributeName`, `attributeType` =
`Float`/`Vector2`/`Vector3`/`Vector4`/`Bool`/`Uint`/`Int`, optional `description`/`isReadOnly`;
describe `customAttributes[]`), then add any `|Set|_X` / `Get|_X` and repoint it with
`set_block_setting setting:"attribute" value:"<Name>"`.

## Contexts and systems

- `add_context` — `contextName` (descriptor, e.g. `Initialize Particle`, `Update Particle`,
  `Output Particle|Unlit|Quad`, `Output Particle|Shader Graph|Quad`, `Event`, `GPU Event`,
  `Output Event`, `Initialize Particle Strip`, `Output Single Mesh`), optional `settings`
  (e.g. `{"eventName":"Burst"}`), `linkFrom` (an existing context type flows into the new one, with
  `fromIndex`/`toIndex` flow-slot indices), `position:[x,y]`. `contextName:"Subgraph"` +
  `subgraphPath` adds a system-subgraph node (see systems-and-events.md).
- `set_context_setting` — `setting` + `value`. Resolution order and the response `via`: the context's
  own `[VFXSetting]` (`context`), the shared particle data (`data`: Init `capacity`, `stripCapacity`,
  `boundsMode`), a composed output's nested setting (`context-composed`: a Shader Graph output's
  `shaderGraph`), then a writable property (`context-property`/`data-property`: simulation `space`
  = `Local`/`World`, which applies to the whole system; describe `contexts[].simulationSpace`).
  Common settings: Spawner `loopDuration`/`loopCount`/`delayBeforeLoop` (`Constant`/`Random`/…),
  Update `ageParticles`/`reapParticles`, Output `blendMode`/`uvMode`/`sortMode`/`castShadows`.
  **Flipbook:** `uvMode:"Flipbook"` exposes a `flipBookSize` input slot (`set_slot_value` with
  `subPath:["x"|"y"]`); frame blending / motion vectors are the separate bools
  `flipbookBlendFrames`/`flipbookMotionVectors`.
- `remove_context` — unlinks flow edges and data slots before deleting.
- `delete_system` — removes every context sharing the addressed context's `VFXData` (Init/Update/Output
  of one system); address any member. Spawn/Event contexts have no data and cannot address a system.
- `set_system_name` — `name`; written to `VFXData.title` (particle system) or the Spawner's label;
  describe `contexts[].systemName`.
- `link_flow` / `unlink_flow` — `from`/`to` = `{contextType}` or `{index}` (alias `contextIndex`),
  optional `fromIndex`/`toIndex` flow-slot indices. `LinkTo` auto-merges the contexts' `VFXData`, so
  a fresh Init→Update→Output chain becomes its own system (describe `dataInstanceId` proves membership).
- `set_bounds` — `mode` (`Manual`/`Recorded`/`Automatic`), `center`/`size` `[x,y,z]` (when the mode
  exposes the `bounds` slot), `padding` (Recorded/Automatic). Targets Init by default; `contextIndex`
  addresses another data-bearing context.

## Operators

- `add_operator` — `operatorName` (descriptor; e.g. `Add`, `Multiply`, `Sample Curve`, `Random|Float`,
  `Get|_Position`, `Custom HLSL`, inline constants `float`/`Vector2`/…), optional `settings` and
  `position:[x,y]`. Response carries `operatorIndex`.
- `set_operator_setting` — `setting` + `value` (mirror of `set_block_setting`; e.g. Custom HLSL
  `m_HLSLCode`/`m_OperatorName`/`m_ShaderFile`/`m_AvailableFunctions`, subgraph `m_Subgraph`, inline
  `m_Type`). A Custom HLSL operator that would expose more than 4 inputs is refused and reverted.
- `duplicate_operator`, `remove_operator` (unlinks first).
- Dynamic operators: **cascaded** operators (`Add`/`Multiply`/`Append`/… — the `+`/`−` in the UI) take
  `add_operator_input` (optional `operandType`), `remove_operator_input` (optional `index`, default
  last; refuses to drop below the minimum, normally 2), `rename_operator_input` (`index`+`name`) and
  `reorder_operator_input` (`index`+`toIndex`, links survive). `set_operator_operand_type` retypes
  operands: **uniform** operators (`Sine`/`Distance`/…) take just `operandType`; unified/cascaded
  operators take an optional `index` (else every operand). `operandType` must be one of the operator's
  valid types (`Float`/`Vector2`/`Vector3`/`Vector4`/…); describe `inputSlots[].valueType` reflects it.
  Non-dynamic operators return a clear error.

## Slots and links

- `link_slots` — `from` (an **output** slot endpoint) → `to` (an **input** slot endpoint). VFX implicit
  conversion applies (a float into a Vector3 input broadcasts); an incompatible pair is rejected. An
  input holds one link — linking again replaces it.
- `unlink_slots` — `target` = the input endpoint; removes all its links, or only the edge from an
  optional `from` output endpoint. Returns `linksRemoved`/`remainingLinks`.
- `set_slot_value` — write a constant into an **unlinked** input: `target` endpoint (+ optional
  `target.subPath` to a child slot), `value` coerced to the slot type — number, bool, `[x,y,z]`,
  `[r,g,b,a]`, an asset path for Object slots (Texture2D/3D/Cubemap/Mesh, e.g. an Output's
  `mainTexture`), a curve `{"keys":[{"time":0,"value":0},…]}`, a gradient
  `{"colorKeys":[{"color":{"r":1,"g":0,"b":0,"a":1},"time":0},…],"alphaKeys":[{"alpha":1,"time":0},…]}`.
  A top-level `subPath` walks the *value struct* instead and sets one field, leaving the rest
  (`["center"]`, `["size","x"]`). Writing to a linked slot returns a `warning` — the link wins.
- `set_slot_space` — `space` = `World`/`Local`/`None` on a spaceable slot (Position/Vector/Direction);
  describe `inputSlots[].space`.
- `convert_to_property` — promote an inline-constant operator (`target` `{node:"operator",operatorIndex}`)
  into a blackboard parameter (`name`, `exposed` default false), value + links carried over;
  `convert_to_inline` is the inverse (`target` `{node:"parameter",parameterIndex}`).
- Describe: every slot carries `links` (resolved node address + top-level slot index, plus a `subPath`
  when the edge lands on a compound child), `hasLink` (own links only), `value`, `valueType`; compound
  slots whose children are linked surface a pruned `children[]` array.

## Blackboard

- `add_parameter` — `parameterName`, `type` (`Bool`/`Int`/`Uint`/`Float`/`Vector2`/`Vector3`/
  `Vector4`/`Color`/`Texture2D`/`Texture3D`/`Cubemap`/`Gradient`/`Animation Curve`/`Mesh`; spaces
  optional), optional `value`, `min`/`max` (sets `valueFilter:"Range"`), `tooltip`, `category`,
  `exposed` (default true; `false` = constant), `isOutput` (subgraph output). Response carries
  `parameterIndex`.
- `set_parameter` — edit in place: any of `value`, `min`, `max`, `valueFilter`, `tooltip`, `exposed`,
  `category`, `exposedName`; response `changed[]`.
- `rename_parameter` (`exposedName`, unique), `set_parameter_category` (`category`),
  `rename_category` (`category`→`newCategory`), `reorder_category` (`category`+`toIndex`; syncs
  `categories[]` from the parameters first), `reorder_parameter` (`order`), `duplicate_parameter`
  (optional `exposedName`, default `"<name> (1)"`), `remove_parameter`.
- Describe `parameters[]`: `exposedName`/`exposed`/`isOutput`/`category`/`order`/`value`/`valueFilter`/
  `min`/`max`/`tooltip`, `position` (model position — the seed for the canvas node the editor creates
  for a linked parameter), `nodes[]` (`{id, position}` per existing canvas node). The top-level
  `categories[]` array stays empty until a category op runs; grade grouping off `parameters[].category`.
- A parameter feeds the graph through its output slot: `link_slots "from":{"node":"parameter",
  "parameterIndex":N,"slot":0}`. An exposed parameter that feeds nothing is stripped at compile.
