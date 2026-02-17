---
name: unity-asset-management
description: Manage Unity assets, materials, import settings, and asset dependencies using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# Asset & Material Management

Manage the Unity Asset Database, create/modify materials, and control import settings.

## Commands

```bash
# Asset database
unity-cli raw manage_asset_database --json '{"action":"refresh"}'
unity-cli raw manage_asset_database --json '{"action":"import","assetPath":"Assets/Textures/hero.png"}'
unity-cli raw refresh_assets --json '{}'

# Materials
unity-cli raw create_material --json '{"name":"HeroMat","shaderName":"Standard","savePath":"Assets/Materials/"}'
unity-cli raw modify_material --json '{"assetPath":"Assets/Materials/HeroMat.mat","properties":{"_Color":{"r":1,"g":0,"b":0,"a":1}}}'

# Import settings
unity-cli raw manage_asset_import_settings --json '{"assetPath":"Assets/Textures/hero.png","settings":{"maxTextureSize":1024}}'

# Dependency analysis
unity-cli raw analyze_asset_dependencies --json '{"assetPath":"Assets/Prefabs/Player.prefab"}'
```

## Tips

- Run `refresh_assets` after external file changes.
- Use `analyze_asset_dependencies` to audit references before deleting assets.
