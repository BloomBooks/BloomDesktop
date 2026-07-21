---
description: read and process comments in a pr. Only appropriate to use in the rare situations where developer pre-review isn't needed.
---

Use the gh tool to determine the PR associated with the current branch. If you cannot find one, use the askQuestions tool to ask the user for a url.
Read the unresolved pr comments and either answer them or handle the problem they call out and then answer them.
Do not rely only on native GitHub review threads. Also inspect the PR's reviews and review bodies for Reviewable-imported comments or discussion summaries, because those may not appear in `reviewThreads` even when the review says it contains many unresolved comments. Treat those Reviewable comments as part of the PR feedback you need to process.
When you answer, prefix your response with the name of your model, e.g. [hall9000].
