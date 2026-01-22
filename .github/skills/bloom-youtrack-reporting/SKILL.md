---
name: bloom-youtrack-reporting
description: prompts about youtrack issues/reports
---

## Scope
Use this skill for answering prompts about issues, using the youtrack mcp. If the mcp tools you need are not enabled, stop and ask the user to enable them.

## Query rules
- Use unquoted project names
  - Example: `project: Bloom-Harvester` (not `project: "Bloom-Harvester"`).
- “Created by” corresponds to `reporter:` (e.g., `reporter: auto_report_creator`).
- Last 12 months: `created: {minus 12M} .. *`.
- Date range since December 2025: `created: 2025-12-01 .. *`.
- If project name/key is uncertain, list available projects first.
- If user requests "by users", they mean  `reporter: auto_report_creator`. When staff members create issues, they use their own accounts.

## Getting all the results
- DO NOT mention "pages" of the data you analyzed. They are an implementation detail.
- Don't stop at the first page. Fetch all pages of results when responding to queries that may return multiple pages. However don't ever mention pages to the user. They don't care about pages. For example if they ask you for a count of something, give them the total count, not count by page.

- Exception: After 200 results, pause and ask the user if they want you to continue because there are 10 years of data and they probably don't want to wade through all that.

## Regressions
- If we know that the bug was introduced in a version, then the issue title (summary) will normally begin with something like [5.1 Regression] or [Regression 5.2]. But sometimes people forget to do that. If you are asked to do counts about these show sub-counts for how you decided that something is a regression. For example, "Of the 37 issues created in the last 12 months that mention a regression, 25 had titles beginning with [x.y Regression] or [Regression x.y], while 12 mentioned 'regression' or said "this used to work in <version>" in the description.
