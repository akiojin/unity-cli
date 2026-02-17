# Unity CLI Bridge

Read this in Japanese: [README.ja.md](./README.ja.md)

Unity Editor bridge package for `unity-cli` automation workflows.

This package exposes editor operations (for example listing and modifying components) through the Unity TCP command interface used by `unity-cli`.

## Installation

- Open Unity Package Manager and choose "Add package from Git URL...".
- Use this URL (UPM subfolder):

```
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

## Features

- Component operations: add, remove, modify, and list components on GameObjects.
- Type-safe value conversion for common Unity types (Vector2/3, Color, Quaternion, enums).
- Extensible editor handlers designed for CLI/TCP command execution.

## Directory Structure

- `Editor/`: CLI command handlers and editor-side logic.
- `Tests/`: Editor test sources.
- `docs/`: Documentation, including this README and the Japanese translation.

## License

MIT

## License Attribution

When redistributing this package or including it in published projects, the MIT license requires you to include the copyright and permission notice. A full attribution guide with templates is available at [`ATTRIBUTION.md`](../../../../ATTRIBUTION.md) in the repository root.

Example attribution text:

```
This product includes software developed by akiojin.
unity-cli - https://github.com/akiojin/unity-cli
Licensed under the MIT License.
```

## Repository

GitHub: https://github.com/akiojin/unity-cli
