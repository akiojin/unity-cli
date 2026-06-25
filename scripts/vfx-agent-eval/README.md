# VFX Agent Eval (outcome layer)

Grades whether an **agent** can BUILD the right Visual Effect Graph from a natural-language prompt
using **only** the `unity-vfx-graph` skill. Complements the routing eval
(`scripts/skill-eval/`), which only checks *which skill/tool* a prompt routes to. Here we run the
real `vfx_*` ops and read the result back with `vfx_describe_graph`, grading against structural
assertions (the "oracle").

This is an **on-demand discovery tool, not a CI gate.** Agent non-determinism makes it a
thermometer, not a tripwire — the runner always exits 0, and first-run failures usually become
skill/docs fixes rather than red builds. The 149+ EditMode tests already prove the handler; this
eval tests the **skill + agent reasoning**.

## Layout

Two tiers:

**Authoring tier (static, via `vfx_describe_graph`):**
- `tests/fixtures/vfx-agent-eval/benchmark.jsonl` — the tasks. Each row: `id`, `prompt` (the NL
  request), `fixture` (pristine input), `grader` (a function in the scorer), `args` (grader params),
  `oracle` (human-readable assertion), `difficulty`.
- `scripts/vfx-agent-eval/vfx-agent-eval.sh` — the runner + embedded Python scorer/graders.

**Runtime tier (behavioral, on a live `VisualEffect` via `vfx_runtime`):**
- `tests/fixtures/vfx-agent-eval/runtime-benchmark.jsonl` — runtime tasks (set an exposed float/texture,
  override the initial event name). Same row shape; `args.param` names the exposed param to read back.
- `scripts/vfx-agent-eval/vfx-runtime-eval.sh` — builds a rig (GameObject + VisualEffect + `set_asset`),
  lets the agent drive `vfx_runtime` ops, then grades `vfx_runtime get_state`. These value round-trips
  are deterministic in the editor (no play mode). **SPAWN behavior** (`aliveParticleCount > 0`) needs
  play mode + a Camera + per-frame `simulate`-with-yields, so it is proven C#-side by the PlayMode test
  `Runtime_SimulateOp_AdvancesSpawnUntilBurstAlive` (which exercises the `vfx_runtime simulate` op), not
  by the raw-driving agent.

**Shared:** `.github/workflows/vfx-agent-eval.yml` — nightly, opt-in behind
`secrets.VFX_AGENT_EVAL_AGENT_CMD` (needs a Unity-equipped runner; skips cleanly when unset).

## Prerequisites

- A live Unity bridge: the `UnityCliBridge` project open, bridge on `:6400`
  (`unity-cli raw ping --port 6400`).
- `python3` (the scorer). The graph ops run through `unity-cli`.

## Modes

**Probe-only (validate the graders, or score hand-built probes).** Omit `--agent-cmd`; the runner
copies each fixture to a probe, then grades whatever is at the target path. Build the probes however
you like first (e.g. by hand, or via the manual clean-room-subagent flow below):

```bash
scripts/vfx-agent-eval/vfx-agent-eval.sh --keep-probes --json
```

**Full agent loop.** Provide `--agent-cmd "<cmd>"`. For each task the runner sets up the probe,
invokes the command once with the task in env vars, then describes + grades:

| env var | meaning |
|---|---|
| `VFX_EVAL_PROMPT` | the natural-language task |
| `VFX_EVAL_TARGET` | the asset the agent should build/produce (probe `.vfx`, or the output `.asset` for the SDF task) |
| `VFX_EVAL_SOURCE` | the source fixture (`== TARGET` for graph tasks; the mesh for the SDF task) |
| `VFX_EVAL_PROJECT_DIR` | the Unity project dir (the paths above are project-relative) |
| `VFX_EVAL_PORT` | the bridge port |

The `--agent-cmd` must launch a **clean-room agent** — given ONLY the `unity-vfx-graph` skill, the
prompt, and the target asset; NOT the handler source, the project handoff docs, or §6b internals.
If it can't do the task from the skill alone, that IS the finding. Wire it to a headless agent
runtime (e.g. Claude Code headless or the Agent SDK) with the skill mounted.

## How the mechanism was proven (before any automation)

The clean-room-subagent + oracle mechanism was validated manually first (the master agent launched a
fresh subagent per task via the Agent tool, given only the skill + a probe, then graded with
`vfx_describe_graph`). All 17 tasks passed; the runs also surfaced the block-op `contextIndex`
addressing gap (since fixed), a flipbook `uvMode` doc bug (since fixed), and minor skill-doc gaps. See
`VFX-Graph-LOG.md` §D/§D.2/§D.4/§D.5 in the project root for the full write-up.

## Caveats

- The runner orchestration (probe copy + `refresh_assets` + describe) and the Python scorer are
  designed for a Linux CI with a real `python3`. On Windows, `python3` is often the Microsoft Store
  stub — run the harness on the CI runner, not locally.
- The graders are ported verbatim from the node-based oracles used in the manual proof, so they
  encode the same assertions that already passed all 17 tasks.
