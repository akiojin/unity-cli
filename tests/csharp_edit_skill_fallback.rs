//! Issue #494: `unity-csharp-edit` must document what to do when the LSP
//! write tools fail, so the agent is never stuck without a write path.

use std::fs;
use std::path::Path;

fn skill_text() -> String {
    let path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join(".claude-plugin/plugins/unity-cli/skills/unity-csharp-edit/SKILL.md");
    fs::read_to_string(path).unwrap()
}

fn fallback_section(text: &str) -> &str {
    let start = text
        .find("## LSP Write Failure Fallback")
        .expect("SKILL.md must contain an `## LSP Write Failure Fallback` section");
    let rest = &text[start + 2..];
    let end = rest.find("\n## ").map_or(rest.len(), |i| i + 2);
    &text[start..start + end]
}

#[test]
fn fallback_section_covers_triggers_edit_refresh_compile_and_report() {
    let text = skill_text();
    let section = fallback_section(&text);
    for needle in [
        "fails to start",
        "times out",
        "unsupported",
        "`Edit`",
        "`Write`",
        "refresh_assets",
        "get_compilation_state",
        "report",
    ] {
        assert!(
            section.contains(needle),
            "fallback section is missing `{needle}`:\n{section}"
        );
    }
}

#[test]
fn preferred_flow_points_to_the_fallback() {
    let text = skill_text();
    let flow_start = text.find("## Preferred Flow").unwrap();
    let flow = &text[flow_start
        ..text
            .find("## LSP Write Failure Fallback")
            .unwrap_or(text.len())];
    assert!(
        flow.contains("LSP Write Failure Fallback"),
        "Preferred Flow must point to the fallback section"
    );
}

#[test]
fn missing_lsp_must_be_recovered_before_direct_editing() {
    let text = skill_text();
    let section = fallback_section(&text);
    let doctor = section
        .find("unity-cli lsp doctor")
        .expect("diagnose LSP first");
    let install = section
        .find("unity-cli lsp install")
        .expect("install before bypassing LSP");
    let direct = section.find("`Edit`").expect("retain last-resort editing");
    assert!(doctor < install && install < direct);
    assert!(section.contains("retry the original LSP write"));
    assert!(section.contains("last resort"));
}

#[test]
fn usage_bootstrap_and_quick_start_require_lsp_install_and_doctor() {
    let text = fs::read_to_string(
        Path::new(env!("CARGO_MANIFEST_DIR"))
            .join(".claude-plugin/plugins/unity-cli/skills/unity-cli-usage/SKILL.md"),
    )
    .unwrap();
    let start = text.find("## Preferred Flow").unwrap();
    let end = text.find("## MCP client setup").unwrap();
    let flow = &text[start..end];
    assert_eq!(flow.matches("unity-cli lsp install").count(), 2);
    assert_eq!(flow.matches("unity-cli lsp doctor").count(), 2);
    assert!(flow.contains("binaryExists: true"));
}
