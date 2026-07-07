# Claude Code instructions

This project's agent guidance lives in various `AGENTS.md` files (shared by all agents). Read and follow them.

@AGENTS.md

In particular, see the **Skills** section: when a request matches a skill, open the relevant
`.github/skills/<name>/SKILL.md` and follow it.

Cross-repo team workflow skills (`preflight`, `pr-ready-for-human`, `devin-review`,
`reviewable-replies`, and the `youtrack-*` skills — e.g. when the user gives a YouTrack issue
id like `BL-1234`, follow `youtrack-fix`) live in
https://github.com/BloomBooks/bloom-team-skills. Install them per that repo's README (clone,
then symlink each skill into `~/.claude/skills` so they are discovered globally).


Whenever a task adds, modifies, or reviews localizable strings (XLF entries), automatically
follow `.github/skills/xlf-strings/SKILL.md` — no explicit request needed.