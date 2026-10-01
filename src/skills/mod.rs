//! Skill contract validation for Claude Code / Codex skills.
//!
//! Implements the `unity-cli skills lint` subcommand. Loads skill directories,
//! parses YAML frontmatter and SKILL.md body, and runs rule R01..R22 against
//! each skill to enforce the Skill Contract v1.

pub mod distribution;
pub mod loader;
pub mod model;
pub mod report;
pub mod rules;
pub mod runner;

#[cfg(test)]
mod tests;

#[allow(unused_imports)]
pub use model::{Frontmatter, RuleId, Severity, Skill, Violation};
#[allow(unused_imports)]
pub use runner::{lint, LintOptions, LintOutcome};
