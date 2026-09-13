# Canvas Layout

## Table of Contents

- [The rule](#the-rule)
- [The layout oracle](#the-layout-oracle-describe)
- [auto_layout](#auto_layout)
- [Manual placement](#manual-placement)
- [Groups and sticky notes](#groups-and-sticky-notes)

A graph an agent authors is opened by a person in the VFX Graph window. Nodes stacked on top of
each other, operators scattered far from what they feed, or systems interleaved make the graph
unreadable even when it compiles and behaves correctly. **Layout is part of the deliverable.**

## The rule

Before reporting any authoring task as done:

1. Run `vfx_apply op:"auto_layout"`. The default scope, `nodes`, places only what you created and
   moves nothing else; a person's hand-placed nodes are theirs.
2. Box the nodes of each feature you added with `group_nodes … note:{…}` so the reader gets the
   group and the note a person would have made; check `nonMembersInside`.
3. Re-describe and confirm `layout.overlapCount` is no higher than the baseline you recorded before
   editing, with no listed overlap involving a node you created. Quote it in the completion report.

Add ops auto-place new nodes (a linked context below its flow source, an unlinked one in a new column
to the right, operators staggered down a column to the left), but that only prevents stacking at the
origin; it knows nothing about what a node feeds. `auto_layout` does.

## The `layout` oracle (describe)

```json
"layout": {
  "nodeCount": 12,
  "overlapCount": 2,
  "overlaps": [ { "a": {"kind":"operator","index":3,"name":"Multiply"}, "b": {"kind":"parameter","index":0,"name":"Rate","nodeId":1} } ],
  "bounds": { "x": -1120, "y": 0, "width": 1900, "height": 1650 }
}
```

Node sizes are *estimated* from slot and block counts (the editor measures real sizes with UI Toolkit,
which a headless bridge cannot run) and honor the node's collapse state (a super-collapsed operator is
a small pill, a collapsed one shows only its linked slots). Estimates are deliberately generous — a
reported overlap is a real overlap or a near miss — so a tightly hand-packed graph reports a non-zero
baseline; compare against that baseline, not against zero. `overlaps` lists at most 50 pairs. Parameters count once per canvas node; a linked parameter authored headlessly has no
canvas node yet, so its model `position` (where the editor will create the node) counts instead.

## `auto_layout`

`{"op":"auto_layout","assetPath":"…"}` with optional `scope` (`nodes` default, `touched`, `all`),
`contexts`, `duplicateShared` (default true) and `splitParameters` (default true).

### `scope:"nodes"` — the default: place what you created, move nothing else

Every `vfx_apply` op records the contexts, operators and parameters that appeared this editor session
(`created`) and every link made from a parameter by `link_slots`. The pass:

- **Created operators** go in free space directly left of what they feed, aligned with the consuming
  block's row; an operator feeding another created operator sits one column further left. Several new
  operators feeding the same consumer stack contiguously. Unlinked operators stay where `add_operator`
  put them.
- **Parameter links made this session** each get a canvas node within reach of the consumer: a new
  parameter's auto-created node is moved into the stack beside its consumer; an existing parameter reuses
  a node of its own already within ~600 px, else gets a new node there. Nodes for one consumer form one
  contiguous stack (`placedParameterNodes[]`, `parameterNodesCreated`, `parameterLinksReusedNode`).
- **Nothing that existed before moves** (`existingNodesMoved`), with one exception a person would also
  make: a context that gained blocks and now covers the next context of its own system pushes that
  lower part of the system down — its contexts, the feeders that drive them, and the sticky notes in
  that band — vertically only (`shiftedDown[] = {grownContext, fromContext, deltaY, nodesMoved}`).
- **A system whose contexts were all created this session** is handed to the system-level pass and
  placed in a fresh column right of everything (`newSystemsPlaced`, `newSystemsPass`).
- Sticky notes are never moved otherwise (`stickyNotesOverNodes` counts notes left over a node you placed).
- With nothing created this session (or after a domain reload, which forgets the marks) the op returns
  an error asking for an explicit scope rather than guessing.

### `scope:"touched"` / `scope:"all"` / `contexts:[…]` — whole-system passes (on request only)

These re-lay out entire systems: every feeder column is rebuilt, shared operators are duplicated per
system and parameters split into one node per consumer band. On a person's working graph that
rearranges most of their canvas for a one-block edit, so use them only when the user asks for a tidy-up.

- `touched` lays out only the systems containing nodes edited this session (describe shows them under
  `touched`); untouched systems, their feeders and group boxes stay exactly where they are. A laid-out
  system is anchored where its contexts sit (feeders grow to its left); if the tidy block would cover
  untouched nodes it is moved below them (`shiftedDown`). A pass clears the touched marks it consumed.
- **Horizontal lanes are the person's.** A system that existed before this session keeps every
  context's x exactly; the pass only re-stacks its contexts vertically and rebuilds the feeder columns
  to its left. Only a system whose contexts were ALL created this session is new and is placed in a
  fresh column right of everything (`newSystemsPlaced`). `move_node` enforces the same rule.
- `all` applies the pass to every system; `contexts:[<describe index>, …]` to the systems containing
  those contexts.
- Sticky notes are never obstacles; a note that is a member of a group box travels with that group
  (`stickyNotesMovedWithGroups`), ungrouped notes are never moved.
- **Systems.** Contexts are grouped by flow connectivity; each connected group is one column laid out
  top-to-bottom by flow depth (Spawn → Init → Update → Output); parallel branches sit side by side.
- **Feeders belong to their system.** Operators and parameter nodes sit in columns immediately LEFT of
  the system they drive, ranked by distance from the contexts they feed (rank 1 feeds a context/block
  directly, rank 2 feeds a rank-1 operator, …): one column per rank, each node vertically aligned with
  what it feeds and pushed apart so nothing overlaps.
- **Shared nodes are duplicated, not stretched.** An operator feeding more than one system is cloned per
  system (`duplicateShared`); a parameter gets one canvas node per system, per group box its consumers
  sit in, plus one per band of consumers more than ~600 px apart (`splitParameters`). Pass `false` to
  either flag to keep the node set untouched. Operator indices change after this — re-describe.
- Response: `{scope, systemsLaidOut, systemsLeftAlone, laidOutContexts[], shiftedDown, duplicatedOperators,
  parameterNodesCreated, groupsRefit, stickyNotesOverNodes, layout}`.

## Manual placement

New nodes never land on existing ones: `add_context`, `add_operator`, `duplicate_operator` and
`insert_template` check the spot they are about to use (auto-placed or your explicit `position`)
against every canvas node and sticky note and move to the nearest open space below it (then
neighbouring columns) when it is taken — the response says `positionAdjusted: true` and reports the
final `position`. An inserted template lands as a block in a fresh column right of the canvas.

- `add_context` / `add_operator` accept `position:[x,y]`.
- `move_node` — `target` = `{node: context|operator|parameter, …index}` + `position:[x,y]`. Blocks have
  no canvas position (they are ordered inside their context; use `reorder_block`/`move_block`). A
  parameter moves all its canvas nodes (staggered vertically); for a linked parameter with no canvas
  node yet the position seeds the node the editor auto-creates.
- Convention: flow reads top-to-bottom, operators sit left of what they feed, upstream above
  downstream, parameters furthest left. Contexts are roughly 440 px wide; leave ≥ 100 px between
  columns and ≥ 50 px between stacked nodes.

## Groups and sticky notes

A person boxes the nodes of one feature and keeps a note explaining it inside the box. Do the same for
what you add.

- `group_nodes` — `title` + `nodes:[…addresses]`; creates the group when the title is new. Node
  addresses are `{node: context|operator|parameter, …index}` (a parameter entry may name one canvas
  node with `nodeId`; default is the node nearest the other members) or `{node:"stickyNote", index}`
  for an existing note. Optional `note:{title, contents, colorTheme?, textSize?, width?, height?}`
  creates the explanatory sticky note in free space just left of the members and makes it a member.
  The box is refit around its members (unless an explicit `position:[x,y,w,h]` is given on creation).
  Blocks cannot be grouped (group the context, or name the block in the note).
  Response: `{groupIndex, createdGroup, contentCount, refit, position, noteIndex, notePosition,
  nonMembersInside[], note}` — `nonMembersInside` lists canvas nodes the box covers without owning
  them; a box like that misleads the reader, so move your members closer together (`move_node`) and
  group again, or leave the box off. Describe `groups[]` = `{title, position, contents[]}`.
- `remove_group` — `title` or `index`; members stay.
- `add_sticky_note` — `title`, `contents`, optional `position:[x,y,w,h]`, `colorTheme` 1–3,
  `textSize` `Small`/`Medium`/`Large`/`Huge`. Like a node, a note never lands on a node: it is nudged
  to the nearest free spot (`positionAdjusted: true`) unless `avoidNodes:false`. `update_sticky_note`
  (`index` + any fields), `remove_sticky_note` (`index`), `reorder_sticky_note` (`index`+`toIndex`).
  Describe `stickyNotes[]`. Fit-to-text is a UI measurement and is not available headless.
