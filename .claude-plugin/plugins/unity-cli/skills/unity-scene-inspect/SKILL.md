---
name: unity-scene-inspect
description: Inspect Unity scenes without mutating them. Use when the user asks to analyze scene hierarchy, list scenes, find a gameobject by name or component, inspect component values, review object references, or query animator state. Do not use for creating scenes; use `unity-scene-create`. Do not use for editing GameObjects; use `unity-gameobject-edit`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: scenes
  triggers:
    - hierarchy
    - inspect
    - find
    - reference
    - animator
  siblings:
    - unity-scene-create
    - unity-gameobject-edit
    - unity-csharp-navigate
    - unity-csharp-reference
---

# Scene Inspect

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Inspect scene hierarchy, find objects, and read component data without mutating anything. This is the read-only sibling of `unity-gameobject-edit`.

## Use When

- The user wants to inspect the current scene before making changes.
- The user asks where a GameObject, component, or animator state lives.
- The user wants a hierarchy summary, scene inventory, or reference analysis.

## Do Not Use When

- The task is to create new scenes; use `unity-scene-create`.
- The task is to mutate existing objects; use `unity-gameobject-edit`.
- The user only needs source-code navigation with no scene context; use `unity-csharp-navigate`.

## Preferred Flow

1. Start with `get_scene_info`, `list_scenes`, or `get_hierarchy` to scope the work.
2. Locate candidates with `find_gameobject` or `find_by_component`.
3. Inspect targets with `get_gameobject_details`, `get_component_values`, or animator queries.
4. Use `analyze_scene_contents` with `includeInactive` for broad audits.

## Target Parameter Names

Read-only queries name their target differently from the edit tools. A wrong key fails locally with `INVALID_ARGUMENT` before reaching Unity.

| Key                        | Tools                                                                                              |
| -------------------------- | -------------------------------------------------------------------------------------------------- |
| `gameObjectName`           | `get_component_values`, `get_object_references`, `get_animator_state`, `get_animator_runtime_info` |
| `gameObjectName` or `path` | `get_gameobject_details`                                                                           |
| `name`                     | `find_gameobject`                                                                                  |
| `rootPath`                 | `get_hierarchy`                                                                                    |

`gameObjectName` takes a name or a hierarchy path such as `/Root/Child`. Edit tools use `path` or `gameObjectPath` instead; see `unity-gameobject-edit`. When unsure: `unity-cli tool schema <tool_name> --output json`.

```bash
unity-cli raw get_hierarchy --json '{"nameOnly":true}'
unity-cli raw find_gameobject --json '{"name":"Player"}'
unity-cli raw get_component_values --json '{"gameObjectName":"Player","componentType":"Transform"}'
unity-cli raw analyze_scene_contents --json '{"includeInactive":true}'
```

## Examples

- "Show the current scene hierarchy and find the player camera."
- "Inspect the `Transform` values on `Player`."
- "Tell me which animator state the character is in right now."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [scene-inspection-playbook.md](references/scene-inspection-playbook.md): step-by-step inspection flow for large or duplicated scenes.
