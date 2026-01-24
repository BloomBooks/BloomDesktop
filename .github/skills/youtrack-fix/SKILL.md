---
name: youtrack-fix
description: fix issues reported in youtrack cards
---

You will be given a url or an issue number starting with "BL". If you have just the issue number, then the URL is https://issues.bloomlibrary.org/youtrack/issue/<issue-number>.
Begin by making a plan: read the code base as needed. If you want clarification from me, use the askQuestions tool. Once you have a plan, use the askQuestions tool to get my ok to proceed. If you get the ok, assign it to card to me.

If the current branch is not "master", "main", or "VersionX.Y" (e.g. "Version6.3"), then this means another agent is already working on an issue. Use the askQuestions tool to tell me that and ask me if I am ready for you to proceed. At this point ensure that the current branch is "master", "main", or "VersionX.Y" (e.g. "Version6.3"). If it isn't, ask me if I can fix the situation.

Then create a new branch that is named "<issue-number><a-few-words>". Then do everything in the plan. Then make a commit that begins with "Fix <issue-number> <card summary>". Make sure that the commit has only the changes for this card, because there are other agents working on issues at the same time.

Do not push unless I direct you to make a PR. If you make a PR, add a comment saying "<your name> submitted <URL of the PR>".

