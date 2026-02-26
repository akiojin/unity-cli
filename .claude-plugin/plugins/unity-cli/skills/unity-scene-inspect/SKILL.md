---
name: unity-scene-inspect
description: Analyze Unity scene hierarchy, find GameObjects, inspect components, and query animator state.
allowed-tools: Bash, Read, Grep, Glob
---

# Scene Analysis & Navigation

Inspect scene hierarchy, find objects, and read component data.

## Commands

```bash
# Hierarchy & scene info
unity-cli raw get_hierarchy --json '{"nameOnly":true}'
unity-cli raw get_scene_info --json '{}'
unity-cli raw list_scenes --json '{}'

# Find objects
unity-cli raw find_gameobject --json '{"name":"Player"}'
unity-cli raw find_by_component --json '{"componentType":"Camera"}'

# Inspect details
unity-cli raw get_gameobject_details --json '{"gameObjectName":"Player"}'
unity-cli raw get_component_values --json '{"gameObjectName":"Player","componentType":"Transform"}'
unity-cli raw get_object_references --json '{"gameObjectName":"Player"}'

# Scene analysis
unity-cli raw analyze_scene_contents --json '{"includeInactive":true}'

# Animator
unity-cli raw get_animator_state --json '{"gameObjectName":"Player"}'
unity-cli raw get_animator_runtime_info --json '{"gameObjectName":"Player"}'
```

## Tips

- Use `nameOnly` in `get_hierarchy` for large scenes.
- Pipe JSON output through `jq` for filtering.
