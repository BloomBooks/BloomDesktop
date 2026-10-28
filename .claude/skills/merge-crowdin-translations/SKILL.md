---
name: merge-crowdin-translations
description: Bring the translations Crowdin has accumulated on the l10n_master pull request into master, then cherry-pick them onto the release branch or two the developer chooses, auditing what each branch gains and loses. Use when asked to "merge the Crowdin translations", "bring in translations from Crowdin", "do a Crowdin round", to deal with the "New Crowdin updates" PR, or when a translation is found to be wrong.
---

# Merge Crowdin translations

Crowdin keeps one pull request open from `l10n_master` into master and adds a commit to it every
ten minutes while translators work, so it holds hundreds or thousands of "New translations"
commits. Every so often we bring in what translators have done since the last time: squash
those commits into one, commit it directly on master, and cherry-pick it directly onto release
branches. No pull requests are involved; the developer approves each push instead. The agent
does all of it except pausing and resuming Crowdin's sync, which the developer does in the
Crowdin web page.

How Crowdin syncs, what edits to xliff files do to translations, and why translations travel
master-first are in `DistFiles/localization/README.md`. Read it if anything here looks odd.

Doing this by hand: every step below is a plain git, gh or python command; run them yourself in
order. Where the agent asks, you decide.

## Asking the developer

**Every question goes to the developer through the `decider` skill**, never as a terminal
question or a list in chat. Prepare everything that can be prepared first (steps 2–5), so that
one form carries all the decisions at once (step 6). Ask again with a new form only when
something forces it: a conflict in step 3, a problem that stops the run, or a push rejected
because a branch moved.

Pausing and resuming the sync (steps 1 and 9) are not decisions; ask for those in a plain
message.

Nothing is pushed without the developer's answer on the form. Each commit is made locally on
top of its target branch, and pushing it is the merge.

## Where to work

Work in a clean worktree where you can switch branches freely, not one holding someone's
work. If you need one, make it from master (`git worktree add <scratch>/crowdin-work
origin/master`) and remove it at the end. On Windows, if `git worktree remove` fails with
"Permission denied", delete the folder and run `git worktree prune`. Run this skill's script
from wherever the skill itself is.

## 1. The developer pauses the sync

Ask the developer to open the GitHub integration of the SIL-Bloom project on Crowdin
(https://crowdin.com/project/sil-bloom/integrations/system/github) and click **Pause Sync**, and wait
until they say it's done. Without the pause, Crowdin keeps adding commits to `l10n_master` while
you work, and those commits would be lost when step 7 deletes the branch.

## 2. Check the branch holds only translations

```bash
git fetch origin
MB=$(git merge-base origin/master origin/l10n_master)
git log --format='%an | %s' $MB..origin/l10n_master | grep -v '| New translations ' | sort | uniq -c
git diff --name-only $MB origin/l10n_master | grep -v -E '^DistFiles/localization/'
git diff --name-only $MB origin/l10n_master | grep -E '^DistFiles/localization/(en/|[^/]+/ReadMe-en\.xlf)'
```

All three must print nothing. Anything else (a Crowdin "Update Crowdin configuration file"
commit, a change to `crowdin.yml`, an English file) is not a translation: stop and put it to the
developer on a decider form.

## 3. Make one commit on top of master

`git merge --squash` stages everything `l10n_master` changed since it left master, as one
change on top of current master. Keep Crowdin's bot as the author, as earlier merges of these
translations did. Don't put `[ci skip]` in the message: these commits must get their CI builds.

Shell variables don't survive from one tool call to the next. Write down `DATE` and `SEEN` when
you set them here, and `SQUASH` in step 7, and put the literal values into the later commands.

```bash
DATE=$(date +%Y%m%d)
SEEN=$(git rev-parse origin/l10n_master)         # step 7 checks nothing was added after this
git switch -c crowdin-master-$DATE origin/master
git merge --squash origin/l10n_master
git commit --author="Crowdin Bot set up for sil lsdev <30415668+CrowdinBot@users.noreply.github.com>" \
  -m "Merge all the new translations from Crowdin ($DATE)"
```

A conflict means someone edited a translated file on master. Put the conflicting files to the
developer on a decider form before resolving; normally Crowdin's side wins.

## 4. Check the xliff files

L10NSharp's CheckOrFixXliff finds invalid XML and broken format markers (`{0}`, `%1`) that
would crash Bloom. Use the version matching the `L10NSharp` PackageReference in
`src/BloomExe/BloomExe.csproj`, or the newest stable one. Downloading and running it needs the
developer's approval the first time, which the permission prompt for the command gives; keep it
in its own scratch directory.

```bash
curl -sL -o pkg.zip https://api.nuget.org/v3-flatcontainer/l10nsharp.checkorfixxliff/<ver>/l10nsharp.checkorfixxliff.<ver>.nupkg
unzip -q pkg.zip -d pkg     # the exe is under pkg/tools/
for f in DistFiles/localization/*/*.xlf; do echo "==== $f ===="; <exe> --fix "$f"; done > check-xliff.log 2>&1
grep -c -i -E 'crash|invalid|unexpected' check-xliff.log
find DistFiles/localization -name '*-fixed*'
```

Every problem it reports is a wrong translation. Don't fix anything yet: for each one, work out
the fix you would make on Crowdin, following [When a translation is wrong](#when-a-translation-is-wrong),
and carry it to the form in step 6. **Never change a translation in this commit that has not
been changed the same way on Crowdin.**

- Sort the problems into two groups: those this batch brought in, and those already in
  master's files before it (run the check on `origin/master`'s copy of the file, or see whether
  the batch changed that unit). The form shows the groups apart; both get proposed fixes.
- A `<name>-fixed.xlf` holds repairs CheckOrFixXliff made locally to format markers. Use them
  only as the proposed fixes; set the `-fixed` file aside. Often it makes none; then work out
  each fix from the English.
- Lines with `crash`, `invalid` or `unexpected` would crash Bloom. Nothing is pushed until each
  is fixed on Crowdin and the same fix is in this commit. If one can't be fixed without knowing
  the language, the form says so and asks the developer what to do.
- Missing or extra markers that don't crash: propose a Crowdin fix when the right one is clear
  without the language; otherwise propose a Crowdin issue for the translators, with this commit
  keeping the translation as Crowdin has it.

Delete `check-xliff.log`, `pkg.zip`, `pkg/` and any `-fixed` files when done.

## 5. Audit master and the candidate release branches

Audit master, and the two newest `VersionX.Y` branches as candidates for the cherry-pick, using
the local commit from step 3:

```bash
AUDIT=.claude/skills/merge-crowdin-translations/audit_translations.py
python -I -X utf8 $AUDIT origin/master HEAD <scratch>/audit-master
T=$(git merge-tree --write-tree --merge-base=HEAD^ origin/VersionX.Y HEAD) || { echo "CONFLICT: goes on the form"; exit 1; }
python -I -X utf8 $AUDIT origin/VersionX.Y $T <scratch>/audit-X.Y
```

On master expect only `lands` and `ignored` lines. `deleted`, `emptied` or `STOP` means Crowdin
dropped translations of strings master still has; that goes on the form.

What the audit means on a release branch:

- **lands** is what users of that version gain. Only these matter.
- **ignored: string this branch's English doesn't have** is translations of master-only
  strings. They ride along harmlessly and only make the files bigger.
- **mismatch** is a translation of master's wording for an id whose English differs on the
  branch.
- **deleted** (always audited): master's English has dropped these ids, so Crowdin dropped
  their translations, and the cherry-pick would remove the branch's existing translations
  too. Users of that version then see English for any the code still uses. The script lists
  each id and marks the ones the branch's `src` names literally (`IN CODE`); ids built at
  runtime, such as `TemplateBooks.PageLabel.*`, can be in use without being marked. For each
  `IN CODE` id, find the commit that removed it from master's English
  (`git log -S'<id>' --format='%h %cs %s' origin/master -- DistFiles/localization/en`) and read
  its message: a deliberate removal says which versions it was checked against, and code
  naming an id does not mean users see it.

## 6. The decision form

Use the `decider` skill to put one form to the developer, with these items:

- **Each wrong translation from step 4**, those this batch brought in apart from those already
  on master: language, string id, English, current translation, what is wrong, and the
  proposed fix on Crowdin (or the proposed Crowdin issue). Say which fixes change only
  placeholders; the bot marks those approved on Crowdin (see below).
- **Which User environment variable holds the SILCrowdinBot token**, if any fix or issue is
  proposed. List the variables whose names contain `CROWDIN`; the `crowdin-api` skill says why
  you can't pick by name. Before any write, confirm the chosen token authenticates as
  SILCrowdinBot, and stop if it doesn't.
- **Push to master**: master's audit numbers (lands by language).
- **Which release branches get the cherry-pick.** The developer decides this every time; never
  reuse an earlier answer. Recommend going back one version, the newest `VersionX.Y`; offer
  the newest and the one before it as the alternative, and no release branch. Show each
  candidate's audit: lands by language, mismatches, and the deleted ids with what their
  removal commits say. Choosing a branch approves pushing to it, and the null merges up the
  stack in step 7.
- Anything else steps 2–5 turned up: master audit problems, a conflict in `merge-tree`.

## 7. Carry out the answers

1. Make the approved translation fixes on Crowdin as the bot (`crowdin-api` skill), copy the
   same fixes into the translated files, and amend the commit. Post the approved Crowdin
   issues. Then rerun CheckOrFixXliff on the changed files and the step 5 audits, and check
   the numbers still match the form.
2. Push to master and retire Crowdin's pull request:

   ```bash
   git fetch origin
   test "$(git rev-parse origin/l10n_master)" = "<SEEN>" || { echo "l10n_master MOVED: stop"; exit 1; }
   git push origin HEAD:master || exit 1   # rejected if master moved: rebase onto origin/master, re-audit, ask again
   SQUASH=$(git rev-parse HEAD)            # write it down for steps 3 and 4
   PR=$(gh pr list --head l10n_master --json number --jq '.[0].number')
   gh pr close $PR --delete-branch --comment "Committed directly to master as $SQUASH."
   ```

   If `l10n_master` moved, Crowdin added translations after step 3: don't push or delete
   anything; tell the developer the sync wasn't paused in time.

   The remote `l10n_master` **must** be deleted: everything on it is now on master, and
   Crowdin recreates the branch, and a fresh pull request, for the next batch of translations.
3. For each chosen release branch:

   ```bash
   git switch -c crowdin-X.Y-<DATE> origin/VersionX.Y
   git cherry-pick <SQUASH>
   git push origin HEAD:VersionX.Y      # rejected if the branch moved: rebase, re-audit, ask again
   ```
4. Null-merge each picked release branch up the stack, oldest first: for example Version6.4
   into Version6.5, then Version6.5 into master. Each branch already has the translations from
   its own commit, so the merge only records that the cherry-pick below it is merged, and later
   merges up the stack don't conflict over it. First check that the lower branch has nothing
   the upper one lacks except this run's own commits; if anything else shows, merge commits
   included, stop and put it to the developer on a decider form, because a null merge would
   throw it away while recording it as merged.

   ```bash
   git fetch origin
   git log --oneline origin/<upper>..origin/<lower>
   #   every line must be one of this run's commits: a cherry-pick reading
   #   "Merge all the new translations from Crowdin (<DATE>)", or (for the second merge when two
   #   release branches were picked) the null merge this step just pushed
   git switch -c null-merge-<lower>-<DATE> origin/<upper>
   git merge -s ours origin/<lower> -m "Merge <lower> into <upper>" \
     -m "Null merge: <upper> already has the Crowdin translations (<DATE>) from its own commit."
   git diff --stat origin/<upper> HEAD                   # must print nothing
   git push origin HEAD:<upper>
   ```

   The developer's choice of release branches on the form also approves these merges.

## When a translation is wrong

This applies whenever a translation turns out to be wrong: one CheckOrFixXliff flags in step 4,
or one a tester reports at any other time.

1. **Consult the developer first**, through the `decider` skill. Show the language, the string
   id, the English, the current translation, what is wrong with it, and the fix you propose.
   Change nothing until they agree.
2. **Plan to fix it on Crowdin yourself, as SILCrowdinBot**, with the `crowdin-api` skill: add
   the corrected translation to that one string in that one language. Crowdin is where
   translations live; every later batch comes from there. Fix one string at a time through the
   API, not by uploading a whole translated file, which does not work cleanly.
   - **Approval on Crowdin.** When a fix changes only placeholders (`{0}`, `%1`, and the like),
     the bot marks it approved on Crowdin, so the next batch is sure to carry it. Any fix that
     changes wording stays unapproved, for a proofreader.
   - **Build the text from the existing translation**, changing only what the fix needs, and
     compare what Crowdin stored with what you meant, byte for byte. Typing non-Latin text by
     hand can reorder characters such as combining marks.
   - **Language ids**: Crowdin's can differ from the folder names here (Nepali is `ne-NP` on
     Crowdin, `ne` in the repo). Look them up in the project's languages rather than assuming.
   - **Removing a translation** on Crowdin makes the next batch show that unit untranslated:
     the English, with `state="needs-translation"`. Copy that into the commit.
3. **Fix only what doesn't need the language**: a broken or missing `{0}` or `%1`, broken
   markup, stray whitespace, an obvious paste of the wrong string. When the fix needs someone
   who knows the language, post a Crowdin issue on the string for its translators instead (the
   `crowdin-api` skill says how).
4. **The repo only ever copies a fix already made on Crowdin.** During this skill, put the same
   fix into the commit before pushing. At any other time, commit it to the translated file only
   if users need it before the next batch, and on the branches the developer names. Editing a
   non-English file is allowed for this, with the developer's agreement. A fix that isn't on
   Crowdin never goes in the repo: the next batch from Crowdin would put the wrong translation
   back.

## 8. Report

Report: the commit pushed to each branch, the audit numbers per branch, what was fixed on
Crowdin and what was raised as issues, and the closed pull request. Delete the local
`crowdin-…` branches and the scratch audit folders.

## 9. The developer resumes the sync

Tell the developer to click **Resume** on the same Crowdin page. Do this even if this skill
stopped partway, unless they want it left paused.
