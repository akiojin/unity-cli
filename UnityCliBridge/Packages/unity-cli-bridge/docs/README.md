# Unity CLI Bridge

Read this in Japanese: [README.ja.md](./README.ja.md)

Unity Editor bridge package for `unity-cli` automation workflows.

This package exposes editor operations (for example listing and modifying components) through the Unity TCP command interface used by `unity-cli`.

## Installation

- Open Unity Package Manager and choose “Add package from Git URL…”.
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

## Attribution Request

When shipping an app that uses this package, please include attribution in credits/about/README.

Recommended text:

`This product uses unity-cli (https://github.com/akiojin/unity-cli), licensed under MIT.`

## Repository

GitHub: https://github.com/akiojin/unity-cli
