---
name: improve-test-automation-coverage
description: '"improve-test-automation-coverage N" — claim N Notion test cases whose Automation is Planned and automate each in its own Orca worktree with a supervised Claude Fable 5.1 worker. Use when the developer says "improve test automation coverage", "burn tokens on test automation", or "automate N planned tests".'
argument-hint: "N — how many Planned test cases to automate in parallel (default 3); or 'case 349,350' to name the cards"
user-invocable: true
---

This is the slash-command entry point only. The procedure, the helper scripts, and the worker
brief live in `.github/skills/improve-test-automation-coverage/`. Open
`.github/skills/improve-test-automation-coverage/SKILL.md` and follow it exactly, with the
argument as N.
