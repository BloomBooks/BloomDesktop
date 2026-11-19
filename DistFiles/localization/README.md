# Bloom localization workflow

Starting with Version 4.0, Bloom uses [Crowdin](https://crowdin.com/project/sil-bloom) to create
and maintain the localization of its many UI strings and texts.  The Crowdin translations are
automatically integrated with the master BloomDesktop repository on github
(github.com/BloomBooks/BloomDesktop.git).  This imposes certain restrictions on how we can
modify the English xliff files found in the various subdirectories of this directory since the
Crowdin updates its (English) source files automatically.

Crowdin automatically creates a pull request (from an automatically created l10n_master branch)
against the master branch, updating it every 10 minutes with a new commit whenever translation
work is actively happening.  This imposes certain requirements on how we merge such a monster
pull request to avoid overwhelming the git log with possibly 99% translation changes.

Note that the Crowdin integration with github has a one-way flow from source Xliff files to
translated Xliff files.  It pulls changes made to the source Xliff files (or new source Xliff
files) on the github master branch automatically, and it creates a pull request to reflect
changes to translated files made on Crowdin.  Changes to translated Xliff files made outside of
Crowdin (or entirely new translated Xliff files created outside of Crowdin) must be uploaded
manually to Crowdin for translators to see those translations.  If this is needed, it should be
done using the SILCrowdinBot account to avoid having a developer's name attached to the
translations.  (Of course, if a programmer doesn't mind being called a lousy translator ...)
Once the translation process is started on Crowdin for a given language, translation changes
made outside of Crowdin are discouraged because it complicates merging changes made on Crowdin
and it negates most of the value of using Crowdin to begin with.

## The "Pseudo-English" UI language (qps-ploc) has no files here, and never should

On the developer, alpha and internal (BetaInternal, ReleaseInternal) channels, the UI Language menu offers **Pseudo-English (i18n test)**,
whose language tag is the standard pseudo-locale `qps-ploc`.  It is *not* a translation.  It is
produced by L10NSharp at lookup time by transforming the live English text: every vowel is
doubled with an accent on the first of the pair, and the whole string is wrapped in brackets, so
`Title Missing` becomes `[Tîitlée Mîissîing]`.  Format placeholders (`{0}`, `%0`, `{name}`) and
markup pass through untouched.

It exists so we can see internationalization problems that are invisible in English:

- plain English in the UI = a hard-coded string that was never internationalized;
- a visible `{0}` / `%0` / `{name}` = a broken placeholder;
- a missing `]` = the string is being truncated;
- brackets in the middle of a sentence = the sentence is being concatenated at runtime;
- clipped or overflowing layout = the layout can't cope with the ~30-40% growth that real
  translations routinely bring.

Because the pseudo text is derived from the English at the moment of lookup, **no `qps-*`
xliff files exist, are loaded, or are ever written**, and none should ever be added here or to
Crowdin.  The pseudo-locale is always exactly as complete as the English source strings are.
(The unrelated `qaa` folder here is a leftover from Crowdin and has nothing to do with this.)

### What stays plain English on purpose

The tester's rule is: **plain English means the string was never internationalized, unless it
is one of the things below.**  Each of these shows English under the pseudo-locale for a known
reason, so it is not a finding:

- **Things localized as whole files, not string by string.**  Bloom picks the file for the
  current UI language at runtime and falls back to English if there isn't one.  There is no
  `qps-ploc` file for any of them, and there should not be one:
  - the built-in template readmes (`ReadMe-en.htm`, baked from
    `DistFiles/localization/<Template>/ReadMe-<lang>.xlf` at build time by
    `src/BloomBrowserUI/scripts/l10n-build.js`).  A development build generates only
    `ReadMe-en.htm`, so there they are English in *every* UI language;
  - readmes of downloaded or user-made templates (`ReadMe-en.md`): author content that we
    never localize at all;
  - help and documentation pages reached through
    `BloomFileLocator.GetBestLocalizableFileDistributedWithApplication`;
  - xmatter descriptions (`<desc>-<lang>.txt`, see `XMatterInfo`).

  This does not weaken the test.  A whole document is either translated or English, so there
  are no individual strings to tell apart, and you can see a readme is in English without any
  transform.
- **The language chooser** (in Collection Settings and the New Collection wizard), apart from
  the dialog's title and buttons, which are Bloom's.  It comes from the
  `@ethnolib/language-chooser-react-mui` package, which has its own translations and falls back
  to English for a language it doesn't know, such as `qps-ploc`.  Teaching it the pseudo-locale
  would mean changing that package, so it is excluded for now.
- **Messages meant only for developers**, such as the Alpha-only "Unexpected field ..." toasts,
  which are deliberately not localized.  The buttons on them, such as **Report**, *are*
  localized, so English on a button is still a finding.

One thing goes the other way: the pseudo-locale does reach a file name.  A new book's folder is
first named after the localized word for "Book" (`[Böoöok]-…` here, `Livre-…` in French), then
renamed to the book's title when it is saved.  Every UI language does this, so it is expected.

See BL-16748, and `LocalizationManager.PseudoLocalizationLanguageId` /
`OfferPseudoLocalization` / `PseudoLocalize` in L10NSharp.

## Effects of English xliff file changes

- Changing the *original* attribute of the *file* element in the xliff file causes all *target*
  elements' content (translations) to be deleted.

- Changing the *product-version* attribute of the *file* element in the xliff file causes the
  *target* elements' content (translations) to be regenerated and automatically reapproved.
  History in the form of *alt-trans* elements might be lost in this process.

- Changing the *datatype* attribute of the *file* element in the xliff file seems to have no
  effect.

- Changing an *id* attribute of an existing *trans-unit* element causes the *target* element
  content (translation) for that *trans-unit* element to be removed.  If the actual *source*
  element content is unchanged, the original translation is available in translation memory with
  a "100% match" displaying.

- Adding or removing leading or trailing spaces in the *source* element content automatically
  does the same in the *target* element content (translation) without changing the approved
  status of the *trans-unit* element.  Except if the content contains internal html markup, then
  the *target* element is not changed and the *approved* status of the *trans-unit* element is
  cleared.  (This appears to be a corner case in Crowdin's code that may or not be intentional.)

- Adding, removing, or reordering *trans-unit* elements in the English xliff file without
  changing them causes the corresponding addition, removal, or reordering in the translation
  xliff file.  Note what the "removal" half of that means in practice: **deleting a
  *trans-unit* from the English file deletes its translations too.**  That is why we normally
  mark a string obsolete rather than deleting it — see "Why we can't just delete a string"
  below.

- I (AP, May 2026) am pretty sure this is affected by the `update_option` option in crowdin.yml.
  - What I think is true today with `update_option: update_without_changes` on master: Editing the *source* element content does not affect the translation.
  - What this document had previously (and I think is true of `update_option: update_as_unapproved` which we had previously): Editing the *source* element content causes the approval status of the *trans-unit* element to be reset but does not affect the content of the *target* element (translation).

- Adding an empty *target* element to a *trans-unit* element has no effect apart from possibly
  putting the *target* and *note* elements in the right order in the target (translated) xliff
  file.

- Adding, deleting, or editing *note* elements has no effect on the translation content.  It
  does change what is displayed as context for the translation in Crowdin.

- Adding a new English xliff file to the master branch on github after the initial setup adds
  that file to Crowdin on the next sync.

## Effects of translated xliff file changes

- Modifying a translated xliff file and committing/merging it to the master BloomDesktop
  repository on github has no effect on what is seen on the Crowdin site.  A translated xliff
  file must be explicitly uploaded to the SIL-Bloom Crowdin project to have any effect there.
  (Editing and committing/merging a translated xliff file may complicate merging in changes from
  Crowdin, however.)

- Uploading to Crowdin a translated xliff file with the new *trans-unit* element added the
  translation to Crowdin without approving it.  (The upload dialog had a checkbox for approving
  uploaded translations which I left unchecked.)  A previously deleted *trans-unit* element did
  not get added to Crowdin.  An unedited English source that had been edited separately in the
  English xliff file and committed/merged retained the change that had been made in English
  rather than reverting to what is in the edit translated xliff file.

- Uploading to Crowdin a translated xliff file with an edited translation which is marked as
  approved replaced any translation on the Crowdin site (even one that had been approved), and
  left the approved flag set on Crowdin.

- Uploading to Crowdin a translated xliff file with an edited translation which not marked as
  approved did not change an approved translation on Crowdin, or change the approval status on
  Crowdin.  It did add the modified translation as a suggestion.

- Changing the *original* attribute of the *file* element in a translated xliff file and then
  uploading it to Crowdin has no effect on what you see on Crowdin.

- Changing the *product-version* attribute of the *file* element in a translated xliff file and
  then uploading it to Crowdin has no effect on what you see on Crowdin.

- Changing the *datatype* attribute of the *file* element in a translated xliff file and then
  uploading it to Crowdin has no effect on what you see on Crowdin.


## Why we can't just delete a string

This is the reasoning behind the "mark it obsolete, don't delete it" convention. It is written
down **here and nowhere else**; `src/BloomBrowserUI/AGENTS.md` and
`.claude/skills/xlf-strings/SKILL.md` state the resulting rules and point back at this section.
If you are about to argue that some particular deletion is safe, read this first — the wrong
argument for it is an easy one to reach.

**Translations only ever travel one route, and every leg of it runs through master:**

1. Crowdin takes its **source** strings from the English xliff files **on master**. Remove an id
   from master and Crowdin stops knowing about that string.
2. Finished translations come **back into master** as the periodic `l10n_master` pull request
   (see "Merging Crowdin pull requests" below).
3. A release branch gets those translations **only** by cherry-picking that merge — we do this
   for the newest release branch, and sometimes the one before it, as the developer decides
   each time.

So an id deleted from master is gone from Crowdin, therefore absent from every later
`l10n_master` merge, therefore absent from anything picked into `Version6.x`. **The release
branch still listing the entry in its own copy of the file saves nothing** — its translations
were never going to come from that copy; they were going to come from master. This is the trap:
"but the 6.4 branch still has the entry" feels like a safety argument and is not one.  It is
worse than that: the cherry-pick carries master's translated files, so it also **removes** the
release branch's existing translations of that id, and its users see English from then on.

That is also why a deletion is only *eventually* safe. Once the version in which the string went
obsolete has shipped and no longer receives updates, nothing will ever need to pick translations
for it again, and the entry can go. Judging that is a human call.

### The exception: a string Crowdin never had

New entries on master are added with `translate="no"`, which is what keeps them out of Crowdin
until we say they are ready. (Entries added on a release branch are not; see "When a new entry
gets translate="no"" below. So this exception never covers them.) An entry that carried
`translate="no"` from the day it was added until the
day it became unused was therefore never offered to a translator, has no translations anywhere,
and appears in none of the language subdirectories. Deleting it loses nothing, and an obsolete
note on it protects nothing.

Establishing that is mechanical — print the entry's line as it stood in every commit that ever
touched it, on master and on the current release branch, and confirm `translate="no"` on all of
them; then confirm the id appears only under `en/`. `.claude/skills/xlf-strings/SKILL.md` has
the exact commands. Deciding to act on it is still the developer's call, and the evidence
belongs in the commit message and in the PR-review reply, because a reviewer — human or bot —
reading only the rule will otherwise flag the deletion, quite correctly.

## When a new entry gets translate="no"

It depends on the branch the entry is added on.

- **On master**, a new entry gets `translate="no"`. Master carries the alpha, and the flag keeps
  its new strings away from translators until we say they are ready.
- **On a release branch** (`VersionX.Y`), a new entry does **not** get it. That version is about
  to ship, and its users need the string translated as soon as possible. The entry reaches
  Crowdin when the release branch is merged forward to master (see the route above), and the
  translations come back to the release branch the same way as any others.

`.claude/skills/xlf-strings/SKILL.md` states the rule and points back here.

## Restrictions on xliff file changes

- ***Never*** change the *original* attribute of the *file* element in a source xliff file.

- **Do not delete a *trans-unit* from a source xliff file.**  Mark it obsolete instead.  See
  "Why we can't just delete a string" above for the reasoning and for the one exception, and
  `.claude/skills/xlf-strings/SKILL.md` for the note format itself.

- Change the *product-version* attribute of the *file* element in a source xliff file only when
  you think it is really needed.

- Change *id* attributes of *trans-unit* elements in source xliff files only when absolutely
  necessary.

- Changes to translated xliff files that are committed to github must be independently uploaded
  to Crowdin to have any effect on the translations there.  The SILCrowdinBot account should be
  used to upload such files to Crowdin.

- If you edit a translation apart from Crowdin and then commit/merge it to github and follow up
  by uploading to Crowdin, remove any *approved* attribute from the *trans-unit* element* unless
  you are absolutely sure of the translation as an expert speaker and translator.

- If you change the leading or trailing spaces in a translation, check to ensure that the same
  is done on Crowdin after uploading the file, and restore any approved status that was cleared
  by a failure to update the translated string automatically.

## Merging Crowdin pull requests

Crowdin keeps one pull request open from the `l10n_master` branch, adding a commit to it every
10 minutes or so while translators work.  Merging it as is would bury the log for real
programming work.  So every so often, when we decide to bring in what translators have done
since the last time, we squash that pull request to one commit, commit it directly on master,
and then cherry-pick it directly onto the newest release branch, and sometimes the one before
it, as the developer decides each time.

The procedure is the `merge-crowdin-translations` skill,
`.claude/skills/merge-crowdin-translations/SKILL.md`.  An agent runs all of it except pausing
and resuming Crowdin's sync, which a person does on the [SIL-Bloom GitHub
integration](https://crowdin.com/project/sil-bloom/integrations/system/github) page.  A person can also
follow the skill by hand; every step is a plain git, gh or python command.

## Updating Bloom and Palaso xliff files

I (AP, May 2026) am pretty sure this whole section is obsolete/incorrect.

Bloom 4.1 and later have a special mode of operation to help with updating the English source
Bloom and Palaso xliff files that operates only with Debug builds.  It is enabled either by a
"--harvest-for-localization" command line argument or by setting an environment variable
"HARVEST_FOR_LOCALIZATION" to "on" or "yes".  In this mode, the English Bloom.xlf and Palaso.xlf
files are not loaded from DistFiles/localization/en when the program starts, and any existing
English Bloom.xlf or Palaso.xlf that are created in the user's data area are deleted so that
fresh copies will be made by scanning the program and by collecting any strings encountered by
dynamic calls to the LocalizationManager.  When Bloom finishes, the existing files from
DistFiles/localization/en are merged into the new files that have the newly collected strings.
Differences in the two files are marked by adding notes to the affected trans-unit elements.

To maximize the strings collected, you need to step through as much of the program as possible
so that as many dynamic strings as posssible are encountered.  Here's a list of possible steps
to accomplish this end.  In some ways it reads like a test script.

 1. Start Bloom with the "--harvest-for-localization" command line argument.  Make sure the UI
    language is set to English.

 2. Create a new Local Language (English) book collection.  Click on the "Settings" icon.  Click
    on each tab of the dialog.  Change the National Language to "Tok Pisin" (tpi).  Enable the
    "Show Experimental Templates" and "Show Experimental Commands" features.  On Windows, check
    the "Automatically update Bloom" feature.  Close the dialog with the "Restart" button.

 3. Create a new basic book and start editing it.

 4. On the front cover, type in a title.  Then click on the format gear and close it without
    changing anything.  After closing the formatting dialog, click on the "Click to choose
    topic" link.  Close the menu without changing anything.  Add a picture to the front cover
    from Art of Reading.  Check its copyright from the upper left icon link.

 5. Open the toolbox to expose the Talking Book Tool.  Click on the More... at the bottom.
    Check the "Decodable Reader Tool" box, then click on the "Set up Stages" link to open the
    dialog.  Close the dialog without changing anything.  Click on the "Generate a letter and
    word list report" link, and then close the document that opens.  Click on the More... at the
    bottom of the toolbox pane again.  Check the "Leveled Reader Tool" box, then click on the
    "Set up Levels" link. Close the dialog without changing anything.  Close the toolbox pane.

 6. Click on the "Inside Front Cover" thumbnail.  Click on the "Title Page" thumbnail.  Click on
    the "Credits Page" thumbnail.  Click on the "Click to Edit Copyright & License" link.  Add
    your name to the "Copyright Holder" box.  Close the dialog by clicking "OK".  Click on the
    "Add Page" icon and add a default ("Basic Text & Picture") page.

 7. Add a JPG (or PNG) picture to the default page from the filesystem.  (A photograph or
    screenshot picture will do.)  Click on the "Set up metadata..." link.  Enter appropriate
    values for the Creator, Copyright Year, and Copypright Holder fields.  Close the dialog with
    "OK", and close the picture dialog with "OK".  Type some text in the text box, then open the
    Format dialog from the gear icon.  On the Style tab, click on the "Create a new style" link.
    Close the dialog without creating anything.  Highlight a word of the text to bring up the
    character style popup.

 8. Click on the "Inside Back Cover" thumbnail.  Click on the "Outside Back Cover" thumbnail.
    Click on the "Title Page" thumbnail.  Click on the "Paste Image Credits" link.  Click on the
    "Publish" tab icon.

 9. Click on each of the publish type icons in turn, from top to bottom.  Click on the "Start
    Serving" button on the "Android" publishing page, then click on the "Stop Serving" button.
    Change the "Method Choices" selection to "Send over USB Cable".  Click on the "Connect with
    USB cable" button.  Click on the "Stop Trying" button.  Change the "Method Choices"
    selection to "Save Bloom Reader File".  Click the "Save..." button.  Cancel out of the
    output file selection dialog that pops up.

10. Click on the "Collections" tab icon.  Click on each of the "Templates" books in turn.  Click
    on each of the "Sample Shells" books in turn.

11. Click on the "Get more source books at BloomLibrary.org" link.  Download "Two Brothers" from
    the test site.  Create a book "Two Brothers" and start editing it.  Unlock the book.  Change
    the book to be "Two Languages" with both English and Tok Pisin.  Go to the first page of
    text.  Cut the text from the English box and paste it in the Tok Pisin box.  (The text is
    obviously Tok Pisin.)  Do the same for a few more pages.  Go back to the first page of text.
    Lock the book again.  Change back to "One Language" with only English.

12. Go the collection tab.  Create a new "Source Collection".  Create a book from "Big Book".
    Type in a title.  Go to the Instructions page.  Add a "Just a Picture" page.  Add a picture,
    giving it a copyright owner and creator.  Add a "Just Text" page.  Type something in the
    page.  Close Bloom.

On Windows, the output Bloom.xlf and Palaso.xlf files will be found at

<pre>
    C:\Users\<username>\AppData\Local\Temp\Bloom.xlf
    C:\Users\<username>\AppData\Local\Temp\Palaso.xlf
    C:\Users\<username>\AppData\Local\SIL\Bloom\localizations\en\Bloom.xlf
    C:\Users\<username>\AppData\Local\SIL\Bloom\localizations\en\Palaso.xlf
</pre>

On Linux, the output files will be found at

<pre>
    /tmp/Bloom.xlf
    /tmp/Palaso.xlf
    /home/<username>/.local/share/SIL/Bloom/localizations/en/Bloom.xlf
    /home/<username>/.local/share/SIL/Bloom/localizations/en/Palaso.xlf
</pre>

Note that it is better to run this process on Windows for two reasons.  First, there are often
features (and therefore strings) that are implemented only on Windows, but the opposite is
rarely if ever true.  Second, some code sends out \r\n pairs for line endings which usually
doesn't matter when displaying strings in Linux, but can affect the file content with literal
carriage return characters possibly inserted into the middle of lines on while processing on
Linux.

The first two files (those in the temporary directory) are the merged output files combining the
result of the harvesting with the old content coming from the corresponding files stored in the
DistFiles/localization/en directory.  The latter two files (in the SIL/Bloom/localizations/en
directory) are the direct output of the harvesting process.  Rather than blindly replacing the
files in source control with those from the temporary directory, some thought should be given to
which changes in the files are really desireable.  Consider these points in particular:

* Not all of the new strings that are harvested are really meant to be localized for Bloom in
  general.

* Some of the comment notes added during the merge process probably should be removed.

* Strings added back from the old file can possibly be removed, but should be checked against
  the sources at least as far back as the Version4.0 branch.  Any strings that exist in
  Version4.0 but not later should have a comment added that they are used by Version4.0.

There may be other points to consider as well before blindly moving new or modified data into
source control and thence to crowdin.
