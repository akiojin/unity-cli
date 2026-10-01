# Issue #445 — CI test command verification

Date: 2026-10-02 JST. Host: macOS, Apple Silicon.
Base: `4028248` (includes #441 exit codes and #443 Editor lifecycle).
Before delivery, the branch was fast-forwarded to `fe44020` (#371 documentation
and benchmark scripts). That update contains no Rust or Unity product changes;
the expanded Python suite was rerun successfully.

## Acceptance audit before implementation

All seven acceptance criteria were unmet. There was no `test` command,
JUnit/NUnit report conversion or GitHub test annotation output. The requested
Actions example and a #445 change record on parent SPEC #155 were also absent.
This change implements that remaining scope and reuses the existing Bridge
test runner and Editor launch arguments.

## Real Editor evidence

The final matrix started at `2026-10-01T18:02:49Z`, after the final product
changes. Both versions completed **45/45 checks** using isolated project copies.

| Acceptance / scenario                                       | 6000.3.25f1 | 2022.3.62f3 |
| ----------------------------------------------------------- | ----------- | ----------- |
| AC-1: stopped Editor → headless EditMode, JUnit XSD, exit 0 | PASS        | PASS        |
| AC-2: exactly one failing case, one XML failure, exit 8     | PASS        | PASS        |
| AC-2: syntax error, exit 6, owned Editor stopped            | PASS        | PASS        |
| AC-3: GUI reused, same sole Editor PID sampled throughout   | PASS        | PASS        |
| AC-4: PlayMode success and failure, Domain Reload enabled   | PASS        | PASS        |
| AC-4: PlayMode success and failure, Domain Reload disabled  | PASS        | PASS        |
| AC-4: existing #250 raw Domain Reload E2E, both settings    | PASS        | PASS        |
| AC-5: GitHub annotation includes source file and line       | PASS        | PASS        |
| NUnit report structure and exit 0                           | PASS        | PASS        |
| Every headless invocation stops its owned Editor            | PASS        | PASS        |

PlayMode success uses the existing `DomainReloadResultTests` (frame advancement
and Rigidbody physics, two cases). The isolated project explicitly enables
Domain Reload; the disabled cases pass `--disable-domain-reload`. The separate
The #250 regression verifies both configured settings and restoration through the
existing raw APIs. GUI reuse records process IDs every 250 ms, including during
execution, and verifies the original GUI process remains alive.

Evidence directories:

- `/tmp/unity-ci-445-6000-fourth/`
- `/tmp/unity-ci-445-2022-fourth/`

Each directory contains `checks.json`, per-scenario XML and CLI logs,
`*-processes.json`, and `regression-250.json`. Earlier `first`/`second`/`third`
attempts found issues and are not passing acceptance evidence.

After fixing the injected syntax error, both versions also passed a headless
EditMode run with no `--port`, `UNITY_CLI_PORT` or `UNITY_CLI_HOST`, confirming
project-based endpoint discovery and recovery from the previous compile error.
The command, exit 0 and successful schema validation are in each directory's
`autodiscovery.json`, with the XML and CLI output alongside it.

Commands:

```bash
cargo build
python3 scripts/e2e-ci-tests.py --version 6000.3.25f1 \
  --output /tmp/unity-ci-445-6000-fourth --port 6551 \
  --junit-schema /tmp/issue-445-junit.xsd
python3 scripts/e2e-ci-tests.py --version 2022.3.62f3 \
  --output /tmp/unity-ci-445-2022-fourth --port 6552 \
  --junit-schema /tmp/issue-445-junit.xsd
```

JUnit validation uses Jenkins xUnit's
[junit-10.xsd](https://github.com/jenkinsci/xunit-plugin/blob/master/src/main/resources/org/jenkinsci/plugins/xunit/types/model/xsd/junit-10.xsd)
through `xmllint --noout --schema`.
Schema SHA-256: `a1a816f58d1bf95ebabf371994df0b9246dee66ea9572fbec4f9296f1b2c0ff6`.
Tested CLI SHA-256: `ac302613df25c0cb983045115508ed66b7e9b4c80f74e51c22e0beafe40dc36b`.

## Automated checks

- `cargo llvm-cov --all-targets --summary-only -- --test-threads=1`: 637 passed;
  line coverage **92.87%** (required minimum 90%). Log: `/tmp/issue-445-coverage.log`.
- `cargo fmt --all -- --check`: PASS.
- `cargo +stable clippy --all-targets -- -D warnings`: PASS.
- `cargo run -- skills lint --severity error`: 23 skills, 0 violations.
- `dotnet test lsp/Server.Tests.csproj`: 53 passed.
- Python script suite after the base update: 75 tests, 1 skipped, no failures.
- Markdownlint, Prettier, Python syntax, `git diff --check`: PASS.

New regression inventory:

- CLI report filename parsing without changing other commands' output formats.
- AssetImportWorker processes excluded from reusable Editors.
- JUnit failed/skipped cases, XML escaping, captured output, schema-compatible durations.
- NUnit suite nesting, failure stack traces and captured output.
- Suite setup/teardown failures preserved; parent rollups do not duplicate leaf failures.
- GitHub source locations and workflow-command escaping.
- Dropped status connection reconnects without submitting tests twice.
- Compile errors exit 6; unreachable Editors exit 7; test failures export before exit 8.
- Startup readiness disconnects are retried; slow test discovery respects `--timeout`.
- Execution timeout differs from initial connection timeout; owned children are reaped.
- Prior compilation logs cannot turn the next invocation into a false compile failure.

## Delivery scope

AC-6 is documented in `docs/development.md`: a self-hosted macOS Actions example,
artifact upload and preconfigured Unity licensing requirements. Actual execution
on GitHub Actions is outside #445's scope. Windows/Linux real Editor verification
remains with #386.

User Verification Result: n/a (autonomous).
Agent Visual Check: n/a (no UI surface change; real GUI process reuse was automated).

AC-7 is recorded on [parent SPEC #155](https://github.com/akiojin/unity-cli/issues/155#issuecomment-5937558643).
The #445 acceptance checkboxes reflect implementation and local verification;
they do not claim a merged PR. PR creation belongs to the PM under #445's Notes
ruling. Canonical verification admission is reported in the Issue handoff.
