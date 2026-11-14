---
name: tui-design
description: "Create distinctive, production-grade terminal user interfaces. Use when building TUI components with ratatui, CLI output styling, or xterm.js terminal rendering. Triggers: 'design TUI', 'terminal UI', 'TUIデザイン', 'ターミナルUI', 'ratatui widget'"
allowed-tools: Bash, Read, Glob, Grep, Edit, Write
---

# TUI Design

Create distinctive, production-grade terminal user interfaces with high design quality. Use this skill when building TUI components, CLI tools, or terminal-based interfaces. Generate creative, polished code that avoids generic terminal aesthetics.

## gwt Technology Context

- **Primary TUI framework:** Rust ratatui (gwt-core TUI migration target)
- **Terminal emulation:** xterm.js v6 (gwt-gui, rendered in Tauri WebView)
- **Design principle:** シンプルさの極限を追求しつつ、ユーザビリティと開発者体験の品質は妥協しない (CLAUDE.md)
- **Scope:** TUI/CLI/terminal rendering — Web UI components use `frontend-design` skill instead

## Design Thinking

Before coding, understand the context and commit to a BOLD aesthetic direction:

1. **Purpose:** What problem does this interface solve? Who uses it? What's the workflow?
2. **Tone:** Pick an extreme: hacker/cyberpunk, retro-computing (80s/90s), minimalist zen, maximalist dashboard, synthwave neon, monochrome brutalist, corporate mainframe, playful/whimsical, matrix-style, steampunk terminal, vaporwave, military/tactical, art deco, paper-tape nostalgic
3. **Constraints:** Technical requirements (Rust ratatui, xterm.js, pure ANSI escape codes)
4. **Differentiation:** What makes this UNFORGETTABLE? What's the one thing someone will remember about this terminal experience?

Choose a clear conceptual direction and execute it with precision. A dense information dashboard and a zen single-focus interface both work — the key is intentionality, not intensity.

## Box Drawing & Borders

Choose border styles that match your aesthetic:

- Single line: `┌─┐│└┘` — Clean, modern
- Double line: `╔═╗║╚╝` — Bold, formal, retro-mainframe
- Rounded: `╭─╮│╰╯` — Soft, friendly, modern
- Heavy: `┏━┓┃┗┛` — Strong, industrial
- Dashed/Dotted: `┄┆` — Light, airy, informal
- ASCII only: `+-+|` — Retro, universal compatibility
- Block characters: `█▀▄▌▐` — Chunky, bold, brutalist
- Custom Unicode: Mix symbols like `◢◣◤◥`, `●○◐◑`, `▲▼◀▶` for unique frames

Avoid defaulting to simple single-line boxes. Consider asymmetric borders, double-thick headers, or decorative corners like `◆`, `◈`, `✦`, `⬡`.

## Color & Theme

Commit to a cohesive palette. Terminal color strategies:

- **ANSI 16:** Classic, universal. Craft distinctive combinations beyond default red/green/blue
- **256-color:** Rich palettes. Use color gradients, subtle background variations
- **True color (24-bit):** Full spectrum. Gradient text, smooth color transitions
- **Monochrome:** Single color with intensity variations (dim, normal, bold, reverse). Elegant constraint

Create atmosphere with:

- Background color blocks for sections
- Gradient fills using block characters `░▒▓█`
- Color-coded semantic meaning (but avoid cliché red=bad, green=good)
- Inverted/reverse video for emphasis
- Dim text for secondary information, bold for primary

Palette examples (invent your own):

- **Cyberpunk:** Hot pink `#ff00ff`, electric cyan `#00ffff`, deep purple `#1a0a2e` background
- **Amber terminal:** `#ffb000` on black, like vintage CRTs
- **Nord-inspired:** Cool blues and muted greens on dark blue-gray
- **Hot Dog Stand:** Intentionally garish yellow/red (for playful/ironic UIs)

## Typography & Text Styling

The terminal is ALL typography. Make it count:

- **ASCII art headers:** Use figlet-style banners, custom letterforms, or Unicode art
- **Text weight:** Bold, dim, normal — create visual hierarchy
- **Text decoration:** Underline, strikethrough, italic (where supported)
- **Letter spacing:** Simulate with spaces for headers: `H E A D E R`
- **Case:** ALL CAPS for headers, lowercase for body, mixed for emphasis
- **Unicode symbols:** Enrich text with `→ • ◆ ★ ⚡ λ ∴ ≡ ⌘`
- **Custom bullets:** Replace `-` with `▸ ◉ ✓ ⬢ ›` or themed symbols

ASCII Art Styles:

```text
Block:    ███████╗██╗██╗     ███████╗
Slant:    /___  / / // /     / ____/
Small:    ╔═╗┌─┐┌─┐
Minimal:  [ HEADER ]
```

## Layout & Spatial Composition

Break free from single-column output:

- **Panels & Windows:** Create distinct regions with borders
- **Columns:** Side-by-side information using careful spacing
- **Tables:** Align data meaningfully, use Unicode table characters
- **Whitespace:** Generous padding inside panels, breathing room between sections
- **Density:** Match to purpose — dashboards can be dense, wizards should be sparse
- **Hierarchy:** Clear visual distinction between primary content, secondary info, and chrome
- **Asymmetry:** Off-center titles, weighted layouts, unexpected alignments

## Motion & Animation

Terminals support dynamic content:

- **Spinners:** Beyond basic `|/-\`. Use Braille patterns `⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏`, dots `⣾⣽⣻⢿⡿⣟⣯⣷`, custom sequences
- **Progress bars:** `▓░`, `█▒`, `[=====>    ]`, or creative alternatives like `◐◓◑◒`
- **Typing effects:** Reveal text character-by-character for drama
- **Transitions:** Wipe effects, fade in/out with color intensity
- **Live updates:** Streaming data, real-time charts

## Data Display

- **Sparklines:** `▁▂▃▄▅▆▇█` for inline mini-charts
- **Bar charts:** Horizontal bars with block characters
- **Tables:** Smart column sizing, alternating row colors, aligned numbers
- **Trees:** `├── └── │` for hierarchies
- **Status indicators:** `●` green, `○` empty, `◐` partial, `✓` complete, `✗` failed
- **Gauges:** `[████████░░]` with percentage

## Decorative Elements

Add character without clutter:

- **Dividers:** `───── ═════ •••••• ░░░░░░ ≋≋≋≋≋≋`
- **Section markers:** `▶ SECTION`, `[ SECTION ]`, `─── SECTION ───`, `◆ SECTION`
- **Background textures:** Patterns using light characters like `· ∙ ░`
- **Icons:** Nerd Font icons if available: `󰊢`

## Anti-Patterns to Avoid

NEVER use generic terminal aesthetics like:

- Plain unformatted text output
- Default colors without intentional palette
- Basic `[INFO]`, `[ERROR]` prefixes without styling
- Simple `----` dividers
- Walls of unstructured text
- Generic progress bars without personality
- Boring help text formatting
- Inconsistent spacing and alignment

## Library Quick Reference

| Framework | Language | Notes |
|-----------|----------|-------|
| **ratatui** | **Rust** | **gwt primary TUI framework** |
| xterm.js v6 | TypeScript | gwt-gui terminal emulation (Tauri WebView) |
| Rich, Textual | Python | General purpose |
| Bubbletea, Lipgloss | Go | General purpose |
| Ink, Blessed | Node.js | General purpose |

### ANSI Escape Codes

```text
\x1b[1m           Bold
\x1b[3m           Italic
\x1b[4m           Underline
\x1b[31m          Red foreground
\x1b[38;2;R;G;Bm  True color
\x1b[2J           Clear screen
```

## Guiding Principle

The terminal is a canvas with unique constraints and possibilities. Don't just print text — craft an experience.

Match implementation complexity to the aesthetic vision. A dense monitoring dashboard needs elaborate panels and live updates. A minimal CLI needs restraint, precision, and perfect alignment. Elegance comes from executing the vision well.
