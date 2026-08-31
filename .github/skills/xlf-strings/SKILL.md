---
name: xlf-strings
description: Add or review localizable strings in Bloom XLF files
---

Apply this skill whenever you add a new localizable string to the codebase, modify an existing one, or review XLF files as part of a task.

This skill is the **how** — the procedure for each operation. The **why** — how Crowdin actually
works and what each kind of edit does to existing translations — lives in one place,
`DistFiles/localization/README.md`. Where a rule here has a reason, that reason is written there
and cited from here rather than restated, so the two can't drift into telling you different
things. Read its "Why we can't just delete a string" section before you conclude that any
deletion or id change is harmless.

## Files

Localizable strings live in `DistFiles/localization/en/`. Only ever add or change strings in the **English** subdirectory — never touch the other language folders.

There are three priority files. Choose based on what the string labels:

| File | Use for |
|---|---|
| `Bloom.xlf` | The most important things in our UI that users must understand
| `BloomMediumPriority.xlf` | Secondary options, help text, feature-specific instructions, important error messages |
| `BloomLowPriority.xlf` | Rarely-seen text like error messages |

Strings that are only meant to be seen by developers or if code bugs occur should not be localized. Add comments explaining why they are not.

## Adding a new entry

1. **Ask the user which priority file to use, with your recommendation.** Explain why you prefer one based on the table above. You may present several strings as a single question if they occur in the same context.
2. Add the entry in the chosen file:

```xml
<trans-unit id="Namespace.EntryName" translate="no">
  <source xml:lang="en">The English text</source>
  <note>ID: Namespace.EntryName</note>
</trans-unit>
```

- Always mark new entries `translate="no"` unless instructed otherwise.
- Choose an ID in the form `Namespace.EntryName` that matches the feature area.

## Translator context notes

After the `<note>ID: ...</note>` line, add a **second `<note>`** whenever a translator would not have enough context from the string alone. This is required when the string is any of:

- A generic or short word ("Source", "Search", "More info", "Not Ready")
- A sentence fragment assembled with other strings
- A numbered step in a sequence
- Link text
- A string where product names, placeholders, or case constraints matter

The note should state: what UI element it labels, where it appears in the UI, and any constraints (e.g. "appears mid-sentence, should be lowercase", "step N of M in X instructions", "'Bloom' is a product name and must not be translated", "{0} is replaced with a count").

Example with context note:
```xml
<trans-unit id="ImageLibrary.PixabayStep4" translate="no">
  <source xml:lang="en">Paste it below</source>
  <note>ID: ImageLibrary.PixabayStep4</note>
  <note>Step 4 in the Pixabay API key instructions. "It" refers to the API key copied in step 3; "below" refers to the text input field below the instruction list.</note>
</trans-unit>
```

## Modifying an existing entry

- **Never change the ID** of an entry that is not new (i.e. not marked `translate="no"`). Changing an ID loses all existing translations.
- **Never change the source text** of a translated entry unless you are certain it won't invalidate existing translations.
- If you need to change the text or ID: mark the old entry with a `<note>` saying `Obsolete as of <version>` and create a **new entry** with a new ID and the updated text. Find the current version in the `Version` property in `build/Bloom.proj`. Avoid this when possible.

## Marking an entry obsolete, and when it may be deleted

BL-16686 found four entries marked obsolete that were all still in use, so be careful with both halves of this:

- **Only add the obsolete note once nothing references the entry.** Search before you write it: code (`l10nKey`, `l10nId`, `useL10n`, `GetString`), shipped content under `src/content` — note that sample shells are `.htm`, not `.html` — and the XLF files themselves. Page label ids in particular are composed at runtime (`"TemplateBooks.PageLabel." + label`), so a page's visible label text is what to grep for, not just the id. A wrong note is actively harmful: it tells the next cleanup pass to delete a string we still use.
- **Do not delete an obsolete entry unless the developer explicitly asks you to.** Deleting an entry from the English file deletes its translations too, and judging when that has become safe is a human call — so mark and leave, never tidy up on your own initiative. `DistFiles/localization/README.md`, "Why we can't just delete a string", explains the route translations travel and why the obvious safety argument (*"but the release branch still has the entry"*) is false. When you **are** explicitly asked to delete the entries for a given version, delete only from `DistFiles/localization/en/`; leaving the translated files intact means re-adding an id later recovers its translations.

### Establishing that a particular deletion loses nothing

There is one case where deletion costs no translations: an entry that was **always**
`translate="no"` was never handed to Crowdin, so no translation of it has ever existed
(`DistFiles/localization/README.md`, "The exception: a string Crowdin never had"). Two commands
establish it, and they are worth running before you either write an obsolete note or answer a
developer asking whether an entry can go.

```bash
ID=CollectionSettingsDialog.AdvancedTab.Experimental.AppBuilder
FILE=DistFiles/localization/en/BloomMediumPriority.xlf

# 1. Was it ALWAYS translate="no"? Print the trans-unit line as it stood in every commit that
#    ever touched it — on master AND on the release branch currently receiving updates,
#    since an entry can be edited on one and not the other. You want translate="no" on every
#    version of the line.
#    Release branches are named origin/Version<major>.<minor>; fill in the one that is current
#    (`git branch -r --list 'origin/Version*'` lists them). Left as-is this fails rather than
#    quietly answering for the wrong branch.
RELEASE=origin/VersionX.Y
for ref in HEAD "$RELEASE"; do
  echo "--- $ref"
  # -G, not -S: -S only lists commits where the NUMBER of occurrences of the id changed, so a
  # commit that edited only the translate attribute on that line would be skipped silently.
  for c in $(git log --format=%h -G "$ID" $ref -- "$FILE"); do
    git show $c:"$FILE" | grep "trans-unit id=\"$ID\""
  done
done

# 2. Does it appear in any translated file? The en/ hit should be the only one.
grep -rl "$ID" DistFiles/localization/
```

Clean on both means deletion is lossless. That still does **not** make it your call: report the
findings and let the developer decide.

When a deletion does go ahead on that basis, **put the evidence where the reviewer will meet
it** — the commit message and the reply on the review thread, in as many words: always
`translate="no"`, absent from every translated file. Review bots take their rules from
`src/BloomBrowserUI/AGENTS.md` (Devin cites it by name, with `based_on_repo_rules: true`) and
read the rule rather than your reasoning, so the deletion *will* be flagged. With the evidence
attached, the next person sees a rule correctly applied; without it, they see a rule broken.

## Reviewing XLF changes in a PR

When reviewing a PR that touches XLF files:

1. Check that every new entry has `translate="no"`.
2. Check that new entries are in the right priority file (see table above).
3. Check that every entry whose context isn't obvious from the string alone has a translator context note.
4. Check that no existing translated entry has had its ID or source text changed without the obsolete/new-entry pattern.
5. If entries were **deleted**, check the PR says why that was lossless — always `translate="no"`, absent from every translated file (see "Establishing that a particular deletion loses nothing"). Deletion without that evidence is the finding; deletion with it is fine.
