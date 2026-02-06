---
name: youtrack-fix
description: fix issues reported in youtrack cards, which start with "BL-", e.g. "BL-1234"
---

You will be given a url or an issue number starting with "BL". If you have just the issue number, then the URL is https://issues.bloomlibrary.org/youtrack/issue/<issue-number>.
Begin by making a plan: read the code base as needed. If you want clarification from me, use the askQuestions tool. Once you have a plan, print it out and then use the askQuestions tool to ask me if you can proceed.

If the current branch is not "master", "main", or "VersionX.Y" (e.g. "Version6.3"), then this means another agent is already working on an issue. Use the askQuestions tool to tell me that and ask me if I am ready for you to proceed. At this point ensure that the current branch is "master", "main", or "VersionX.Y" (e.g. "Version6.3"). If it isn't, ask me if I can fix the situation.

Then create a new branch that is named "<issue-number>-<a-few-words>". Then do everything in the plan.

# Committing

Run Prettier on every changed file (from the repo root run `yarn prettier --write path/to/changed-file`) and make sure your editor has saved everything. Stage all code changes, commit with a message Then make a commit that begins with "Fix <issue-number> <card summary>". Add the URL of the youtrack issue to the commit description. Explicitly say if you ran tests and what the results were. Add a summary of the change to the description. Do not push.

ABSOLUTELY NEVER run destructive git operations (e.g., git reset --hard, rm, git checkout/git restore to an older commit) unless the user gives an explicit, written instruction in this conversation. Treat these commands as catastrophic; if you are even slightly unsure, stop and ask before touching them. (When working within Cursor or Codex Web, these git limitations do not apply; use the tooling's capabilities as needed.)
Never use git restore (or similar commands) to revert files you didn't author—coordinate with other agents instead so their in-progress work stays intact.

Always double-check git status before any commit. Make sure you ran prettier on every changed file.

Make sure that the commit has only the changes for this card, because there are other agents working on issues at the same time.

Do not push unless I direct you to make a PR. If you make a PR, add a comment saying "<your name> submitted <URL of the PR>".
