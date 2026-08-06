---
description: create a document to help in process large numbers of pr comments
---
1) if I didn't tell you the name of the document, use the askQuestions tool to ask me. Always include the option "append or create pr-comments.md". If there is already a "pr-comments.md", then also offer one with a unique name that we don't have yet, like "pr-comments-2.md". Do this quickly so that I can leave while you work on the rest.
2) Find the pr. Use the gh tool to determine the PR associated with the current branch.
3) Collect up comments that do that have a reply. Do not rely only on native GitHub review threads. Also inspect the PR's reviews and review bodies for Reviewable-imported comments or discussion summaries, because those may not appear in `reviewThreads` even when the review says it contains many unresolved comments. Treat those Reviewable comments as part of the PR feedback you need to process.

4) for each issue, add some lines like this:

----------------------
## Fred Flintstone review 3918647127

### 1. foo should be bar

Link: <https://reviewable.io/reviews/BloomBooks/BloomDesktop/7621#-On-YsWVBDGHZLABoBtc:-On3jl8a-ol2j7BjS4fE:b-nlm6k9>
Relevant code locations: bedrock.ts, line 52.

What they said:
| The foo here should be bar, shouldn't it?   <-- verbatim quote. Do not paraphrase.

Evaluation:
Bar would't be bad, and we can make this change cheaply. The complication is that we already have a bar.

Action Proposals:
1. Change foo to baz <-- proposed
2. Change foo to bar2
3. Say that <this user> prefers to stick with foo.

- Proposed Reply: "[model name]: It turns out "bar" is already in use, so I've changed to "baz". <--- do not be chatty, just state what you did (or would do)

User Decision:  <-- user will fill this in later
Reply:

- [ ] Reply successfully posted (check off when posted to github or Reviewable):

-------------------

Leave the "decision:" and "reply" lines there for a future step in our process.

When you answer, prefix your response with the name of your model, e.g. [hall9000].
