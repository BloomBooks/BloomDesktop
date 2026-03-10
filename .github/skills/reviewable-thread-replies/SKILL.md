---
name: reviewable-thread-replies
description: 'Reply to GitHub and Reviewable PR discussion threads one-by-one. Use whenever the user asks you to respond to review comments with accurate in-thread replies and verification.'
argument-hint: 'Repo/PR and target comments to reply to (for example: BloomBooks/BloomDesktop#7557 + specific discussion links/IDs)'
---

# Reviewable Thread Replies

## What This Skill Does
Checks whether PR discussion threads still need attention and, only when they do, posts in-thread replies on both:
- GitHub PR review comments (`discussion_r...`)
- Reviewable-only discussion anchors quoted in review bodies

## When To Use
- The user asks you to respond to one or more PR comments.
- Some comments are directly replyable on GitHub, while others only exist as Reviewable anchors.
- You need one response per thread, posted in the right place.
- You have first confirmed that the thread does not already have a verified reply with no newer reviewer follow-up.

## Inputs
- figure out the PR using the gh cli
- Target links or IDs (GitHub `discussion_r...` or Reviewable `#-...` anchors), or enough context to discover them.
- Reply text supplied by user, or instruction to compose replies from thread context.
- If working from a markdown tracking file, current checkbox/reply status for each target.

## Finding Threads Efficiently
- Prefer DOM-based discovery over screenshots or image-style inspection.
- Use text from the review comment, file path, line number, and nearby thread controls to locate the live thread in the DOM.
- Prefer page locators, `querySelector`, and `innerText`/text matching to find the right discussion and its active composer.
- Use page snapshots or screenshots only for coarse orientation when the DOM path is temporarily unclear.

## Known Reviewable DOM
- These are observed DOM patterns, not stable public APIs. Reuse them when they still match, but fall back to comment-text scoping if Reviewable changes.
- Thread container: discussion content is commonly under `.discussion.details`.
- Thread working scope: for posting, the useful scope is often the parent just above `.discussion.details`, because that scope also contains the reply launcher and draft composer.
- Reply launcher: thread-local inputs commonly use `input.response-input` with placeholder `Reply…` or `Follow up…`.
- Open draft composer: active draft blocks commonly include `.relative.discussion.bottom` and `.ui.draft.comments.form`.
- Draft textarea: the editable reply body has been observed as `textarea.draft.display.textarea.mp-sensitive.sawWritingArea`.
- Send control: the post action has been observed as `.ui.basic.large.icon.send.button.item`.
- Nearby non-post controls: status buttons can appear very close to the launcher or composer, including `DONE`, `RETRACT`, `ACKNOWLEDGE`, and `RESOLVE`.
- Thread discovery pattern: find the reviewer comment text first, then scope DOM queries inside that thread instead of searching globally for launchers or textareas.
- Virtualization warning: off-screen discussions may be detached or recycled, so old handles can become stale after scrolling or reload.

## Required Reply Format
- If the user supplies exact reply text, post that exact text.
- Otherwise, begin the composed reply with `[<agent name>]`.
- Do not prepend workflow labels (for example `Will do, TODO`).
- Do not use dismissive framing such as `left as-is`, `not worth churn`, `I wouldn't bother`, or similar language that downplays a reviewer's concern.  It is very good to evaluate whether we want to make a change or not, but always get the user's OK before deciding not to make a code change, but if you do end up skipping a change, explain the reasoning clearly and respectfully in the reply.
- If no code change is made, reply with a concrete explanation of the current behavior, the reasoning, and any follow-up you did instead.

## Procedure
1. Collect and normalize targets.
- Build a list of target threads with: `target`, `context`, `response`.
- If response text is not provided, defer composing it until after you confirm the thread still needs a reply.
- Separate items into:
  - GitHub direct thread comments (have comment IDs / `discussion_r...`).
  - Reviewable-only threads (anchor IDs like `-Oko...`).

2. Determine whether each target still needs attention.
- For GitHub direct thread comments, inspect the existing thread replies before drafting anything new.
- For Reviewable-only threads, inspect the visible thread history in the DOM before drafting anything new.
- If a verified reply from us already exists and there is no newer follow-up from the original commenter or another participant asking for more action, mark the target `already handled` and skip it.
- If a markdown tracking file already marks the item and its `reply:` line as completed, treat that as a strong signal that the thread may already be handled and verify against the live thread before doing more work.
- If the tracking file says `No further comment needed`, or equivalent, verify that the thread already has the expected reply and no newer follow-up; if so, skip it.
- Only compose or post a new reply when there is no verified existing reply, or when a newer reviewer comment arrived after the last verified reply.

3. Post direct GitHub thread replies first.
- Use GitHub PR review comment reply API/tool for each direct comment ID.
- Post exactly one response per thread.
- Verify the new reply IDs/URLs are returned.

4. Open Reviewable and navigate to the PR/thread.
- Wait for Reviewable permissions/loading state to settle before concluding that replying is blocked.
- Check whether you are already signed in before assuming auth is the problem.
- If Reviewable is not signed in, click `Sign in`.
- Use the askQuestions tool to get the user's attention and wait for them to confirm they have completed sign-in.
- After the user confirms sign-in, reload or re-check the thread and confirm the reply controls appear before posting.
- When locating the target thread, prefer DOM text search and scoped locators over visual inspection.

5. Reply to Reviewable-only threads one by one.
- For each discussion anchor:
  - Navigate to the anchor.
  - Expand/open the target file or discussion until the inline thread is rendered.
  - Check the existing visible thread history before opening a reply composer.
  - Prefer this fast path when using Playwright locators: find the reviewer comment text, climb to the nearest `.discussion.details`, then use its parent scope for launcher/composer queries.
  - Find the small thread reply launcher for that discussion. In current Reviewable UI this may be `Reply…` or `Follow up…`.
  - After clicking the launcher, wait for the draft composer to replace it; the textarea may not appear synchronously.
  - Type into the launcher to open the draft composer.
  - If the draft composer is already open, skip the launcher and reuse the visible draft textarea instead of trying to reopen it.
  - Enter the actual reply body into the draft textarea that appears below. Do not assume typing into `Follow up…` posts the reply.
  - After filling the draft textarea, wait for the send arrow control to become enabled before clicking it.
  - Submit the draft using the send arrow control.
  - Post the user-supplied text exactly, or if composing the reply yourself, add the required `[<agent name>]` prefix.
  - Avoid adding status macros or extra prefixes.
  - Never use nearby status controls like `DONE`, `RETRACT`, `ACKNOWLEDGE`, or `RESOLVE` as a substitute for posting the reply.
- Wait for each post to render before moving to the next thread.

6. Verification pass.
- Re-check every target thread and confirm the expected response appears.
- Distinguish a saved draft from a posted reply: `Draft` / `draft saved` / a visible editor is not sufficient.
- Reload the page and confirm the reply still appears in the thread after the fresh render.
- Confirm no target remains unreplied due to navigation/context loss.
- Confirm no accidental text prefixes were added.
- Confirm no duplicate reply was posted to a thread that was already handled.
- If you are working from a markdown tracking file, convert the completed item line into a checked checkbox only after the reload verification succeeds. If there is a "reply:" line, make sure to also make that into a checkbox and check it so that the user knows for sure that you posted the reply successfully.

## Decision Points
- If a target already has a verified reply from us and no newer reviewer follow-up, skip it and report `already handled` instead of drafting a new reply.
- If a tracking markdown file marks the item as replied or says no further comment is needed, verify that against the live thread before doing anything else.
- If the tracking markdown and the live thread disagree, use the live thread as the source of truth and explain the mismatch.
- If target has GitHub comment ID: use GitHub API/tool reply path.
- If target exists only in Reviewable anchor: use browser automation path.
- If Reviewable initially shows `Checking permissions` or a temporary signed-out header state: wait for the page to settle and open the target thread before deciding auth is required.
- If Reviewable is not signed in, click `Sign in`, use askQuestions to wait for the user to finish auth, then retry.
- If the inline thread never shows `Reply…`, `Follow up…`, or an already-open draft composer after that wait: authenticate first, then retry.
- If multiple visually identical reply launchers exist, use DOM scoping from the target comment text instead of image-based picking.
- A reliable Playwright pattern is: locate the comment by text, derive the thread scope from the nearest `.discussion.details` ancestor, then query `input.response-input`, `.ui.draft.comments.form`, `textarea.draft.display.textarea.mp-sensitive.sawWritingArea`, and `.ui.basic.large.icon.send.button.item` inside that scope.
- Never click `resolve`, `done`, or `acknowledge` controls and never change discussion resolution state.
- If reply input transitions into a draft composer panel:
  - Treat the draft composer as the real editor and the `Reply…` / `Follow up…` input as only the launcher.
  - Submit without modifying response text semantics.
  - If you are composing the reply, keep the required `[<agent name>]` prefix. If the user gave exact text, preserve it exactly. Avoid workflow labels.
- If Reviewable virtualizes the thread list and your earlier input handle disappears, re-find the thread by its comment text and continue from the live on-screen composer instead of relying on stale selectors.
- If posted text does not match intended response: correct immediately before continuing.

## Quality Criteria
- Exactly one intended response posted per target thread.
- No new reply is posted to a thread that was already handled and had no newer reviewer follow-up.
- Responses are correct for thread context and preserve exact user text when supplied; otherwise they begin with `[<agent name>]`.
- No unwanted prefixes like `Will do, TODO`.
- No unresolved posting errors left undocumented.
- Tracking markdown, if used, is updated only after a verified successful post.
- Final status includes: posted targets and skipped/failed targets.

## Guardrails
- Do not post broad summary comments when thread-level replies were requested.
- Do not draft or post a fresh reply just because a comment appears in a review summary; first verify that the thread is still awaiting a response.
- Do not resolve, acknowledge, dismiss, or otherwise change PR discussion status; leave resolution actions to humans.
- Do not rely on internal/private page APIs for mutation unless officially supported and permission-safe.
- Do not assume draft state implies publication; verify thread-visible posted output.
- Do not continue after repeated auth/permission failures without reporting the blocker.
- Do not post dismissive or hand-wavy review replies; every reply should either describe the concrete code change made or give a specific technical explanation of the verified current behavior. You, the AI agent, are welcome to suggestion doing nothing to the user you are chatting with, but remember that we are not in a hurry, we are not lazy, we are not dismissive of reviewer concerns.

## Quick Command Hints
- List PR review comments:
```bash
 gh api repos/<owner>/<repo>/pulls/<pr>/comments --paginate
```

- List PR reviews (to inspect review-body quoted discussions):
```bash
 gh api repos/<owner>/<repo>/pulls/<pr>/reviews --paginate
```

