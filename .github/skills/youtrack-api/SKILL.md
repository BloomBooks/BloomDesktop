---
name: youtrack-api
description: How to talk to the Bloom YouTrack tracker over REST — authentication, base URL, and common operations (read an issue, find an issue id, list/post comments, attachments). The shared low-level building block that the other youtrack-* skills and any PR/review workflow rely on.
---

# Using the Bloom YouTrack REST API

This is the **shared, low-level** skill for interacting with the Bloom YouTrack tracker
(`https://issues.bloomlibrary.org/youtrack`). Use it whenever a task needs to authenticate to
YouTrack or perform a basic operation (read an issue, comment, look something up).

It is referenced by the higher-level YouTrack skills — use those for their specific jobs:

- **`youtrack-fix`** — fix an existing issue (`BL-xxxxx`): branch, plan, commit.
- **`youtrack-create-issue`** — create a new issue/bug/card.
- **`bloom-youtrack-reporting`** — query/report across issues.

If you only need to *read* one issue, the REST calls below are all you need.

## 1. Authentication (never hard-code a token in a committed file)

You need a YouTrack **permanent token** (they start with `perm-`). Find one in this order:

1. **`$YOUTRACK` environment variable** — on this machine the token is stored here. Check with
   `echo "${YOUTRACK:+set}"`. This is the normal path; use `-H "Authorization: Bearer $YOUTRACK"`.
   (Some older skills referred to `$YOUTRACK_TOKEN`; the variable that is actually set is
   `$YOUTRACK`.)
2. **A stored token memory** (e.g. a `youtrack-api-token` memory) if the env var is not set.
3. If none of the above, ask the user to create one: in YouTrack click the avatar →
   **Profile** → **Account Security** → **New token…**, scope **YouTrack**. Do **not** paste a
   real token into this skill or any committed file.

Validate before doing real work:
```bash
curl -s -H "Authorization: Bearer $YOUTRACK" \
  "https://issues.bloomlibrary.org/youtrack/api/users/me?fields=login,name"
```
A 200 with your login confirms the token works.

## 2. Conventions

- **Base URL:** `https://issues.bloomlibrary.org/youtrack/api`
- **Headers:** every call sends `Authorization: Bearer $YOUTRACK` and `Accept: application/json`;
  POSTs also send `Content-Type: application/json`.
- **Fields:** YouTrack returns nothing unless you ask — always pass a `fields=` query param
  listing what you want (e.g. `fields=idReadable,summary,description`).
- **Web URL is a SPA:** `https://issues.bloomlibrary.org/youtrack/issue/BL-xxxxx` is a
  JavaScript app and returns blank to an anonymous WebFetch. Always use the REST API for data.

## 3. Common operations

### Read an issue
```bash
curl -s -H "Authorization: Bearer $YOUTRACK" -H "Accept: application/json" \
  "https://issues.bloomlibrary.org/youtrack/api/issues/BL-16467?fields=idReadable,summary,description,customFields(name,value(name))"
```

### Find the issue id for the current work
Look for a `BL-XXXXX` token, in this order:
1. the branch name (`git rev-parse --abbrev-ref HEAD`),
2. the PR title,
3. recent commit messages (`git log --oneline -20`).

### List an issue's comments
```bash
curl -s -H "Authorization: Bearer $YOUTRACK" \
  "https://issues.bloomlibrary.org/youtrack/api/issues/<issue-id>/comments?fields=text"
```

### Post a comment
```bash
curl -s -X POST "https://issues.bloomlibrary.org/youtrack/api/issues/<issue-id>/comments" \
  -H "Authorization: Bearer $YOUTRACK" \
  -H "Content-Type: application/json" \
  -d '{"text": "your comment text"}'
```
Before posting, list existing comments and check you are not creating a duplicate (e.g. when
posting a PR link, `grep -i "github.com.*pull"` the existing comment text first). For bodies
with embedded quotes/code, write the JSON to a file and use `curl -d @file.json`.

### List an issue's attachments
```bash
curl -s -H "Authorization: Bearer $YOUTRACK" \
  "https://issues.bloomlibrary.org/youtrack/api/issues/<issue-id>/attachments?fields=name,url,size"
```

## 4. If the token is unavailable

Tell the user plainly: e.g. "I need a YouTrack API token (`$YOUTRACK`) to do this. Set it in
your environment, or do the step manually: …". For low-stakes steps (like posting a PR-link
comment) note that it's skipped and continue; for read operations you cannot proceed without it.
