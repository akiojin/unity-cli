# Contract: CLI Commands (SPEC-911befe7)

## list-instances
- Args: `--host <string>` (default localhost), `--ports <comma list>` (required), `--json` (optional)
- Output (text): table with id, status(up/down), active(*)
- Output (json): [{ id, host, port, status, active }]
- Errors: missing ports → exit 1 with message

## set-active <id>
- id format: host:port
- Behavior: validates by tcp connect (timeout 1s); on success, sets activeId and prints confirmation
- Output (json): { activeId, previousId?, checkedAt }
- Errors: not found / unreachable → exit 1 with guidance: "run list-instances again"

## 共通エラー
- exit code ≠0, stderr に人間可読メッセージ、日本語/英語どちらか
