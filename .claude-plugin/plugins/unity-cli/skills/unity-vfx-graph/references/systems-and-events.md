# Systems, Events, Subgraphs, Templates, Asset Settings

## Building a system

A particle system is the chain Init → Update → Output sharing one `VFXDataParticle`. Build one from
scratch with `add_context "Initialize Particle"`, `add_context "Update Particle"`,
`add_context "Output Particle|Unlit|Quad"` (or another Output variant), then `link_flow` them by
`{index}` — `VFXContext.LinkTo` merges the contexts' data, so each chain becomes its own system. A
Spawner (`add_context "Spawn"` or the fixture's existing one) flows into Init. Describe emits
`dataInstanceId` per context: equal ids prove membership, different ids prove disjoint systems.

Variants compose the same way:

- **Particle strip:** `Initialize Particle Strip` → `Update Particle` → `Output ParticleStrip|Shader Graph|Quad`
  (Init Strip seeds `ParticleStrip` data; `stripCapacity` on Init).
- **Mesh output:** `Output Particle|Unlit|Mesh` (per-particle `VFXMeshOutput`); a standalone
  `Output Single Mesh` (`VFXStaticMeshOutput`) is its own single-context system and force-disables
  instancing (`instancing.disabledReason:"MeshOutput"`).
- **Shader Graph output:** `add_context "Output Particle|Shader Graph|Quad"` (a
  `VFXComposedParticleOutput`), then `set_context_setting index:<sgOutput> setting:"shaderGraph"
  value:"<path>.shadergraph"` (response `via:"context-composed"`). The `.shadergraph` must have
  **Support VFX Graph** enabled (a URP target toggle) so it imports as a `ShaderGraphVfxAsset`;
  assigning a shader graph to a plain Unlit output raises `WrongOutputShaderGraph`.

To **change** an existing system's output type there is no convert op: `remove_context` the old
Output (for a strip also the old `Initialize Particle`), `add_context` the variant with `linkFrom`,
and `link_flow` the chain back together. Verify with describe's `contexts[].type`: quad output
`VFXPlanarPrimitiveOutput`, strip `VFXComposedParticleStripOutput`, mesh `VFXMeshOutput`, static mesh
`VFXStaticMeshOutput`. Name systems with `set_system_name`.

## Events

- **Custom events:** `add_context "Event" settings:{"eventName":"Burst"}` then
  `link_flow from:{contextType:"Event"} to:{contextType:"Spawner"}`. Trigger at runtime with
  `vfx_runtime send_event`.
- **GPU events (particles spawning particles):** a `Trigger Event|<Mode>` block (`On Die`/`Over Time`/
  `Always`/…) in Update has an `evt` output; `link_slots` it into a `GPU Event` context's `evt` input
  (that context's `contextType` is `SpawnerGPU`), then `link_flow` the GPU Event context into a second
  system's Initialize.
- **Event payloads:** a `Set SpawnEvent <Attribute>` block on the Spawner carries an attribute on the
  spawn event; Initialize reads it with a Set block whose `Source` setting is `Source`.
- **Output events:** `add_context "Output Event"` (`contextType` `OutputEvent`) is the CPU callback
  endpoint; authoring is headless, the C# callback fires only in play mode. Its presence force-disables
  instancing (`instancing.disabledReason:"OutputEvent"`).

## Subgraphs

`create_subgraph_asset` — `subgraphPath` + `kind`:

- `kind:"block"` / `kind:"operator"` copy the package's default `.vfxblock` / `.vfxoperator`. The
  parent references them by adding the matching library node (`add_block "Empty Subgraph Block"` /
  `add_operator "Empty Subgraph Operator"`) and writing the asset path into `m_Subgraph`
  (`set_block_setting` / `set_operator_setting`).
- `kind:"system"` creates a plain `.vfx`; the parent references it with `add_context "Subgraph"` +
  `subgraphPath` (the System subgraph node is not in the library, so add_context instantiates it
  directly).

All three surface the reference as `{type, name, assetPath}` under the node's `settings.m_Subgraph`.
**Expose inputs:** `add_parameter … exposed:true` inside the subgraph asset surfaces as an input slot
on the parent's node. **Define outputs:** `add_parameter … isOutput:true` surfaces as an output slot
on the parent's subgraph operator (`isOutput` forces the parameter non-exposed). **Block suitable
contexts:** a block subgraph holds one `BlockSubgraph` context whose `m_SuitableContexts` flags enum
(`Spawner`/`Init`/`Update`/`Output`, combos like `UpdateAndOutput`, default `InitAndUpdateAndOutput`)
is set with `set_context_setting contextType:"BlockSubgraph" setting:"m_SuitableContexts"`.
Convert-selection-to-subgraph is UI-coupled and not available.

## Templates

- `vfx_list_library kind:"template"` lists the package's built-in templates (`01_Minimal_System` …
  `06_Firework`) with paths.
- `create_from_template` — `targetPath` (new `.vfx`) + `template` (name or `.vfx` path): a real,
  describable graph.
- `insert_template` — `assetPath` (existing graph) + `template`: merges the template's nodes (flow +
  slot links + blocks intact) as a new disjoint system (response `addedNodes`/`addedTypes`). Run
  `auto_layout` afterwards.
- `designate_template` — `assetPath`, `name`, optional `category`/`description`/`icon`/`thumbnail`
  (Texture2D paths): marks the asset as a custom template for the Templates window; describe
  `template` = `{name, category, description}` (null when not a template).

## Asset-level settings

- `set_instancing` — `mode` `Auto`/`Custom`/`Disabled` (+ optional `capacity`); describe
  `instancing: {mode, capacity, disabledReason}` where `disabledReason` (`OutputEvent`/`MeshOutput`/
  `None`) is the graph-level force-disable. The third gate is the editor preference
  `instancingEnabled` (`vfx_settings scope:"preferences"`).
- `set_initial_event_name` — `eventName` (default `"OnPlay"`; `""` = no auto-play); describe
  `initialEventName`. The per-instance override is `vfx_runtime set_initial_event_name`.
