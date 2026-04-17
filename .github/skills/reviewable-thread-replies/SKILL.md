---
name: reviewable-thread-replies
description: 'Reply to GitHub and Reviewable PR discussion threads one-by-one. Use whenever the user asks you to respond to review comments with accurate in-thread replies and verification.'
argument-hint: 'Repo/PR and target comments to reply to (for example: BloomBooks/BloomDesktop#7557 + specific discussion links/IDs)'
note: it's not clear that this skill is adequately developed, it's not clear that it works.
---

# Reviewable Thread Replies

## What This Skill Does
Posts in-thread replies on both:
- GitHub PR review comments (`discussion_r...`)
- Reviewable-only discussion anchors quoted in review bodies

## When To Use
- The user asks you to respond to one or more PR comments.
- Some comments are directly replyable on GitHub, while others only exist as Reviewable anchors.
- You need one response per thread, posted in the right place.

## Inputs
- figure out the PR using the gh cli
- Target links or IDs (GitHub `discussion_r...` or Reviewable `#-...` anchors), or enough context to discover them.
- Reply text supplied by user, or instruction to compose replies from thread context.

## Required Reply Format
- Every posted reply must begin with `[<agent name>]`.
- Do not prepend workflow labels (for example `Will do, TODO`).

## Procedure
1. Collect and normalize targets.
- Build a list of target threads with: `target`, `context`, `response`.
- If response text is not provided, compose a concise response from the thread context.
- Separate items into:
  - GitHub direct thread comments (have comment IDs / `discussion_r...`).
  - Reviewable-only threads (anchor IDs like `-Oko...`).

2. Post direct GitHub thread replies first.
- Use GitHub PR review comment reply API/tool for each direct comment ID.
- Post exactly one response per thread.
- Verify the new reply IDs/URLs are returned.

3. Open Reviewable, give the user time to authenticate.
- Navigate to the PR in Reviewable.
- If the user session is not active, use Reviewable sign-in flow and confirm identity before posting.

4. Reply to Reviewable-only threads one by one.
- For each discussion anchor:
  - Navigate to the anchor.
  - Find the thread reply input for that discussion.
  - Post response text with the required `[<agent name>]` prefix.
  - Avoid adding status macros or extra prefixes.
- Wait for each post to render before moving to the next thread.

5. Verification pass.
- Re-check every target thread and confirm the expected response appears.
- Confirm no target remains unreplied due to navigation/context loss.
- Confirm no accidental text prefixes were added.

## Decision Points
- If target has GitHub comment ID: use GitHub API/tool reply path.
- If target exists only in Reviewable anchor: use browser automation path.
- If Reviewable shows sign-in or disabled reply controls: authenticate first, then retry.
- Never click `resolve`, `done`, or `acknowledge` controls and never change discussion resolution state.
- If reply input transitions into a temporary composer panel:
  - Submit without modifying response text semantics.
  - Keep the required `[<agent name>]` prefix and avoid workflow labels.
- If posted text does not match intended response: correct immediately before continuing.

## Quality Criteria
- Exactly one intended response posted per target thread.
- Responses are correct for thread context and begin with `[<agent name>]`.
- No unwanted prefixes like `Will do, TODO`.
- No unresolved posting errors left undocumented.
- Final status includes: posted targets and skipped/failed targets.

## Guardrails
- Do not post broad summary comments when thread-level replies were requested.
- Do not resolve, acknowledge, dismiss, or otherwise change PR discussion status; leave resolution actions to humans.
- Do not rely on internal/private page APIs for mutation unless officially supported and permission-safe.
- Do not assume draft state implies publication; verify thread-visible posted output.
- Do not continue after repeated auth/permission failures without reporting the blocker.

## Quick Command Hints
- List PR review comments:
```bash
 gh api repos/<owner>/<repo>/pulls/<pr>/comments --paginate
```

- List PR reviews (to inspect review-body quoted discussions):
```bash
 gh api repos/<owner>/<repo>/pulls/<pr>/reviews --paginate
```

