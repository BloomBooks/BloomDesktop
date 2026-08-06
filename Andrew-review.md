# GHA Release Workflow — Andrew's review list

Everything outstanding on the `GhaReleaseInstaller` branch that needs your eyes, a decision,
or verification. (Working checklist for this branch — delete before merging to master.)

## Current state (2026-08-05) — read this first

The TeamCity→GHA migration is in three parts; two are already on master and working:

- **Nightly build+test** (`.github/workflows/nightly.yml`) — ON MASTER, running. Full
  build + both test suites (vitest via `test:ci`, plus a visual-regression suite, plus C#
  via `msbuild /t:TestOnly`), health-check only (no publish). Scheduled `0 4 * * *` UTC;
  GitHub routinely delays scheduled runs, so it actually fires ~06:19 UTC — that's normal,
  not a changed cron. Scheduled runs are green.
- **Harvester artifacts** (`.github/workflows/harvester-artifacts.yml`) — ON MASTER, ported
  to pnpm (commit 53001e3d0d), triggers on `harvester-development` / `harvester-production`.
  The whole harvester chain (BloomDesktop bundle → rolling release → dispatch →
  bloom-harvester build → deploy) is DONE and working for dev and prod. See the Harvester
  section below.
- **Release installer** (`.github/workflows/release-installer.yml`) — the Velopack
  channel-publishing workflow — is the ONLY piece NOT yet on master. It lives on this branch
  (`GhaReleaseInstaller`), now a single clean commit rebuilt on current master. It has never
  had a green end-to-end run yet; the open decisions and verification items below are all
  about this workflow.

**This branch is local-only and was rebuilt (history collapsed to one commit), so publishing
it needs `git push --force-with-lease`.** A new worktree in this same repo can check out the
local branch directly; a fresh clone would need it pushed first. When the branch is on the
new worktree, this file is the complete handoff — nothing important lives only in the old
chat.

## Decisions you tentatively made (ratify or change)

- [ ] **Stage lives in `Directory.Build.props`** (`<BloomChannelStage>`). You said "go with your
      recommendation for now, but I'm unsure." Alternatives were a separate small file
      (`build/channel-stage.txt`) or hardcoding in each branch's workflow yaml. Easy to move
      until other branches depend on it.
- [ ] **Stage → channel mapping** (in the `plan` job):
      push = `Alpha` / `BetaInternal` / `ReleaseInternal`; dispatch = `Alpha` / `Beta` / stable (`""`).
      Stable is *only* reachable as the Release-stage default; no input can select it.
- [ ] **`channel_override` allow-list**: Alpha→{Alpha}, Beta→{Beta, BetaInternal},
      Release→{Beta, ReleaseInternal}. The Release→Beta entry is your manual "beta users never
      behind release" catch-up. Confirm these are the right sets.
- [ ] **Continuous builds may publish unsigned** if signing fails (tests still must pass).
      Manual publishes hard-require valid signatures. Matches what you said; confirm it's
      really OK for `Alpha`, which is more public than the Internal channels.
- [ ] **No cancel-in-progress**: every commit to a continuous branch builds and publishes, in
      order (serialized per channel). You didn't pick "cancel superseded runs" — deliberate?
      A busy day on master = a long Alpha queue at ~1 hr/build.
- [ ] **Dry-run version reuse**: repeated dry runs of a channel produce the *same* version
      number (feed doesn't advance). Harmless for artifacts; just be aware.
- [ ] **Version counters continue across major.minor within a channel** (first 6.5 Beta =
      last 6.4 Beta counter + 1). Monotonic and automatic, but e.g. "6.5.9123" no longer hints
      at how many 6.5 builds there were. OK?
- [x] **Integration and Nightly tests: excluded from release-installer, run in nightly only**
      — DONE (2026-08, per your call). The C# step passes
      `/p:excludedCategories=Integration%3BNightly%3BSkipOnTeamCity` — the same three the
      harvester workflow leaves out, and the same escaped-semicolon idiom: a raw `;` cannot
      survive pwsh (statement separator) and MSBuild would split an intact one into separate
      `/p:` assignments (MSB1006), so `%3B` is the form that works from any shell, and
      `RunNUnit` unescapes it back into a real separator. This deliberately follows PR #8166
      rather than the CI-detection scheme this branch briefly carried — that one auto-added
      `SkipOnTeamCity` on any CI system so each caller needed only one extra category, which
      cannot express three. One mechanism repo-wide beats two.
      The nightly workflow remains the only run that exercises `[Category("Integration")]`
      `WebLibraryIntegration` (real S3 / parse.com) and the `Nightly` category.
      Follow-up if you want it: if nightly's Integration tests ever go red on missing
      credentials, wire the `BloomBooks*`/`BloomHarvester*` env-var family into the nightly
      job. (Nightly is green today, so they appear to run fine as-is.)

## Setup needed before publish runs can work

- [ ] **`GH_TOKEN_HARVESTER_ACTIONS` secret** (this repo): fine-grained PAT (or GitHub App
      token) with Actions: write on BloomBooks/bloom-harvester, so a published harvester
      bundle auto-triggers the downstream bloom-harvester build (dev→master, prod→release),
      restoring the TC dependency chain. If missing, the trigger step fails the run.

- [ ] **`TRUSTED_SIGNING_CREDENTIALS` secret** on this repo — same base64 JSON the
      bloompub-viewer repo uses (if it's an org secret, may just need this repo added to its
      access list).
- [ ] **`AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` secrets** with write access to the
      `bloomlibrary.org` bucket.
- [ ] **Push the branch** (`git push --force-with-lease`) when ready — each push to
      `GhaReleaseInstaller` runs the full pipeline as a DRY run (the plan job forbids
      publishing from any non-master/VersionX.Y branch, so the temporary push trigger can
      never publish). There is no meaningful prior run to look at; the release-installer
      workflow has not had a green end-to-end run since being rebuilt on current master.

## Things the first real runs must verify (my educated bets)

- [ ] **`vpk download http` exists in our fork** (BloomBooks.Velopack.Cli 0.0.1350-l10n-0020)
      and correctly pulls `releases.win.json` + latest full nupkg from the public feed URL
      `https://s3.amazonaws.com/bloomlibrary.org/deltas<channel>`.
- [ ] **A delta nupkg is actually produced** and the computed version continues the channel's
      TeamCity sequence (watch the "Compute BUILD_NUMBER" step log).
- [ ] **The `sign` shim works**: `Invoke-TrustedSigning` parameter names/auth match what
      `azure/trusted-signing-action@v0.5.1` does (I mirrored them; first signed run confirms).
      Requires the secret to be present.
- [ ] **`S3BuildPublisher` finds the `BloomLibrary` profile** in `~/.aws/credentials` (the
      workflow writes it there; the SIL.BuildTasks.AWS task uses the AWS SDK credential chain,
      which *should* read that file). Only exercised on a publish run.
- [ ] **C# test categories**: currently excludes only `SkipOnTeamCity`. See the Integration
      decision above — if you add `,Integration`, do it here too. Also confirm the TC configs
      don't exclude still more categories that would otherwise red-fail in CI.
- [x] **`getDependencies-windows.sh`** — DE-RISKED: runs successfully in the nightly workflow
      on master every day, so it works on a windows-latest runner.
- [x] **Vitest junit output + report** — DE-RISKED and FIXED: nightly proved the pattern and
      surfaced a real bug. The junit reporter must be invoked as
      `pnpm run test:ci --reporter=default --reporter=junit --outputFile=...` — NO `--`
      before the flags (pnpm forwards it literally and vitest ignores everything after),
      and `--outputFile=` NOT `--outputFile.junit=`. release-installer.yml was corrected to
      match, and its publish step now uses the unified `files:` input with
      `comment_mode: off` (avoids a 403 posting a PR comment on push/dispatch) and
      `action_fail_on_inconclusive: true`. First release-installer run should still confirm
      the report renders both suites.

## Before merging to master

- [ ] **Remove the temporary `push: GhaReleaseInstaller` trigger** (marked in the yaml).
- [ ] Decide whether the commented-out `master`/`Version*` push triggers merge as-is
      (commented) and get enabled at cutover, or stay out until then.
- [ ] Review the header comment in `release-installer.yml` — it's the design doc future-you
      will read at promotion time.

## Cutover plan (per channel, when you're ready to leave TeamCity)

1. Disable the TeamCity config for that channel FIRST — both systems publishing one channel
   would race the per-channel version counter and clobber `releases.win.json`.
2. Uncomment the corresponding push trigger (`master` for Alpha; `Version*` for Internal).
3. For release branches (`Version6.4`, `Version6.3`): backport the workflow + `build/gha/` +
   the `Directory.Build.props` stage property (with the right stage value), and **pin the
   runner image** (e.g. `windows-2025` instead of `windows-latest`) so old branches don't
   break when GitHub updates the image.

## Harvester branches

**DONE AND WORKING (2026-07)** — the whole chain runs on GHA for both dev and prod:
BloomDesktop harvester-development/-production → bundle to rolling release → dispatch
(GH_TOKEN_HARVESTER_ACTIONS) → bloom-harvester master/release → deployable harvester.zip to
master-latest/release-latest → updateDeployment.ps1 on the servers. The related TeamCity
configs are retirable.

⚠️ **yarn↔pnpm seam — the one thing to watch (OPEN).** `harvester-artifacts.yml` originated
on the Version6.4/harvester lineage as an all-**yarn** workflow. It has since been ported
onto **master as pnpm** (commit 53001e3d0d). A push-triggered workflow runs *the copy of the
file on the branch that triggered it*, and it triggers on `harvester-development` /
`harvester-production`. So: when master (pnpm) is merged into `harvester-development`, the
pnpm workflow lands there and is correct ONLY IF that branch is itself pnpm by then. If the
harvester branch is still yarn (6.4-era), the merged pnpm workflow will break (pnpm steps on
a yarn tree). Before the next master→harvester-development merge, confirm the harvester
branch's front-end toolchain matches the workflow it will receive. (The old yarn variant is
recoverable from this branch's history at commit d02c266317 if a yarn copy is ever needed on
a still-yarn branch.)

**Merging master into harvester-development re: THIS branch's files is safe:**
`release-installer.yml` would arrive but has no trigger matching harvester branches and its
plan job refuses non-master/VersionX.Y refs; the `BloomChannelStage` in Directory.Build.props
is inert there. (This concerns only the release-installer branch's additions, not the
pnpm-seam issue above, which is about the harvester workflow already on master.)

Reminder for the *bloom-harvester* repo: merging its master into its release branch silently
adopts the dev bundle URL in getDependencies.ps1 — re-fix to harvester-production-latest
after every such merge (the line is annotated).

- [x] **Decided (2026-07): rolling GitHub Release.** The workflow builds both harvester
      branches per push (yarn installs, yarn build-prod, yarn test:ci, msbuild Build,
      TestOnly — mirroring the TC config verbatim) and, only when everything is green,
      clobbers `bloom-harvester-deps.zip` on the per-branch prerelease tag
      `harvester-<branch>-latest`. Latest-successful-only, per your answer. Zip layout
      matches the TC downloadAll artifact (bin/Release/x64, output/browser, DistFiles) so
      the consumer only changes its URL.
- [x] **Stage-model hazard**: RESOLVED — the release workflow's plan job only allows master
      and VersionX.Y branches at all, so a harvester branch can never publish a channel with
      a stage inherited from Version6.4 merges.
- [x] **Consumer patch**: DONE — branch `BloomFromGhaRelease` in the sibling repo
      (c:\dev\bloom-harvester): URL change (812c8f1) plus a Build-and-Test GHA workflow
      (f495522) replacing Bloom_HarvesterMasterContinuous (getDependencies from the rolling
      release → nuget restore → msbuild Release → dotnet test, trx test report).
      Squash-merged to bloom-harvester master as d128d27 after a green dry run. A further
      commit (2c7d5b0) migrated deployment too: CI publishes the deployable build to
      per-branch rolling releases (master-latest/release-latest) and updateDeployment.ps1
      downloads from them. The release branch got the pipeline via merge-of-master plus the
      production-bundle URL fix (8de652c). All confirmed working for dev and prod.
- [x] **Production side**: CLARIFIED — `updateDeployment.ps1` downloads the *harvester's own*
      TC builds (Bloom_HarvesterMasterContinuous / Bloom_HarvesterReleaseContinuous), not
      BloomDesktop; migrating those is a separate bloom-harvester-repo concern. The only
      BloomDesktop dependency URL is the one in getDependencies.ps1. Check whether
      bloom-harvester's release branch has its own copy pointing at a production Bloom
      config; if so it should get `.../harvester-production-latest/...`.
- [x] **yarn-vs-pnpm conflict**: RESOLVED per your direction — the workflow now lives on
      `GhaHarvesterArtifacts` (cut from harvester-development) with all-yarn steps matching
      the TC config verbatim (`yarn --network-timeout 200000`, `yarn build-prod`,
      `yarn test:ci` + junit reporter flags, node cache keyed on yarn.lock, no pnpm setup).
- [x] **Restore step**: RESOLVED — the harvester workflow now runs `msbuild -t:Restore
      Bloom.sln` exactly like TC (restore → Build → TestOnly), and drops the explicit
      RestoreBuildTasks step: entering Bloom.proj through the Build target lets its
      RestartBuild self-heal install the SIL.BuildTasks dlls on a clean runner (still a
      single compile). The release-installer workflow keeps its explicit RestoreBuildTasks
      step because it enters through SignIfPossible, where VersionNumbers needs those task
      dlls at parse time, before Build's self-heal can run.
- [x] **Bundle layout**: VERIFIED against the dry run's artifact — top level is exactly
      bin/DistFiles/output; Bloom.exe, gm/, runtimes/, output/browser, DistFiles/localization
      and fonts all present where getDependencies.ps1 expects them; 92 MB compressed /
      211 MB uncompressed, far under the 2 GB release-asset limit.
- [x] **Cutover** — DONE for both dev and prod; the GHA chain is live and the harvester TC
      configs (Bloom_BloomDesktopHarvesterBranchContinuous, Bloom_HarvesterMaster/Release-
      Continuous) are retirable whenever you want to turn them off.

## Larger things to think about (not blocking)

- [ ] **Cert identity change**: Trusted Signing's certificate subject differs from the old
      cert. Velopack doesn't pin publisher, so updates should work, but **SmartScreen
      reputation restarts** on the new identity — expect warnings until reputation builds.
      Worth a deliberate first-channel choice (Alpha/Internal first).
- [ ] **Trusted Signing quota/cost**: continuous builds sign 3 files per commit across up to
      3 branches. Check SIL's Trusted Signing plan limits.
- [ ] **`UpgradeTable{channel}.txt` on S3** remains a manual promotion step outside this
      workflow (who owns it?).
- [ ] **`getDependencies-windows.sh` pulls `latest.lastSuccessful`** from build.palaso.org —
      unpinned inputs in release builds. Fine to start; consider pinning for stable-channel
      reproducibility.
- [ ] **Old runs' artifacts are public** (public repo): installers built from unmerged
      branches are downloadable by anyone with the run URL. Retention is 7 days.
- [ ] **Timeout is 120 min** for the build job; adjust once real run times are known.
