---
name: reviewable-thread-replies
description: 'Reply to Reviewable PR discussion threads using the reviewable CLI. Use whenever the user asks you to respond to review comments.'
argument-hint: 'Repo/PR reference (e.g. BloomBooks/BloomDesktop#7949) and optionally specific discussion keys to target'
---
# Reviewable Comments

## When To Use

- When you look for PR comments and you notice that some comments have come in from reviewable. These will be in big unusable blocks directly on Github so you need to go to reviewable and read them individually. 
- When you need to respond to comments that were created on reviewable. It is not okay to respond to them directly on GitHub. 

Use the `reviewable` CLI to read and reply to PR review discussions. 

## Environment Setup

Two env vars must be visible to the shell running `reviewable`:

- `REVIEWABLE_URL` — e.g. `https://reviewable.io`
- `REVIEWABLE_API_TOKEN` — looks like `rvbl_...` (from the user's personal Reviewable settings page)

If reviewable cli is not installed, see: [https://docs.reviewable.io/agents#using-the-cli](https://docs.reviewable.io/agents#using-the-cli)

If the user thinks that the environmental variables are there but you don't see them, ask them to restart whatever application you are running in so that the terminal environment gets the latest environment variables.

Every command requires exactly one of:

- `--pr=owner/repo/123`  (preferred — use the GitHub PR number)
- `--branch=owner/repo/branch-name`

Find the PR number with: `gh pr view --json number`

## Full Workflow

### 1. Check state

```bash
reviewable review state --pr=BloomBooks/BloomDesktop/7949
```

Check `discussions.unreplied` and `drafts.comments` to understand what needs attention.

### 2. List all discussions

```bash
reviewable review discussions list --pr=BloomBooks/BloomDesktop/7949
```

Returns a JSON array of discussion keys. To limit to ones needing your attention:

```bash
reviewable review discussions list --pr=BloomBooks/BloomDesktop/7949 --query="+needs:me"
```

### 3. View each discussion

```bash
reviewable review discussions view --pr=BloomBooks/BloomDesktop/7949 --key="-OvkCqVS01iSEUE9u24g"
```

The output contains:

- `comments`: the thread history (read these to understand what was said)
- `location.file.path` and `location.line`: which code line the discussion is on
- `location.revision.key`: which revision (r1, r2, r3...) the comment was made against
- `status.replied` / `status.resolved`: current state
- `participants[].disposition`: `blocking`, `satisfied`, `discussing`, etc.

**Discussion key types:**

- `-O...` keys: Reviewable-native inline comments — NOT mirrored to GitHub
- `gh-...` keys: GitHub PR review comments mirrored into Reviewable
- `-top`: the PR-level discussion (aggregates all non-inline comments — bots, general PR comments)

### 4. Understand what was done

For `gh-` discussions, the thread often already contains replies from the developer visible in the `comments` array with `"provenance": "github"`. Check `status.resolved` — if `true`, just acknowledge.

For `-O` (Reviewable-native) discussions, there are usually no GitHub replies — the work was done in code changes. Read the current code at the discussion's file/line to understand what changed, then craft a reply that explains it.

For `-top`, look at issue-level PR comments via:

```bash
gh api repos/BloomBooks/BloomDesktop/issues/7949/comments --paginate
```

### 5. Reply to each discussion

```bash
echo '{"markdownBody": "Reply text here.", "disposition": "satisfied"}' \
  | reviewable review discussions reply \
    --pr=BloomBooks/BloomDesktop/7949 \
    --key="-OvkCqVS01iSEUE9u24g"
```

**Required fields:**

- `markdownBody`: non-empty Markdown reply text

**Optional fields:**

- `disposition`: `satisfied` | `discussing` | `blocking` | `working`
  - Use `satisfied` when the concern is fully addressed
  - Use `discussing` when partially addressed or when flagging a follow-up

This creates a **draft** — not published yet.

### 6. Acknowledge instead of replying (when no reply is needed)

For bot-only discussions or discussions that are clearly already resolved:

```bash
reviewable review discussions acknowledge \
  --pr=BloomBooks/BloomDesktop/7949 \
  --key="-top" \
  --disposition=satisfied
```

### 7. Publish all drafts

```bash
reviewable review publish --pr=BloomBooks/BloomDesktop/7949
```

Publishes all pending drafts at once. Verify with `reviewable review state` afterward (`drafts.comments` should be 0).

To cancel a queued on-push publication:

```bash
reviewable review publish cancel --pr=BloomBooks/BloomDesktop/7949
```

## Reply authorship rule

All replies post under the user's account. Per project policy, **prefix every reply body with `[Claude [[ORCA_RAW_HTML_INLINE:%3Cmodel%3E]]]`** (e.g. `[Claude Sonnet 4.6]`) so it is clear the text came from the AI, not the user directly.

## Parallel fetching

When viewing many discussions, fetch them all in parallel — issue multiple Bash tool calls in one message. Each call must re-export the env vars.

## What "resolved" means in Reviewable

After posting replies, discussions show `"resolved": false` until the original reviewer changes their disposition to `satisfied`. That is expected — our job is to reply with `satisfied` disposition; the reviewer decides whether to close the thread.

## Handling the three discussion types together

When addressing a PR with many open discussions:

1. **Bot discussions (`gh-...`)**: often already have replies from the developer. Check `status.resolved` — if `true`, just acknowledge. If `false` and `status.replied: true`, verify the reply is substantive before skipping.
2. **Reviewer discussions (`-O...`)**: Reviewable-native, no GitHub mirror. Read the current code at the discussion location, understand what was done, and write a concrete reply.
3. **Top-level (`-top`)**: summarize how non-inline review feedback (bots, general comments) was handled. A single acknowledgement or summary reply is usually sufficient.

## What to say

- For concerns that were addressed in code: describe the specific change made (function name, file, what it does differently now).
- For concerns not acted on: give a concrete technical explanation of why not, not just "left as-is."
- Never be dismissive. Every reply should leave the reviewer informed.
- Use `discussing` disposition for partial fixes or acknowledged follow-up items; use `satisfied` only when fully addressed.

## Efficiency tips

- Fetch all discussion views in parallel (one bash call per key, all in the same message).
- Sort the work: bot discussions first (quick acks), then reviewer discussions (require code reading), then `-top` last.
- Use `--query="+needs:me"` on `list` to skip already-handled discussions when bot threads are resolved.
- The `status.replied` field tells you if you've already drafted a reply this session; `status.resolved` tells you if the original poster is satisfied.
- You can process 30+ discussions in one session by batching view calls 6-8 at a time.

