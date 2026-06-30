---
name: bloom-youtrack-reporting
description: prompts about youtrack issues/reports
---

## Scope
Use this skill for answering prompts that **query or report** across YouTrack issues. For
*fixing* an existing issue see `youtrack-fix`; for *creating* one see `youtrack-create-issue`.

Run searches against the YouTrack REST API — see the **`youtrack-api`** skill for auth, the base
URL, and request conventions. Issues are fetched from `GET /api/issues` with the search text in
the `query=` parameter and the fields you need in `fields=`, e.g.:
```bash
curl -s -H "Authorization: Bearer $YOUTRACK" \
  "https://issues.bloomlibrary.org/youtrack/api/issues?fields=idReadable,summary,created&query=project:%20Bloom%20created:%20%7Bminus%2012M%7D%20..%20*&$top=200"
```

## Query rules
The rules below describe the YouTrack **query language** that goes in the `query=` parameter.
- Use unquoted project names
  - Example: `project: Bloom-Harvester` (not `project: "Bloom-Harvester"`).
- “Created by” corresponds to `reporter:` (e.g., `reporter: auto_report_creator`).
- Last 12 months: `created: {minus 12M} .. *`.
- Date range since December 2025: `created: 2025-12-01 .. *`.
- If project name/key is uncertain, list available projects first.
- If user requests "by users", they mean  `reporter: auto_report_creator`. When staff members create issues, they use their own accounts.

## Getting all the results
The REST API returns at most `$top` issues per call; page through with `$skip` (e.g.
`&$top=200&$skip=200` for the next batch) until a call returns fewer than `$top`.

- DO NOT mention "pages" or `$top`/`$skip` of the data you analyzed. They are an implementation detail.
- Don't stop at the first batch. Fetch all results when responding to queries that may return more than one batch. However don't ever mention paging to the user. They don't care about it. For example if they ask you for a count of something, give them the total count, not a count per batch.

- Exception: After 200 results, pause and ask the user if they want you to continue because there are 10 years of data and they probably don't want to wade through all that.

## Regressions
- If we know that the bug was introduced in a version, then the issue title (summary) will normally begin with something like [5.1 Regression] or [Regression 5.2]. But sometimes people forget to do that. If you are asked to do counts about these show sub-counts for how you decided that something is a regression. For example, "Of the 37 issues created in the last 12 months that mention a regression, 25 had titles beginning with [x.y Regression] or [Regression x.y], while 12 mentioned 'regression' or said "this used to work in <version>" in the description.
