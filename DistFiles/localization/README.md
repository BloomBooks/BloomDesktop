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
  changing them merely causes the corresponding addition, removal, or reordering in the
  translation xliff file.

- Editing the *source* element content causes the approval status of the *trans-unit* element to
  be reset but does not affect the content of the *target* element (translation).

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


## Restrictions on xliff file changes

- ***Never*** change the *original* attribute of the *file* element in a source xliff file.

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

The Crowdin pull request is structured as a series of commits, possibly one every 10 minutes
while translation work is going on.  This is good because the PR reflects translation progress
almost as soon as it is made, but bad in that each commit adds an entry to the history log.  On
the master branch, it is much better to squash down all the commits in the PR to a single commit
with a comment along the lines of "merge recent translations from Crowdin".  A simple merge
would bring over all the history for all the commits, which would effectively hide the log
entries for real programming work.  This means that a manual process is needed to properly merge
in translations from Crowdin.  (If there were a separate repository just for localizations, then
a normal merge with all that history would probably be okay.)

1. Before anything else, go to [Crowdin Integration
   Settings](https://crowdin.com/project/SIL-Bloom/settings#integration) for github and click on
   the "Pause Sync" button.  This prevents a flood of translation work continuing to be added to
   the pull request branch while you are trying to merge the pull request.

2. Create a temporary clone of the master BloomDesktop repository on your local machine and
   checkout the l10n_master branch.

    <pre>
        cd ~/tmp
        git clone https://github.com/BloomBooks/BloomDesktop.git
        cd BloomDesktop
        git checkout l10n_master
    </pre>

3. Cleanup the history on the l10n_branch, squashing all the multitudinous commits down to a
   single commit.  This requires figuring out how many commits have been made to the branch.

    <pre>
        git log --oneline | grep -c '^[0-9A-Fa-f]\{9\} New translations .*\.xlf (.*)$'
    </pre>

   <p>Then use the git command for amending history.  (Use whatever number the preceding command
   prints instead of 432 in the following command.  For example, this assumes that there are 432
   commits labeled "New translations" in the branch.)  Every line in the commit message file
   except the first should be marked as *fixup*.  The first line should be marked *reword*.  The
   "git log" command is just to reassure yourself that everything looks okay after squashing
   down all the history.  [NB: the count is likely too high.  I think 3 individual commits have
   snuck through somehow.]

    <pre>
        git rebase -i HEAD~432
        git log
    </pre>

4. Verify that the modified xliff files are still valid, and fix any errors that may be found.
   If you execute build/getDependencies-Windows.sh to get all the artifacts from TeamCity, then
   on Windows, the following command could be used in the git bash shell window:

   <pre>
       for f in DistFiles/localization/*/*.xlf; do
           echo ==== $f ====
           lib/dotnet/CheckOrFixXliff.exe --fix "$f"
       done | tee check-xliff.log
   </pre>

   On Linux, the shell command is almost the same:

   <pre>
       for f in DistFiles/localization/*/*.xlf; do
           echo ==== $f ====
           /opt/mono4-sil/bin/mono lib/dotnet/CheckOrFixXliff.exe --fix "$f"
       done | tee check-xliff.log
   </pre>

   This checks for invalid XML and for malformed formatting markers ({0}, {1}, etc.).  It tries
   to fix any malformed formatting markers that it finds.  If it does find and fix any, it
   creates a new file with the same name of the one with the invalid formatting string(s), but
   with "-fixed" appended to the name.  If any such files are created, they should be renamed to
   remove the "-fixed", and the commit updated to include the fixed file.  Then the command
   given above using CheckOrFixXliff.exe should be run again to check that nothing will crash.

   Fix any crashing errors that are (still) reported.  If the XML file is malformed or malformed
   formatting markers remain that would crash Bloom, the output log file will contain the words
   "crash", "invalid", or "unexpected" in it.  This can be checked easily by

   <pre>
       grep -c '\(crash\|invalid\|unexpected\)' check-xliff.log
   </pre>

   Fixing any remaining crashing errors may require hand editing the offending xliff file, or it
   may require updating the CheckOrFixXliff program by editing its sources in a copy of the
   l10nsharp repository.  Other errors (missing or extra format markers) probably won't crash
   the program, and there's no way for a programmer to fix them.  The check-xliff.log file could
   be passed on to someone in touch with the translators (Chris Weber maybe?).

   I'm not sure how to feed corrected strings back to Crowdin.  Just uploading the corrected
   translated xliff file to crowdin does not work, at least not cleanly.

5. Force push the modified l10n_master branch back to the master BloomDesktop repository.  The
   form of the command given here ensures that nobody else has modified the branch on github
   since you acquired it.

    <pre>
        git push --force-with-lease origin l10n_master
    </pre>


6. Merge the modified PR on github using the normal web browser interface.  If translated xliff
   files have been modified locally, this may require resolving conflicts.  The web browser
   conflict resolution view will essentially show you a simple textual comparison of the
   l10n_master and master branch contents.  You will probably want to delete one branch's
   content for each conflict (plus the 3 added lines marking the beginning and end of the
   conflict) unless you are an expert translator and familiar with this simpleminded approach to
   conflict resolution. After merging, delete the remote l10n_master branch using the handy
   "Delete branch" button provide by the web browser interface.  **Do not forget to delete the
   remote l10n__master branch!**

7. Delete the temporary local copy of the BloomDesktop repository.

    <pre>
        cd ~/tmp
        rm -rf BloomDesktop
    </pre>

8. Go back to [Crowdin Integration
   Settings](https://crowdin.com/project/SIL-Bloom/settings#integration) for github and click on
   the "Resume" button to allow translation changes to be committed to the l10n_master branch
   once again.  The l10n_master branch (and a new pull request) will be automatically created by
   Crowdin when it is first needed.


## Updating Bloom and Palaso xliff files

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
