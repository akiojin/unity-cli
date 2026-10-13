# File Write Recipes

Use this guide when choosing between symbol edits, full-file writes, and multi-file writes.

## Choose A Symbol Edit When

- A single method, field, property, or type member is changing in place.
- The surrounding file structure should stay intact.
- You want LSP to preserve local formatting and target a specific `namePath`.

Prefer:

- `rename_symbol`
- `replace_symbol_body`
- `insert_before_symbol`
- `insert_after_symbol`
- `remove_symbol`

## Choose `write_csharp_file` When

- A file needs a full rewrite.
- The change is easier to express as complete text than as symbol operations.
- You are editing interfaces, enums, test files, editor utilities, or generated-style wrappers.
- `get_symbols` is incomplete or the symbol tree is not stable enough for `namePath`.

Recommended options:

- `validate: true` for normal use.
- `format: true` when you want the formatter to normalize the final file.
- `waitForCompile: true` when later steps depend on clean diagnostics.
- `updateIndex: true` when the next step is symbol lookup on the touched file.

## Choose `create_csharp_file` When

- You need any new `.cs` file: a class, `MonoBehaviour`, `ScriptableObject`, interface, enum, test fixture, editor utility, or multiple types.
- You want the same validated write contract as `write_csharp_file`.

`create_class` is a shortcut for an empty class shell (`name`, `folder`, optional `namespace` / `inherits`). It writes through the same LSP path as `create_csharp_file`, so both are safe; prefer `create_csharp_file` when the file needs members.

## Choose `apply_csharp_edits` When

- Interface, implementation, and tests must change together.
- A namespace or public API change spans multiple files.
- The task benefits from one validated write request instead of repeated per-file calls.

Keep the `files` array minimal and coherent. If files are unrelated, split the task.

## Parameter Guidance

- `refresh`: Enable when editor-side state or generated bindings may need an asset refresh.
- `waitForCompile`: Enable when later steps depend on compilation success or fresh diagnostics.
- `updateIndex`: Enable when the next step is symbol search, rename, or ref lookup on touched files.
- `apply: false`: Use to preview risky symbol edits before writing.

## Example Paths

```bash
unity-cli raw create_csharp_file --json '{"relative":"Assets/Scripts/EnemyAI.cs","text":"using UnityEngine;\n\npublic class EnemyAI : MonoBehaviour\n{\n}\n","validate":true,"apply":true,"waitForCompile":true,"updateIndex":true}'
unity-cli raw replace_symbol_body --json '{"relative":"Assets/Scripts/PlayerController.cs","namePath":"PlayerController/Move","body":"{ transform.position += direction * speed * Time.deltaTime; }","apply":false}'
unity-cli raw write_csharp_file --json '{"relative":"Assets/Scripts/PlayerController.cs","newText":"using UnityEngine;\n\npublic sealed class PlayerController : MonoBehaviour {}\n","validate":true,"apply":true,"waitForCompile":true,"updateIndex":true}'
unity-cli raw apply_csharp_edits --json '{"files":[{"relative":"Assets/Scripts/IPlayerMover.cs","newText":"public interface IPlayerMover { void Move(); }\n"},{"relative":"Assets/Scripts/PlayerMover.cs","newText":"public sealed class PlayerMover : IPlayerMover { public void Move() {} }\n"},{"relative":"Assets/Tests/EditMode/PlayerMoverTests.cs","newText":"using NUnit.Framework;\n\npublic class PlayerMoverTests {}\n"}],"validate":true,"apply":true,"waitForCompile":true,"updateIndex":true}'
```
