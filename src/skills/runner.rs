use anyhow::{anyhow, Context, Result};
use std::path::{Path, PathBuf};

use super::loader::load_skills;
use super::model::{Severity, Violation};
use super::rules::{run_all, RuleContext};

#[derive(Debug, Clone)]
pub struct LintOptions {
    pub root: PathBuf,
    pub repo_root: PathBuf,
    #[allow(dead_code)]
    pub severity: Severity,
}

#[derive(Debug, Clone)]
pub struct LintOutcome {
    pub skills: Vec<String>,
    pub violations: Vec<Violation>,
}

impl LintOutcome {
    pub fn has_errors(&self, severity: Severity) -> bool {
        match severity {
            Severity::Error => !self.violations.is_empty(),
            Severity::Warning => false,
        }
    }
}

pub fn lint(options: &LintOptions) -> Result<LintOutcome> {
    let skills = load_skills(&options.root)
        .with_context(|| format!("load skills from {}", options.root.display()))?;
    if skills.is_empty() {
        return Err(anyhow!(
            "no `unity-*` skills found under {}",
            options.root.display()
        ));
    }
    let ctx = RuleContext {
        skills: &skills,
        repo_root: &options.repo_root,
    };
    let mut violations = Vec::new();
    for skill in &skills {
        violations.extend(run_all(skill, &ctx));
    }
    violations.sort_by(|a, b| {
        a.skill
            .cmp(&b.skill)
            .then_with(|| a.rule.cmp(&b.rule))
            .then_with(|| a.message.cmp(&b.message))
    });
    Ok(LintOutcome {
        skills: skills.iter().map(|s| s.name.clone()).collect(),
        violations,
    })
}

/// Auto-discover the unity-cli skills root by walking upward from `cwd` until
/// `.claude-plugin/plugins/unity-cli/skills/` is found.
pub fn discover_root(cwd: &Path) -> Option<(PathBuf, PathBuf)> {
    let mut current = cwd;
    loop {
        let candidate = current.join(".claude-plugin/plugins/unity-cli/skills");
        if candidate.is_dir() {
            return Some((candidate, current.to_path_buf()));
        }
        match current.parent() {
            Some(parent) => current = parent,
            None => return None,
        }
    }
}
