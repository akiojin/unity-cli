//! Unit tests for the skill linter rules (R01..R22).

#![cfg(test)]

use std::fs;
#[cfg(unix)]
use std::os::unix::fs::symlink;
use std::path::{Path, PathBuf};
use tempfile::TempDir;

use super::loader::load_skills;
use super::model::{allowed_tool_set, RuleId, Severity, Violation};
use super::report::{render, ReportFormat};
use super::rules::{rule_r18, rule_r19, rule_r20, rule_r21, run_all, RuleContext};
use super::runner::{discover_root, lint, LintOptions, LintOutcome};

const VALID_DESC: &str = "Manage Unity prefab assets with unity-cli. Use when the user asks to create a prefab from a scene object or open a prefab in edit mode. Do not use for general scene object editing; use `unity-gameobject-edit` instead.";

fn write_skill(dir: &Path, name: &str, frontmatter: &str, body: &str) -> PathBuf {
    let skill_dir = dir.join(name);
    fs::create_dir_all(skill_dir.join("references")).unwrap();
    fs::write(
        skill_dir.join("SKILL.md"),
        format!("---\n{frontmatter}---\n\n{body}\n"),
    )
    .unwrap();
    fs::write(
        skill_dir.join("references/runtime-checklist.md"),
        "# Runtime\n\nMinimal placeholder.\n",
    )
    .unwrap();
    skill_dir
}

fn valid_frontmatter(name: &str) -> String {
    format!(
        "name: {name}\ndescription: {VALID_DESC}\nallowed-tools: Bash(unity-cli:*), Read, Grep, Glob\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-gameobject-edit\n"
    )
}

fn sibling_frontmatter(name: &str, partner: &str) -> String {
    format!(
        "name: {name}\ndescription: Edit GameObjects with unity-cli. Use when the user asks to rename a gameobject. Do not use for prefab edit mode; use `{partner}`.\nallowed-tools: Bash(unity-cli:*), Read, Grep, Glob\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: scenes\n  triggers:\n    - gameobject\n  siblings:\n    - {partner}\n"
    )
}

fn valid_body() -> String {
    [
        "# Test Skill",
        "",
        "## Use When",
        "- The user asks for prefabs.",
        "",
        "## Do Not Use When",
        "- Anything else.",
        "",
        "## Preferred Flow",
        "1. Step one.",
        "",
        "## Examples",
        "- An example.",
        "",
        "## References",
        "- [runtime-checklist.md](references/runtime-checklist.md)",
    ]
    .join("\n")
}

fn lint_dir(root: &Path) -> Vec<String> {
    let skills = load_skills(root).unwrap();
    let ctx = RuleContext {
        skills: &skills,
        repo_root: root,
    };
    let mut out = Vec::new();
    for skill in &skills {
        for v in run_all(skill, &ctx) {
            out.push(v.rule.clone());
        }
    }
    out
}

#[test]
fn good_skill_passes_all_rules() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-prefab-workflow",
        &valid_frontmatter("unity-prefab-workflow"),
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-prefab-workflow"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert_eq!(violations, Vec::<String>::new(), "expected zero violations");
}

#[test]
fn r01_missing_description_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-broken",
        "name: unity-broken\nallowed-tools: Bash(unity-cli:*), Read, Grep, Glob\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: scenes\n  triggers:\n    - scene\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(
        violations.iter().any(|r| r == "R01"),
        "expected R01, got {violations:?}"
    );
}

#[test]
fn r01_missing_multiple_required_fields_fire() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-missing-fields",
        "name:\nmetadata:\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-missing-fields"),
        &valid_body(),
    );

    let skills = load_skills(tmp.path()).unwrap();
    let skill = skills
        .iter()
        .find(|candidate| candidate.name == "unity-missing-fields")
        .unwrap();
    let violations = super::rules::rule_r01(skill);
    let messages: Vec<&str> = violations.iter().map(|v| v.message.as_str()).collect();
    assert!(messages.iter().any(|msg| msg.contains("missing `name`")));
    assert!(messages
        .iter()
        .any(|msg| msg.contains("missing `description`")));
    assert!(messages
        .iter()
        .any(|msg| msg.contains("missing `allowed-tools`")));
    assert!(messages
        .iter()
        .any(|msg| msg.contains("missing `metadata.author`")));
    assert!(messages
        .iter()
        .any(|msg| msg.contains("missing `metadata.version`")));
    assert!(messages
        .iter()
        .any(|msg| msg.contains("missing `metadata.category`")));
    assert!(messages
        .iter()
        .any(|msg| msg.contains("missing `metadata.triggers`")));
}

#[test]
fn r02_name_mismatch_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-mismatch",
        &valid_frontmatter("unity-other-name"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R02"));
}

#[test]
fn r03_disallowed_tool_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-tooltest",
        "name: unity-tooltest\ndescription: Manage Unity assets with unity-cli. Use when the user asks for assets. Do not use for code; use `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read, WebFetch\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: assets\n  triggers:\n    - asset\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R03"));
}

#[test]
fn r03_legacy_bare_bash_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-bashtest",
        "name: unity-bashtest\ndescription: Manage Unity assets with unity-cli. Use when the user asks for assets. Do not use for code; use `unity-gameobject-edit`.\nallowed-tools: Bash, Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: assets\n  triggers:\n    - asset\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R03"));
}

#[test]
fn r04_unknown_category_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-cattest",
        "name: unity-cattest\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefabs. Do not use for assets; use `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: nonsense\n  triggers:\n    - prefab\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R04"));
}

#[test]
fn r05_uppercase_trigger_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-uppertest",
        "name: unity-uppertest\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefabs. Do not use for assets; use `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - Prefab\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R05"));
}

#[test]
fn r05_empty_and_duplicate_triggers_fire() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-badtriggers",
        "name: unity-badtriggers\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefabs. Do not use for assets; use `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - \"\"\n    - prefab\n    - prefab\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-badtriggers"),
        &valid_body(),
    );

    let skills = load_skills(tmp.path()).unwrap();
    let skill = skills
        .iter()
        .find(|candidate| candidate.name == "unity-badtriggers")
        .unwrap();
    let violations = super::rules::rule_r05(skill);
    let messages: Vec<&str> = violations.iter().map(|v| v.message.as_str()).collect();
    assert!(messages
        .iter()
        .any(|msg| msg.contains("contains empty string")));
    assert!(messages
        .iter()
        .any(|msg| msg.contains("duplicate trigger `prefab`")));
}

#[test]
fn r06_unknown_sibling_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-orphan",
        "name: unity-orphan\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefabs. Do not use for the imaginary `unity-ghost`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-ghost\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R06"));
}

#[test]
fn r07_too_long_description_fires() {
    let tmp = TempDir::new().unwrap();
    let long = "Manage Unity prefabs with unity-cli. ".repeat(40);
    write_skill(
        tmp.path(),
        "unity-toolong",
        &format!("name: unity-toolong\ndescription: {long} Use when the user asks. Do not use for `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-gameobject-edit\n"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R07"));
}

#[test]
fn r08_missing_front_trigger_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-noheadtrig",
        "name: unity-noheadtrig\ndescription: Drive Unity Editor automation with unity-cli. Use when the user asks to do something arbitrary in the editor without using the trigger word at all so the head test can fail. Do not use for `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: editor\n  triggers:\n    - obscuretoken\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R08"));
}

#[test]
fn r09_first_person_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-person",
        "name: unity-person\ndescription: I can help you manage Unity prefabs with unity-cli. Use when the user asks for prefabs. Do not use for `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R09"));
}

#[test]
fn r10_missing_use_when_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-nousewhen",
        "name: unity-nousewhen\ndescription: Manage Unity prefabs with unity-cli. Do not use for `unity-gameobject-edit`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R10"));
}

#[test]
fn r11_missing_sibling_mention_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-nomention",
        "name: unity-nomention\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefabs. Do not use otherwise.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-gameobject-edit\n",
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-nomention"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R11"));
}

#[test]
fn r12_unidirectional_sibling_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-one",
        "name: unity-one\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefabs. Do not use for `unity-two`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-two\n",
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-two",
        "name: unity-two\ndescription: Manage Unity assets with unity-cli. Use when the user asks for assets. Do not use for `unity-three`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: assets\n  triggers:\n    - asset\n  siblings:\n    - unity-three\n",
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-three",
        "name: unity-three\ndescription: Manage Unity assets with unity-cli. Use when the user asks for assets. Do not use for `unity-two`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: assets\n  triggers:\n    - asset-mgmt\n  siblings:\n    - unity-two\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R12"));
}

#[test]
fn r13_too_long_body_fires() {
    let tmp = TempDir::new().unwrap();
    let body_lines: Vec<String> = (0..600).map(|i| format!("line {i}")).collect();
    let body = format!(
        "# Title\n\n## Use When\n- a\n## Do Not Use When\n- b\n## Preferred Flow\n1. c\n## Examples\n- d\n## References\n- [r](references/runtime-checklist.md)\n\n{}\n",
        body_lines.join("\n")
    );
    write_skill(
        tmp.path(),
        "unity-bigbody",
        &valid_frontmatter("unity-bigbody"),
        &body,
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-bigbody"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R13"));
}

#[test]
fn r14_missing_heading_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-noheading",
        &valid_frontmatter("unity-noheading"),
        "# Title\n\n## Use When\n- a\n## Examples\n- e\n## References\n- [r](references/runtime-checklist.md)\n",
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-noheading"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R14"));
}

#[test]
fn r15_references_without_link_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-emptyrefs",
        &valid_frontmatter("unity-emptyrefs"),
        "# Title\n\n## Use When\n- a\n## Do Not Use When\n- b\n## Preferred Flow\n1. c\n## Examples\n- d\n## References\n\nNo links here.\n",
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-emptyrefs"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R15"));
}

#[test]
fn r16_time_sensitive_phrase_fires() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-timed",
        &valid_frontmatter("unity-timed"),
        "# Title\n\n## Use When\n- as of yesterday this works\n## Do Not Use When\n- b\n## Preferred Flow\n1. c\n## Examples\n- d\n## References\n- [r](references/runtime-checklist.md)\n",
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-timed"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R16"));
}

#[test]
fn r17_missing_runtime_checklist_fires() {
    let tmp = TempDir::new().unwrap();
    let dir = tmp.path().join("unity-noruntime");
    fs::create_dir_all(&dir).unwrap();
    fs::write(
        dir.join("SKILL.md"),
        format!(
            "---\n{}---\n\n{}\n",
            valid_frontmatter("unity-noruntime"),
            valid_body()
        ),
    )
    .unwrap();
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-noruntime"),
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(violations.iter().any(|r| r == "R17"));
}

#[test]
fn r18_long_reference_without_toc_fires() {
    let tmp = TempDir::new().unwrap();
    let skill_dir = write_skill(
        tmp.path(),
        "unity-longref",
        &valid_frontmatter("unity-longref"),
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-longref"),
        &valid_body(),
    );
    let lines: Vec<String> = (0..120).map(|i| format!("line {i}")).collect();
    fs::write(
        skill_dir.join("references/long-guide.md"),
        format!("# Long Guide\n\n{}\n", lines.join("\n")),
    )
    .unwrap();

    let skills = load_skills(tmp.path()).unwrap();
    let skill = skills.iter().find(|s| s.name == "unity-longref").unwrap();
    let violations = rule_r18(skill);
    assert!(violations.iter().any(|v| v.rule == "R18"));
}

#[test]
fn r19_reference_link_to_reference_fires() {
    let tmp = TempDir::new().unwrap();
    let skill_dir = write_skill(
        tmp.path(),
        "unity-refnest",
        &valid_frontmatter("unity-refnest"),
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-refnest"),
        &valid_body(),
    );
    fs::write(
        skill_dir.join("references/nested.md"),
        "# Nested\n\n[runtime](runtime-checklist.md)\n",
    )
    .unwrap();

    let skills = load_skills(tmp.path()).unwrap();
    let skill = skills.iter().find(|s| s.name == "unity-refnest").unwrap();
    let violations = rule_r19(skill);
    assert!(violations.iter().any(|v| v.rule == "R19"));
}

#[cfg(unix)]
#[test]
fn r20_and_r21_symlink_rules_cover_missing_and_valid_targets() {
    let tmp = TempDir::new().unwrap();
    let skill_dir = write_skill(
        tmp.path(),
        "unity-symlinked",
        &valid_frontmatter("unity-symlinked"),
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-symlinked"),
        &valid_body(),
    );
    fs::create_dir_all(tmp.path().join(".claude/skills")).unwrap();
    fs::create_dir_all(tmp.path().join(".agents/skills")).unwrap();

    let skills = load_skills(tmp.path()).unwrap();
    let skill = skills.iter().find(|s| s.name == "unity-symlinked").unwrap();
    let ctx = RuleContext {
        skills: &skills,
        repo_root: tmp.path(),
    };

    let missing_claude = rule_r20(skill, &ctx);
    let missing_agents = rule_r21(skill, &ctx);
    assert!(missing_claude.iter().any(|v| v.rule == "R20"));
    assert!(missing_agents.iter().any(|v| v.rule == "R21"));

    symlink(
        skill_dir.canonicalize().unwrap(),
        tmp.path().join(".claude/skills/unity-symlinked"),
    )
    .unwrap();
    symlink(
        skill_dir.canonicalize().unwrap(),
        tmp.path().join(".agents/skills/unity-symlinked"),
    )
    .unwrap();

    assert!(rule_r20(skill, &ctx).is_empty());
    assert!(rule_r21(skill, &ctx).is_empty());
}

#[test]
fn report_and_runner_cover_text_json_discovery_and_error_paths() {
    let tmp = TempDir::new().unwrap();
    let skills_root = tmp.path().join(".claude-plugin/plugins/unity-cli/skills");
    fs::create_dir_all(&skills_root).unwrap();
    write_skill(
        &skills_root,
        "unity-prefab-workflow",
        &valid_frontmatter("unity-prefab-workflow"),
        &valid_body(),
    );
    write_skill(
        &skills_root,
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-prefab-workflow"),
        &valid_body(),
    );

    let nested = tmp.path().join("nested/project");
    fs::create_dir_all(&nested).unwrap();
    let discovered = discover_root(&nested).unwrap();
    assert_eq!(discovered.0, skills_root);
    assert_eq!(discovered.1, tmp.path());
    assert!(discover_root(TempDir::new().unwrap().path()).is_none());

    let clean = lint(&LintOptions {
        root: discovered.0.clone(),
        repo_root: discovered.1.clone(),
        severity: Severity::Error,
    })
    .unwrap();
    assert!(clean.violations.is_empty());
    assert!(!clean.has_errors(Severity::Error));
    assert!(!clean.has_errors(Severity::Warning));

    let sample = LintOutcome {
        skills: vec!["unity-prefab-workflow".to_string()],
        violations: vec![Violation::new(
            RuleId::R01FrontmatterRequired,
            Severity::Error,
            "unity-prefab-workflow",
            skills_root.join("unity-prefab-workflow/SKILL.md"),
            "missing description",
        )],
    };
    let error_text = render(&sample, ReportFormat::Text, Severity::Error);
    let warning_text = render(&sample, ReportFormat::Text, Severity::Warning);
    let json_text = render(&sample, ReportFormat::Json, Severity::Error);
    assert!(error_text.contains("error [R01]"));
    assert!(warning_text.contains("warning [R01]"));
    assert!(error_text.contains("1 skills checked, 1 violations"));
    let parsed: serde_json::Value = serde_json::from_str(&json_text).unwrap();
    assert_eq!(parsed.as_array().unwrap().len(), 1);
    assert_eq!(parsed[0]["rule"], "R01");

    let empty_root = tmp.path().join("empty");
    fs::create_dir_all(&empty_root).unwrap();
    let err = lint(&LintOptions {
        root: empty_root,
        repo_root: tmp.path().to_path_buf(),
        severity: Severity::Error,
    })
    .unwrap_err()
    .to_string();
    assert!(err.contains("no `unity-*` skills found"));

    let blocked = LintOutcome {
        skills: vec!["unity-prefab-workflow".to_string()],
        violations: sample.violations.clone(),
    };
    assert!(blocked.has_errors(Severity::Error));

    let invalid_root = tmp.path().join("invalid");
    write_skill(
        &invalid_root,
        "unity-zed",
        "name: unity-zed\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: scenes\n  triggers:\n    - zed\n",
        &valid_body(),
    );
    write_skill(
        &invalid_root,
        "unity-alpha",
        "name: unity-alpha\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: scenes\n  triggers:\n    - alpha\n",
        &valid_body(),
    );
    let invalid = lint(&LintOptions {
        root: invalid_root,
        repo_root: tmp.path().to_path_buf(),
        severity: Severity::Error,
    })
    .unwrap();
    assert_eq!(invalid.violations[0].skill, "unity-alpha");
    assert_eq!(invalid.violations[1].skill, "unity-zed");
}

#[test]
fn model_helpers_cover_skill_accessors_and_tool_sets() {
    let tmp = TempDir::new().unwrap();
    write_skill(
        tmp.path(),
        "unity-prefab-workflow",
        &valid_frontmatter("unity-prefab-workflow"),
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-gameobject-edit",
        &sibling_frontmatter("unity-gameobject-edit", "unity-prefab-workflow"),
        &valid_body(),
    );

    let skills = load_skills(tmp.path()).unwrap();
    let skill = skills
        .iter()
        .find(|candidate| candidate.name == "unity-prefab-workflow")
        .unwrap();
    assert_eq!(skill.body_line_count(), valid_body().lines().count() + 1);
    assert_eq!(skill.description(), VALID_DESC);
    assert_eq!(skill.triggers(), &["prefab".to_string()]);
    assert_eq!(skill.siblings(), &["unity-gameobject-edit".to_string()]);

    let prefab_tools = allowed_tool_set("unity-prefab-workflow");
    let edit_tools = allowed_tool_set("unity-csharp-edit");
    assert!(prefab_tools.contains("Read"));
    assert!(!prefab_tools.contains("Edit"));
    assert!(edit_tools.contains("Edit"));
    assert!(edit_tools.contains("Write"));
}

#[test]
fn r22_collision_without_cross_listing_fires() {
    let tmp = TempDir::new().unwrap();
    // Two skills share trigger `prefab` but are not siblings of each other.
    write_skill(
        tmp.path(),
        "unity-x-one",
        "name: unity-x-one\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefab. Do not use for `unity-x-other`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-x-other\n",
        &valid_body(),
    );
    write_skill(
        tmp.path(),
        "unity-x-other",
        "name: unity-x-other\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefab. Do not use for `unity-x-one`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-x-one\n",
        &valid_body(),
    );
    // Third skill also uses `prefab` but does not cross-list.
    write_skill(
        tmp.path(),
        "unity-x-third",
        "name: unity-x-third\ndescription: Manage Unity prefabs with unity-cli. Use when the user asks for prefab. Do not use for `unity-x-one`.\nallowed-tools: Bash(unity-cli:*), Read\nmetadata:\n  author: tester\n  version: 0.3.0\n  category: prefabs\n  triggers:\n    - prefab\n  siblings:\n    - unity-x-one\n",
        &valid_body(),
    );
    let violations = lint_dir(tmp.path());
    assert!(
        violations.iter().any(|r| r == "R22"),
        "expected R22, got {violations:?}"
    );
}
