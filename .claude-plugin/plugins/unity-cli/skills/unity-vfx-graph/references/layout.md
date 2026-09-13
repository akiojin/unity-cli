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

1. Run `vfx_apply op:"auto_layout"` — unless the user placed nodes by hand and asked to keep them, in
   which case fix overlaps with `move_node` instead.
2. Re-describe and confirm `layout.overlapCount == 0`. Quote it in the completion report.

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
which a headless bridge cannot run), and deliberately generous — a reported overlap is a real overlap
or a near miss. Parameters count once per canvas node; a linked parameter authored headlessly has no
canvas node yet, so its model `position` (where the editor will create the node) counts instead.

## `auto_layout`

`{"op":"auto_layout","assetPath":"…"}` with optional `scope`, `contexts`, `duplicateShared` (default
true) and `splitParameters` (default true). Sticky notes are never moved.

- **Scope — default `"touched"`.** Every `vfx_apply` op fingerprints the graph (settings, values,
  links; not positions) before and after, and records the contexts, operators and parameters that
  appeared or changed for this asset in this editor session; describe shows them under `touched`.
  `auto_layout` lays out only the systems those nodes belong to (a touched operator/parameter selects
  the system it feeds) and leaves every other system, its feeders, and its group boxes exactly where
  they are. A laid-out system is anchored where its contexts currently sit (feeders grow to its
  left); if the tidy block would cover untouched nodes it is moved below them (`shiftedDown`). A
  layout pass clears the touched marks for the systems it laid out. With nothing touched (or after a
  domain reload, which forgets the marks) the op returns an error asking for an explicit scope
  rather than silently re-laying out someone's canvas.
- **Horizontal lanes are the person's.** A system that existed before this session keeps every
  context's x exactly (its branches stay in their lanes); the pass only re-stacks its contexts
  vertically and rebuilds the feeder columns to its left. Only a system whose contexts were ALL
  created this session is new and is placed in a fresh column right of everything on the canvas
  (`newSystemsPlaced`). `move_node` enforces the same rule: a horizontal move of an existing
  context keeps x and reports `note`; a context created this session moves freely.
- `scope:"all"` applies the pass to every system (each in place, per the rule above).
  `contexts:[<describe index>, …]` lays out the systems containing those contexts, anchored in place.
- Sticky notes are never obstacles (a note must not push a system away). A note that is a member
  of a group box travels with that group (same displacement as the group's nodes,
  `stickyNotesMovedWithGroups`); ungrouped notes are never moved. The response's
  `stickyNotesOverNodes` counts notes left over a node — reposition those with `update_sticky_note`.
- Response: `scope`, `systemsLaidOut`, `systemsLeftAlone`, `laidOutContexts[]`, `shiftedDown`, `stickyNotesOverNodes`.

- **Systems.** Contexts are grouped by flow connectivity. Each connected group is one column laid out
  top-to-bottom by flow depth (Spawn → Init → Update → Output); parallel branches (one spawner feeding
  two systems) sit side by side within the column. Systems keep their current left-to-right order.
- **Feeders belong to their system.** The operators and parameter nodes that drive a system sit in
  columns immediately to the LEFT of that system, so the canvas reads `feeders → system, feeders →
  system, …` and no edge crosses another system's chain. Within a system, feeders are ranked by
  distance from the contexts they feed (rank 1 feeds a context/block directly, rank 2 feeds a rank-1
  operator, …): one column per rank, each node vertically aligned with what it feeds (a block-feeder
  aligns with that block's row) and pushed apart so nothing overlaps. Unlinked operators land at the
  bottom of the first system's rank-1 column.
- **Shared nodes are duplicated, not stretched.** Long edges come from one node feeding many places.
  An operator feeding more than one system is duplicated per system (`duplicateShared`): the clone
  has the same settings and the same input links, and that system's output edges move to the clone.
  This is what a person does by hand and is functionally equivalent (the same expression, evaluated
  once per copy). A parameter gets one canvas node per system it feeds, per group box its consumers sit in
  (a parameter node is never shared between groups — it joins the group of what it feeds), plus one
  more for every band of consumers more than ~600 px apart vertically (`splitParameters`) — most parameters stay single,
  only the ones that fan out across the canvas get duplicates; the blackboard entry stays single. The response reports
  `duplicatedOperators` and `parameterNodesCreated`. Pass `false` to either flag to keep the graph's
  node set untouched (positions only).
- **Groups** are resized to bound their members (every canvas node of a member parameter). **Sticky
  notes** are not moved — reposition them with `update_sticky_note position:[x,y,w,h]` if they end
  up over nodes.
- Response: `{systems, contexts, operators, duplicatedOperators, parameters, parameterNodes,
  parameterNodesCreated, groupsRefit, layout}` — `layout` is the post-pass oracle and should read
  `overlapCount: 0`.

Run it once at the end of a session, not after every op (operator indices change when shared
operators are duplicated — re-describe before addressing anything by index). If you then place a few
nodes by hand, re-describe to make sure you did not create a new overlap.

When authoring by hand, follow the same rule: reuse a parameter through a **new canvas node** next
to each consumer (the editor's "duplicate" of a property) rather than dragging one node's edge across
the canvas, and prefer a second cheap operator over an edge that spans systems.

## Manual placement

- `add_context` / `add_operator` accept `position:[x,y]`.
- `move_node` — `target` = `{node: context|operator|parameter, …index}` + `position:[x,y]`. Blocks have
  no canvas position (they are ordered inside their context; use `reorder_block`/`move_block`). A
  parameter moves all its canvas nodes (staggered vertically); for a linked parameter with no canvas
  node yet the position seeds the node the editor auto-creates.
- Convention: flow reads top-to-bottom, operators sit left of what they feed, upstream above
  downstream, parameters furthest left. Contexts are roughly 440 px wide; leave ≥ 100 px between
  columns and ≥ 50 px between stacked nodes.

## Groups and sticky notes

- `group_nodes` — `title` + `nodes:[…addresses]`; creates the group when the title is new, optional
  `position:[x,y,w,h]` for a new group. Blocks cannot be grouped (group the context). Describe
  `groups[]` = `{title, position, contents[]}`.
- `remove_group` — `title` or `index`; members stay.
- `add_sticky_note` — `title`, `contents`, optional `position:[x,y,w,h]`, `colorTheme` 1–3,
  `textSize` `Small`/`Medium`/`Large`/`Huge`. `update_sticky_note` (`index` + any fields),
  `remove_sticky_note` (`index`), `reorder_sticky_note` (`index`+`toIndex`). Describe `stickyNotes[]`.
  Fit-to-text is a UI measurement and is not available headless.
