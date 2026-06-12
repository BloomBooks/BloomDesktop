---
name: youtrack-create-issue
description: create a new YouTrack issue/bug/card in the Bloom tracker when asked (e.g. "make a youtrack bug for ...", "file a youtrack issue", "create a card on the 6.5 board")
---

Use this skill when the user asks you to **create** a new YouTrack issue (a bug, task, or card)
in the Bloom tracker at `https://issues.bloomlibrary.org/youtrack`. For *fixing* an existing
issue see `youtrack-fix`; for *querying/reporting* on issues see `bloom-youtrack-reporting`.

## 1. Authentication (never hard-code a token)

You need a YouTrack **permanent token**. Find one in this order; do NOT paste a real token into
this skill or any committed file:

1. If the `youtrack` MCP server is connected (`claude mcp list`), prefer its tools — no token
   handling needed. (Note: the built-in `/mcp` endpoint has been returning HTTP 500 on the
   self-hosted instance, so REST below is usually the working path.)
2. Otherwise look for a token already available to the agent (e.g. a stored `youtrack-mcp-token`
   memory, or a `YOUTRACK_TOKEN` environment variable).
3. If you still don't have one, ask the user to create it: in YouTrack click the avatar →
   **Profile** → **Account Security** → **New token…**, name it (e.g. `claude-code`), scope
   **YouTrack**, and paste it back (or have them set it up themselves). Tokens start with `perm-`.

Validate the token before doing real work:
`GET /api/users/me?fields=login,name` with header `Authorization: Bearer <token>` should return 200.

## 2. Choose the initial state — ASK

Unless the requester already named a state, use the **askQuestions tool** to choose where the
new issue should start. Offer exactly these options (header e.g. "Initial state"):

- **Ready For Work** — *(Recommended / default)* triaged and ready to be picked up.
- **Incoming** — not yet triaged.
- **Open** — open but not specifically queued for work.

Use the chosen value as the `State` field below.

## 3. Gather the content

- **Summary**: one concise line.
- **Description**: Markdown. For code-found bugs include where (`file:line`), the root cause, any
  compiler warning id, where it's consumed, and a suggested fix.
- **Type**: usually `Bug` (use `Task`/`Feature` if appropriate).
- **Board/version**: which release board the user wants (e.g. "6.5"). This maps to the
  **Kanban Board** version field / agile sprint — see below.

## 4. Create via the REST API

Base: `https://issues.bloomlibrary.org/youtrack`. All calls send
`Authorization: Bearer <token>`, `Accept: application/json`, and (for POSTs) `Content-Type: application/json`.

**Look IDs up dynamically — do not trust the cached values, they change every release:**

- Project: `GET /api/admin/projects?fields=id,shortName,name&query=Bloom` → "Bloom" is shortName
  `BL` (currently id `77-0`).
- Board + sprint for the target version: the board is **"Bloom Kanban"** (`GET /api/agiles?fields=id,name` →
  currently agile id `89-5`), and each release is a **sprint** on it
  (`GET /api/agiles/<agileId>/sprints?fields=id,name&$top=200` → find the one named like `Bloom 6.5`).
  The issue's version field is the **`Kanban Board`** single-version custom field.
- State / Type values: `GET /api/admin/projects/<projectId>/customFields?fields=field(name),bundle(values(name))&$top=200`.

**Order matters (workflow rule):** there is a rule *"Set Kanban Board before leaving Incoming."*
So set the board/sprint **before** setting State to anything other than `Incoming`, or the State
command returns HTTP 400.

Recommended sequence:

1. **Create** the issue (no state yet):
   `POST /api/issues?fields=id,idReadable` with body
   `{"project":{"id":"<projectId>"},"summary":"...","description":"..."}`
   → capture `idReadable` (e.g. `BL-16428`) and `id` (e.g. `83-50346`).
2. **Type**: `POST /api/commands` with `{"query":"Type Bug","issues":[{"idReadable":"<idReadable>"}]}`.
3. **Board/sprint** (do this before State): add the issue to the release sprint —
   `POST /api/agiles/<agileId>/sprints/<sprintId>/issues` with `{"id":"<numericId>"}`.
4. **State**: `POST /api/commands` with
   `{"query":"State <Chosen State>","issues":[{"idReadable":"<idReadable>"}]}`
   (e.g. `State Ready For Work`). If you set State to `Incoming`, step 3 isn't strictly required.
5. **Verify**:
   `GET /api/issues/<idReadable>?fields=idReadable,summary,customFields(name,value(name))`
   and confirm Type, Kanban Board, and State are what was requested.

Building POST bodies with embedded code/quotes is easier from a JSON file (`curl -d @file.json`)
than inline.

## 5. Report back

Give the user the readable id and a clickable link:
`https://issues.bloomlibrary.org/youtrack/issue/<idReadable>`, and note the final Type / board /
state. The reporter will be whoever owns the token. Do not push code or change anything else
unless separately asked.
