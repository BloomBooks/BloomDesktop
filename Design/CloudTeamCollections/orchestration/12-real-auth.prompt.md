# Agent prompt — task 12: real auth provider, Option A seams (resume-aware)

You are implementing task 12 in the MAIN working tree at c:\github\BloomDesktop (you build
and unit-test C#; you must NOT launch Bloom.exe or run E2E tests — the machine is in
interactive use by the developer).

**Resume check (do this FIRST):** `git status` must be clean (stop and report if not). If
branch `task/12-real-auth` exists, check it out and continue from the `## Progress log` at
the bottom of `Design/CloudTeamCollections/tasks/12-real-auth.md`. Otherwise
`git checkout -b task/12-real-auth cloud-collections`.

**Durability protocol (mandatory):** commit after EVERY completed step, messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"; tick checkboxes + progress log
(`date · done · exact next action`) in the same commit. Interruptions are likely.

**Read first:** `Design/CloudTeamCollections/tasks/12-real-auth.md` (authoritative steps);
`Design/CloudTeamCollections/GOING-LIVE.md` Phase 3 (the boundary between your work and the
deferred live wiring); the existing dev provider + seam in
`src/BloomExe/TeamCollection/Cloud/` (CloudAuth.cs, CloudEnvironment.cs) and its tests in
`src/BloomTests/TeamCollection/Cloud/`; how Bloom currently hosts BloomLibrary sign-in
(search WebLibraryIntegration and the sharing sign-in flow) BEFORE designing the token
receipt endpoint; `supabase/migrations/` for tc.jwt_email_verified()'s current shape;
`.github/skills/xlf-strings/SKILL.md` if you add any user-visible strings (en-only;
NEVER a double hyphen inside a <note> — it crashes every Bloom launch).

**Test rules:** the mandatory C# filter is
`"(FullyQualifiedName~Cloud|FullyQualifiedName~TeamCollection|FullyQualifiedName~SharingApi)&FullyQualifiedName!~LiveTests"`;
never `--no-build`; never `yarn build`; vitest (if any front-end) single-run with
`--pool=threads`. pgTAP (if you add migrations): `supabase test db` with the stack up — it
already is; `supabase db reset` is allowed. All HTTP in unit tests is mocked; no live
Google/Firebase calls anywhere.

**Contract discipline:** document the token-receipt request shape in CONTRACTS.md (new
"Auth (Option A)" section) — the BloomLibrary2 editor.ts change will be written against
your text verbatim, so be precise about route, method, body fields, and the reply.

**Final report (raw data):** branch + shas; per-step status; test commands + verbatim
counts; the CONTRACTS.md section text you added; anything you discovered about the existing
BloomLibrary sign-in flow that affects GOING-LIVE Phase 3.2; exact next action if unfinished.
