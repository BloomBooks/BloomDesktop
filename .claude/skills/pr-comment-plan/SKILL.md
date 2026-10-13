---
name: pr-comment-plan
description: Build a working document (pr-comments.md) that lists every unanswered review comment on the current PR with a verbatim quote, an evaluation, ranked action proposals and a slot for the developer's decision, so a large batch of review feedback can be triaged offline before anything is replied to. Use when asked to "make a PR comment plan", "collect the review comments into a document", or to triage many PR comments at once.
argument-hint: "optional: the document name (default pr-comments.md) and the PR number"
---

# PR comment plan

The job: gather the PR's unanswered review feedback into one Markdown document the developer
can read, decide on, and hand back. Nothing is replied to or changed in this skill; the
document is the deliverable. (Replying happens later, through `reviewable-replies` for
Reviewable threads or `gh` for GitHub threads.)

1. **Pick the document name.** If the user did not name one, ask, offering "append to or create
   `pr-comments.md`" and, if that file already exists, a fresh unique name such as
   `pr-comments-2.md`. Ask this first and quickly so the user can leave while you work.
2. **Find the PR** for the current branch with `gh` (`gh pr view --json number,url`). If there
   is none, ask for a URL.
3. **Collect the comments that have no reply.** Do not rely only on GitHub's `reviewThreads`.
   Also read the PR's reviews and review bodies: Reviewable-imported comments and discussion
   summaries often appear only there, even when the review says it contains many unresolved
   comments. Treat those as part of the feedback to process.
4. **Write one block per comment**, in this shape:

   ```markdown
   ----------------------
   ## Fred Flintstone review 3918647127

   ### 1. foo should be bar

   Link: <https://reviewable.io/reviews/BloomBooks/BloomDesktop/7621#-On-YsWVBDGHZLABoBtc>
   Relevant code locations: bedrock.ts, line 52.

   What they said:
   | The foo here should be bar, shouldn't it?      <-- verbatim quote; never paraphrase

   Evaluation:
   Bar wouldn't be bad, and the change is cheap. The complication is that we already have a bar.

   Action Proposals:
   1. Change foo to baz                             <-- the proposed one first
   2. Change foo to bar2
   3. Say that <reviewer> prefers to stick with foo.

   - Proposed Reply: "[<attribution tag>] "bar" is already in use, so I've changed to "baz"."
                                                    <-- not chatty; state what you did or would do

   User Decision:                                   <-- the developer fills this in later
   Reply:

   - [ ] Reply successfully posted (tick when posted to GitHub or Reviewable)
   -------------------
   ```

   Leave the `User Decision:` and `Reply:` lines empty; a later step in the process fills them.
   The proposed reply starts with the team attribution tag (`TEAM-AGENTS.md`, "Attribution"),
   since it will be posted under the developer's account.
