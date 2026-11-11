# Runtime Checklist

## Binary Selection

- Prefer an installed `unity-cli` binary on `PATH`; verify it with `unity-cli --version`.
- If it is missing, install the release binary first:
  - macOS / Linux: `curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh`; use `~/.local/bin/unity-cli` if needed.
  - Windows (PowerShell): `irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex`. The installer adds `%USERPROFILE%\.unity\tools\unity-cli\win-x64` to the user `PATH`; open a new shell to pick it up, or invoke `unity-cli.exe` in that directory.
- Only inside a unity-cli source checkout (its `Cargo.toml` declares the `unity-cli` package), `cargo run -- <args>` is a development alternative. A consuming Unity project is not that checkout; use the installer there.

## Instance Selection

- Use `unity-cli system ping` when a single active target is expected.
- Use `unity-cli instances list` when multiple editors may be running.
- Use `unity-cli instances set-active <host:port>` only after confirming the target is `up`.

## Command Routing

- Prefer typed subcommands for stable workflows such as `system`, `scene`, and `instances`.
- Use `raw` when only the low-level tool exists or you need an exact tool payload.
- Use `--output json` when another tool or script will consume the result.

## CI Notes

- Set `UNITY_CLI_HOST` and `UNITY_CLI_PORT` explicitly in CI.
- Keep JSON payloads quoted as a single shell argument.
- If connectivity fails in CI, report the resolved host and port before retrying.
