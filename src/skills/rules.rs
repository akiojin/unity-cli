use std::collections::{BTreeMap, BTreeSet};
use std::fs;
use std::path::{Path, PathBuf};

use regex::Regex;

use super::model::{
    allowed_tool_set, RuleId, Severity, Skill, Violation, ALLOWED_CATEGORIES,
    ALLOWED_TOOLS_LEGACY_BASH,
};

pub struct RuleContext<'a> {
    pub skills: &'a [Skill],
    pub repo_root: &'a Path,
}

/// Run all R01..R22 against `skill` (single-skill rules) plus cross-skill
/// rules. Returns violations in deterministic order by rule id.
pub fn run_all(skill: &Skill, ctx: &RuleContext<'_>) -> Vec<Violation> {
    let mut out = Vec::new();
    out.extend(rule_r01(skill));
    out.extend(rule_r02(skill));
    out.extend(rule_r03(skill));
    out.extend(rule_r04(skill));
    out.extend(rule_r05(skill));
    out.extend(rule_r06(skill, ctx));
    out.extend(rule_r07(skill));
    out.extend(rule_r08(skill));
    out.extend(rule_r09(skill));
    out.extend(rule_r10(skill));
    out.extend(rule_r11(skill));
    out.extend(rule_r12(skill, ctx));
    out.extend(rule_r13(skill));
    out.extend(rule_r14(skill));
    out.extend(rule_r15(skill));
    out.extend(rule_r16(skill));
    out.extend(rule_r17(skill));
    out.extend(rule_r18(skill));
    out.extend(rule_r19(skill));
    out.extend(rule_r20(skill, ctx));
    out.extend(rule_r21(skill, ctx));
    out.extend(rule_r22(skill, ctx));
    out
}

fn err(rule: RuleId, skill: &Skill, msg: impl Into<String>) -> Violation {
    Violation::new(
        rule,
        Severity::Error,
        &skill.name,
        &skill.skill_md_path,
        msg,
    )
}

// ---------- R01: required frontmatter fields exist ----------
pub fn rule_r01(skill: &Skill) -> Vec<Violation> {
    let mut v = Vec::new();
    let fm = &skill.frontmatter;
    if fm.name.as_deref().unwrap_or("").is_empty() {
        v.push(err(RuleId::R01FrontmatterRequired, skill, "missing `name`"));
    }
    if fm.description.as_deref().unwrap_or("").is_empty() {
        v.push(err(
            RuleId::R01FrontmatterRequired,
            skill,
            "missing `description`",
        ));
    }
    if fm.allowed_tools.as_deref().unwrap_or("").is_empty() {
        v.push(err(
            RuleId::R01FrontmatterRequired,
            skill,
            "missing `allowed-tools`",
        ));
    }
    if fm.metadata.author.as_deref().unwrap_or("").is_empty() {
        v.push(err(
            RuleId::R01FrontmatterRequired,
            skill,
            "missing `metadata.author`",
        ));
    }
    if fm.metadata.version.as_deref().unwrap_or("").is_empty() {
        v.push(err(
            RuleId::R01FrontmatterRequired,
            skill,
            "missing `metadata.version`",
        ));
    }
    if fm.metadata.category.as_deref().unwrap_or("").is_empty() {
        v.push(err(
            RuleId::R01FrontmatterRequired,
            skill,
            "missing `metadata.category`",
        ));
    }
    if fm.metadata.triggers.is_empty() {
        v.push(err(
            RuleId::R01FrontmatterRequired,
            skill,
            "missing `metadata.triggers` (must be non-empty)",
        ));
    }
    // metadata.siblings may legitimately be empty; no check here.
    v
}

// ---------- R02: name matches dir ----------
pub fn rule_r02(skill: &Skill) -> Vec<Violation> {
    let dir_name = skill.dir.file_name().and_then(|s| s.to_str()).unwrap_or("");
    let name = skill.frontmatter.name.as_deref().unwrap_or("");
    if name.is_empty() || name == dir_name {
        Vec::new()
    } else {
        vec![err(
            RuleId::R02NameMatchesDir,
            skill,
            format!("frontmatter `name` ({name}) does not match directory ({dir_name})"),
        )]
    }
}

// ---------- R03: allowed-tools subset ----------
pub fn rule_r03(skill: &Skill) -> Vec<Violation> {
    let raw = match skill.frontmatter.allowed_tools.as_deref() {
        Some(s) if !s.is_empty() => s,
        _ => return Vec::new(),
    };
    let permitted = allowed_tool_set(&skill.name);
    let mut v = Vec::new();
    for token in raw.split(',') {
        let token = token.trim();
        if token.is_empty() {
            continue;
        }
        if token == ALLOWED_TOOLS_LEGACY_BASH {
            v.push(err(
                RuleId::R03AllowedTools,
                skill,
                "`allowed-tools` uses legacy bare `Bash`; use `Bash(unity-cli:*)` instead",
            ));
            continue;
        }
        if !permitted.contains(token) {
            v.push(err(
                RuleId::R03AllowedTools,
                skill,
                format!("`allowed-tools` contains disallowed token `{token}`"),
            ));
        }
    }
    v
}

// ---------- R04: category enum ----------
pub fn rule_r04(skill: &Skill) -> Vec<Violation> {
    let cat = match skill.frontmatter.metadata.category.as_deref() {
        Some(c) => c,
        None => return Vec::new(),
    };
    if ALLOWED_CATEGORIES.contains(&cat) {
        Vec::new()
    } else {
        vec![err(
            RuleId::R04Category,
            skill,
            format!(
                "`metadata.category` `{cat}` not in {:?}",
                ALLOWED_CATEGORIES
            ),
        )]
    }
}

// ---------- R05: triggers shape ----------
pub fn rule_r05(skill: &Skill) -> Vec<Violation> {
    let triggers = &skill.frontmatter.metadata.triggers;
    if triggers.is_empty() {
        return Vec::new(); // R01 reports.
    }
    let mut v = Vec::new();
    let mut seen = BTreeSet::new();
    for t in triggers {
        if t.is_empty() {
            v.push(err(
                RuleId::R05Triggers,
                skill,
                "`metadata.triggers` contains empty string",
            ));
            continue;
        }
        if t.chars().any(|c| c.is_uppercase()) {
            v.push(err(
                RuleId::R05Triggers,
                skill,
                format!("trigger `{t}` is not lowercase"),
            ));
        }
        // Note: a programmatic singular-form check produces too many false
        // positives for Unity-domain terms like `addressables` and `canvas`,
        // so the contract treats singular form as a documentation guideline
        // and the linter only enforces lowercase + non-empty + uniqueness.
        if !seen.insert(t.clone()) {
            v.push(err(
                RuleId::R05Triggers,
                skill,
                format!("duplicate trigger `{t}`"),
            ));
        }
    }
    v
}

// ---------- R06: siblings exist ----------
pub fn rule_r06(skill: &Skill, ctx: &RuleContext<'_>) -> Vec<Violation> {
    let known: BTreeSet<&str> = ctx.skills.iter().map(|s| s.name.as_str()).collect();
    let mut v = Vec::new();
    for sibling in &skill.frontmatter.metadata.siblings {
        if !known.contains(sibling.as_str()) {
            v.push(err(
                RuleId::R06SiblingsExist,
                skill,
                format!("sibling `{sibling}` does not exist among loaded skills"),
            ));
        }
    }
    v
}

// ---------- R07: description length ----------
pub fn rule_r07(skill: &Skill) -> Vec<Violation> {
    let desc = skill.description();
    if desc.chars().count() > 1024 {
        vec![err(
            RuleId::R07DescriptionLength,
            skill,
            format!(
                "`description` length {} > 1024 (Anthropic hard limit)",
                desc.chars().count()
            ),
        )]
    } else {
        Vec::new()
    }
}

// ---------- R08: description front-loaded with at least one trigger ----------
pub fn rule_r08(skill: &Skill) -> Vec<Violation> {
    let desc = skill.description();
    if desc.is_empty() || skill.triggers().is_empty() {
        return Vec::new();
    }
    let head: String = desc.chars().take(250).collect();
    let head_lc = head.to_lowercase();
    let any = skill
        .triggers()
        .iter()
        .any(|t| head_lc.contains(&t.to_lowercase()));
    if any {
        Vec::new()
    } else {
        vec![err(
            RuleId::R08DescriptionFrontTriggers,
            skill,
            "no `metadata.triggers` token appears in the first 250 characters of `description`",
        )]
    }
}

// ---------- R09: description third-person / no first or second person ----------
pub fn rule_r09(skill: &Skill) -> Vec<Violation> {
    let desc = skill.description();
    if desc.is_empty() {
        return Vec::new();
    }
    let patterns = [
        r"\bI\s",
        r"\bI'm\b",
        r"\bwe\s",
        r"\bwe're\b",
        r"\byou can\b",
        r"\byou'll\b",
        r"\blet me\b",
        r"\bour\b",
        "お手伝い",
    ];
    let mut v = Vec::new();
    for pat in patterns {
        let re = Regex::new(pat).unwrap();
        if re.is_match(desc) {
            v.push(err(
                RuleId::R09DescriptionPerson,
                skill,
                format!("`description` contains first/second-person pattern `{pat}`"),
            ));
        }
    }
    v
}

// ---------- R10: description has Use when / Do not use ----------
pub fn rule_r10(skill: &Skill) -> Vec<Violation> {
    let desc = skill.description();
    if desc.is_empty() {
        return Vec::new();
    }
    let lc = desc.to_lowercase();
    let has_use_when = lc.contains("use when");
    let has_dont = lc.contains("do not use") || lc.contains("not for ");
    let mut v = Vec::new();
    if !has_use_when {
        v.push(err(
            RuleId::R10DescriptionUseDoNotUse,
            skill,
            "`description` is missing `Use when ` clause",
        ));
    }
    if !has_dont {
        v.push(err(
            RuleId::R10DescriptionUseDoNotUse,
            skill,
            "`description` is missing `Do not use ` (or `Not for `) clause",
        ));
    }
    v
}

// ---------- R11: description mentions at least one sibling ----------
pub fn rule_r11(skill: &Skill) -> Vec<Violation> {
    let siblings = skill.siblings();
    if siblings.is_empty() {
        return Vec::new();
    }
    let desc = skill.description();
    let mentioned = siblings.iter().any(|s| desc.contains(s.as_str()));
    if mentioned {
        Vec::new()
    } else {
        vec![err(
            RuleId::R11DescriptionMentionsSibling,
            skill,
            format!("`description` does not mention any of the declared siblings: {siblings:?}"),
        )]
    }
}

// ---------- R12: sibling bidirectionality ----------
pub fn rule_r12(skill: &Skill, ctx: &RuleContext<'_>) -> Vec<Violation> {
    let mut v = Vec::new();
    let by_name: BTreeMap<&str, &Skill> = ctx.skills.iter().map(|s| (s.name.as_str(), s)).collect();
    for sibling in skill.siblings() {
        let other = match by_name.get(sibling.as_str()) {
            Some(s) => *s,
            None => continue, // R06 reports.
        };
        if !other.siblings().iter().any(|s| s == &skill.name) {
            v.push(err(
                RuleId::R12SiblingBidirectional,
                skill,
                format!(
                    "sibling `{sibling}` does not list `{}` back in its own siblings",
                    skill.name
                ),
            ));
        }
    }
    v
}

// ---------- R13: body line count ----------
pub fn rule_r13(skill: &Skill) -> Vec<Violation> {
    let n = skill.body_line_count();
    if n > 500 {
        vec![err(
            RuleId::R13BodyMaxLines,
            skill,
            format!("SKILL.md body has {n} lines > 500 max"),
        )]
    } else {
        Vec::new()
    }
}

// ---------- R14: required H2 sections in order ----------
pub fn rule_r14(skill: &Skill) -> Vec<Violation> {
    let required = [
        "Use When",
        "Do Not Use When",
        "Preferred Flow",
        "Examples",
        "References",
    ];
    let mut idx = 0usize;
    for line in skill.body.lines() {
        let trimmed = line.trim_start();
        if let Some(rest) = trimmed.strip_prefix("## ") {
            // Allow CRITICAL: or IMPORTANT: prefixes per S-1.4.
            let cleaned = rest
                .trim_start_matches("CRITICAL:")
                .trim_start_matches("IMPORTANT:")
                .trim();
            if idx < required.len() && cleaned.eq_ignore_ascii_case(required[idx]) {
                idx += 1;
            }
        }
    }
    if idx == required.len() {
        Vec::new()
    } else {
        vec![err(
            RuleId::R14BodyHeadings,
            skill,
            format!(
                "missing or out-of-order required H2 sections; expected `## {}` next",
                required[idx]
            ),
        )]
    }
}

// ---------- R15: References section has at least one link ----------
pub fn rule_r15(skill: &Skill) -> Vec<Violation> {
    let mut in_refs = false;
    let mut found_link = false;
    let link_re = Regex::new(r"\[[^\]]+\]\([^)]+\)").unwrap();
    for line in skill.body.lines() {
        let trimmed = line.trim_start();
        if let Some(rest) = trimmed.strip_prefix("## ") {
            let cleaned = rest.trim();
            if cleaned.eq_ignore_ascii_case("References") {
                in_refs = true;
                continue;
            } else if in_refs {
                break;
            }
        }
        if in_refs && link_re.is_match(line) {
            found_link = true;
            break;
        }
    }
    if found_link {
        Vec::new()
    } else {
        vec![err(
            RuleId::R15ReferencesHasLink,
            skill,
            "`## References` section has no markdown link",
        )]
    }
}

// ---------- R16: time-sensitive vocabulary ----------
pub fn rule_r16(skill: &Skill) -> Vec<Violation> {
    let patterns = [
        r"\bas of\b",
        r"\buntil next release\b",
        r"\bcurrently\b",
        "現時点では",
    ];
    let mut v = Vec::new();
    for pat in patterns {
        let re = Regex::new(pat).unwrap();
        if re.is_match(&skill.body) {
            v.push(err(
                RuleId::R16BodyTimeSensitive,
                skill,
                format!("body contains time-sensitive phrase matching `{pat}`"),
            ));
        }
    }
    v
}

// ---------- R17: references/runtime-checklist.md exists ----------
pub fn rule_r17(skill: &Skill) -> Vec<Violation> {
    let path = skill.dir.join("references/runtime-checklist.md");
    if path.exists() {
        Vec::new()
    } else {
        vec![err(
            RuleId::R17RuntimeChecklist,
            skill,
            "missing `references/runtime-checklist.md`",
        )]
    }
}

// ---------- R18: long references must have ToC ----------
pub fn rule_r18(skill: &Skill) -> Vec<Violation> {
    let mut v = Vec::new();
    for ref_path in &skill.references {
        let raw = match fs::read_to_string(ref_path) {
            Ok(s) => s,
            Err(_) => continue,
        };
        let lines: Vec<&str> = raw.lines().collect();
        if lines.len() <= 100 {
            continue;
        }
        let head = lines.iter().take(15);
        let has_toc = head.into_iter().any(|line| {
            let l = line.trim().to_lowercase();
            l.starts_with("## table of contents")
                || l.starts_with("## toc")
                || l.starts_with("## 目次")
        });
        if !has_toc {
            v.push(Violation::new(
                RuleId::R18ReferenceToc,
                Severity::Error,
                &skill.name,
                ref_path,
                format!(
                    "reference file is {} lines (> 100) but missing `## Table of Contents` in the first 15 lines",
                    lines.len()
                ),
            ));
        }
    }
    v
}

// ---------- R19: reference files do not link to other reference files ----------
pub fn rule_r19(skill: &Skill) -> Vec<Violation> {
    let link_re = Regex::new(r"\[[^\]]+\]\(([^)]+)\)").unwrap();
    let mut v = Vec::new();
    for ref_path in &skill.references {
        let raw = match fs::read_to_string(ref_path) {
            Ok(s) => s,
            Err(_) => continue,
        };
        for cap in link_re.captures_iter(&raw) {
            let target = cap.get(1).map(|m| m.as_str()).unwrap_or("");
            if target.starts_with("http") || target.starts_with("#") {
                continue;
            }
            // If link points to another file inside references/, that's a 2-level nest.
            let resolved = ref_path
                .parent()
                .map(|p| p.join(target))
                .unwrap_or_else(|| PathBuf::from(target));
            if let Ok(canon) = resolved.canonicalize() {
                if canon.components().any(|c| c.as_os_str() == "references") && canon != *ref_path {
                    v.push(Violation::new(
                        RuleId::R19ReferenceNesting,
                        Severity::Error,
                        &skill.name,
                        ref_path,
                        format!("reference file links to another reference file `{target}`"),
                    ));
                }
            }
        }
    }
    v
}

// ---------- R20 / R21: symlink integrity ----------
pub fn rule_r20(skill: &Skill, ctx: &RuleContext<'_>) -> Vec<Violation> {
    let parent = ctx.repo_root.join(".claude/skills");
    if !parent.is_dir() {
        return Vec::new();
    }
    let link = parent.join(&skill.name);
    check_symlink(skill, &link, RuleId::R20ClaudeSymlink)
}

pub fn rule_r21(skill: &Skill, ctx: &RuleContext<'_>) -> Vec<Violation> {
    let parent = ctx.repo_root.join(".agents/skills");
    if !parent.is_dir() {
        return Vec::new();
    }
    let link = parent.join(&skill.name);
    check_symlink(skill, &link, RuleId::R21AgentsSymlink)
}

fn canonical_skill_link(link: &Path) -> std::io::Result<PathBuf> {
    #[cfg(windows)]
    if link.is_file() && !link.is_symlink() {
        // Git with core.symlinks=false checks out a symlink as a file containing
        // its relative target. Validate that target against the canonical skill
        // below, without requiring Developer Mode or changing checkout files.
        let text = fs::read_to_string(link)?;
        let target = Path::new(&text);
        if !text.is_empty() && !text.chars().any(char::is_control) && target.is_relative() {
            return link
                .parent()
                .unwrap_or(Path::new("."))
                .join(target)
                .canonicalize();
        }
    }
    link.canonicalize()
}

fn check_symlink(skill: &Skill, link: &Path, rule: RuleId) -> Vec<Violation> {
    if !link.exists() && !link.is_symlink() {
        return vec![Violation::new(
            rule,
            Severity::Error,
            &skill.name,
            link,
            format!("symlink `{}` is missing", link.display()),
        )];
    }
    let target = match canonical_skill_link(link) {
        Ok(p) => p,
        Err(_) => {
            return vec![Violation::new(
                rule,
                Severity::Error,
                &skill.name,
                link,
                format!("symlink `{}` is dangling", link.display()),
            )];
        }
    };
    let canonical_skill = match skill.dir.canonicalize() {
        Ok(p) => p,
        Err(_) => skill.dir.clone(),
    };
    if target != canonical_skill {
        vec![Violation::new(
            rule,
            Severity::Error,
            &skill.name,
            link,
            format!(
                "symlink `{}` points to `{}` but expected `{}`",
                link.display(),
                target.display(),
                canonical_skill.display()
            ),
        )]
    } else {
        Vec::new()
    }
}

// ---------- R22: trigger collisions require sibling cross-listing ----------
pub fn rule_r22(skill: &Skill, ctx: &RuleContext<'_>) -> Vec<Violation> {
    let mut v = Vec::new();
    for trigger in skill.triggers() {
        for other in ctx.skills {
            if other.name == skill.name {
                continue;
            }
            if other.triggers().iter().any(|t| t == trigger) {
                let a_lists_b = skill.siblings().iter().any(|s| s == &other.name);
                let b_lists_a = other.siblings().iter().any(|s| s == &skill.name);
                if !(a_lists_b && b_lists_a) {
                    v.push(err(
                        RuleId::R22TriggerCollision,
                        skill,
                        format!(
                            "trigger `{trigger}` collides with `{}` but siblings are not cross-listed",
                            other.name
                        ),
                    ));
                }
            }
        }
    }
    v
}
