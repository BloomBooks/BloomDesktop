---
name: youtrack-create-issue
description: create a new YouTrack issue/bug/card in the Bloom tracker when asked (e.g. "make a youtrack bug for ...", "file a youtrack issue", "create a card on the 6.5 board")
---

Use this skill when the user asks you to **create** a new YouTrack issue (a bug, task, or card)
in the Bloom tracker at `https://issues.bloomlibrary.org/youtrack`. For *fixing* an existing
issue see `youtrack-fix`; for *querying/reporting* on issues see `bloom-youtrack-reporting`.

For authentication, the base URL, and request conventions (headers, `fields=`), use the
**`youtrack-api`** skill — this skill only covers what's specific to *creating* an issue.

## 1. Choose the initial state — ASK

Unless the requester already named a state, use the **askQuestions tool** to choose where the
new issue should start. Offer exactly these options (header e.g. "Initial state"):

- **Ready For Work** — *(Recommended / default)* triaged and ready to be picked up.
- **Incoming** — not yet triaged.
- **Open** — open but not specifically queued for work.

Use the chosen value as the `State` field below.

## 2. Gather the content

- **Summary**: one concise line.
- **Description**: Markdown. For code-found bugs include where (`file:line`), the root cause, any
  compiler warning id, where it's consumed, and a suggested fix.
- **Type**: usually `Bug` (use `Task`/`Feature` if appropriate).
- **Board/version**: which release board the user wants (e.g. "6.5"). This maps to the
  **Kanban Board** version field / agile sprint — see below.

## 3. Create via the REST API

Calls use the base URL, headers, and token from the **`youtrack-api`** skill.

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

## 4. Report back

Give the user the readable id and a clickable link:
`https://issues.bloomlibrary.org/youtrack/issue/<idReadable>`, and note the final Type / board /
state. The reporter will be whoever owns the token. Do not push code or change anything else
unless separately asked.
