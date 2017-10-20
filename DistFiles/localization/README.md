# Bloom localization workflow

Starting with Version 4.0, Bloom uses Crowdin (https://crowdin.com/project/sil-bloom) to create
and maintain the localization of its many UI strings and texts.  The Crowdin translations are
automatically integrated with the master BloomDesktop repository on github
(github.com/BloomBooks/BloomDesktop.git).  This imposes certain restrictions on how we can
modify the English xliff files found in the various subdirectories of this directory since the
Crowdin updates its (English) source files automatically.

Crowdin automatically creates a pull request (from an automatically created l10n_master branch)
against the master branch, updating it every 10 minutes with a new commit whenever translation
work is actively happening.  This imposes certain requirements on how we merge such a monster
pull request to avoid overwhelming the git log with possibly 99% translation changes.

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
  does the same in the *target* element content (translation).

- Adding, removing, or reordering *trans-unit* elements in the English xliff file without
  changing them merely causes the corresponding addition, removal, or reordering in the
  translation xliff file.

- Editing the *source* element content causes the approval status of the *trans-unit* element to
  be reset but does not affect the content of the *target* element (translation).

- Adding an empty *target* element to a *trans-unit* element has no effect apart from possibly
  putting the *target* and *note* elements in the right order in the target (translated) xliff
  file.

- Adding, deleting, or editing *note* elements has no effect on the translation content.  It
  does change what is displayed as context for the translation in crowdin.

## Effects of translated xliff file changes

- Modifying a translated xliff file and committing/merging it to the master BloomDesktop
  repository on github has no effect on what is seen on the crowdin site.  A translated xliff
  file must be explicitly uploaded to the SIL-Bloom crowdin project to have any effect there.
  (Editing and committing/merging a translated xliff file may complicate merging in changes from
  crowdin, however.)

- Uploading to crowdin a translated xliff file with the new *trans-unit* element added the
  translation to crowdin without approving it.  (The upload dialog had a checkbox for approving
  uploaded translations which I left unchecked.)  A previously deleted *trans-unit* element did
  not get added to crowdin.  An unedited English source that had been edited separately in the
  English xliff file and committed/merged retained the change that had been made in English
  rather than reverting to what is in the edit translated xliff file.

- Uploading to crowdin a translated xliff file with an edited translation which is marked as
  approved replaced any translation on the crowdin site (even one that had been approved), and
  left the approved flag set on crowdin.

- Uploading to crowdin a translated xliff file with an edited translation which not marked as
  approved did not change an approved translation on crowdin, or change the approval status on
  crowdin.  It did add the modified translation as a suggestion.

- Changing the *original* attribute of the *file* element in a translated xliff file and then
  uploading it to crowdin has no effect on what you see on crowdin.

- Changing the *product-version* attribute of the *file* element in a translated xliff file and
  then uploading it to crowdin has no effect on what you see on crowdin.

- Changing the *datatype* attribute of the *file* element in a translated xliff file and then
  uploading it to crowdin has no effect on what you see on crowdin.


## Restrictions on xliff file changes

- ***Never*** change the *original* attribute of the *file* element in a source xliff file.

- Change the *product-version* attribute of the *file* element in a source xliff file only when
  you think it is really needed.

- Change *id* attributes of *trans-unit* elements in source xliff files only when absolutely
  necessary.

- Changes to translated xliff files that are committed to github must be independently uploaded
  to crowdin to have any effect on the translations there.

- If you edit a translation apart from crowdin and then commit/merge it to github and follow up
  by uploading to crowdin, remove any *approved* attribute from the *trans-unit* element* unless
  you are absolutely sure of the translation as an expert speaker and translator.


## Merging crowdin pull requests

The crowdin pull request is structured as a series of commits, possibly one every 10 minutes
while translation work is going on.  This is good because the PR reflects translation progress
almost as soon as it is made, but bad in that each commit adds an entry to the history log.  On
the master branch, it is much better to squash down all the commits in the PR to a single commit
with a comment along the lines of "merge recent translations from crowdin".  A simple merge
would bring over all the history for all the commits, which would effectively hide the log
entries for real programming work.  This means that a manual process is needed to properly merge
in translations from crowdin.  (If there were a separate repository just for localizations, then
a normal merge with all that history would probably be okay.)

1. Before anything else, go to https://crowdin.com/project/SIL-Bloom/settings#integration and
   click on the "Pause Sync" button.  This prevents a flood of translation work continuing to be
   added to the pull request branch while you are trying to merge the pull request.

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
        git log --oneline | grep -c '^[0-9A-Fa-f]\{7\} New translations .*\.xlf (.*)$'
    </pre>

   <p>Then use the git command for amending history.  (Use whatever number the preceding command
   prints instead of 432 in the following command.  For example, this assumes that there are 432
   commits labeled "New translations" in the branch.)  Every line in the commit message file
   except the first should be marked as *fixup*.  The first line should be marked *reword*.  The
   "git log" command is just to reassure yourself that everything looks okay after squashing
   down all the history.

    <pre>
        git rebase -i HEAD~432
        git log
    </pre>

4. Force push the modified l10n_master branch back to the master BloomDesktop repository.  The
   form of the command given here ensures that nobody else has modified the branch on github
   since you acquired it.

    <pre>
        git push --force-with-lease origin l10n_master
    </pre>


5. Merge the modified PR on github using the normal web browser interface.  If translated xliff
   files have been modified locally, this may require resolving conflicts.  The web browser
   conflict resolution view will essentially show you a simple textual comparison of the
   l10n_master and master branch contents.  You will probably want to delete one branch's
   content for each conflict (plus the 3 added lines marking the beginning and end of the
   conflict) unless you are an expert translator and familiar with this simpleminded approach to
   conflict resolution. After merging, delete the remote l10n_master branch using the handy
   "Delete branch" button provide by the web browser interface.  **Do not forget to delete the
   remote l10n__master branch!**

6. Delete the temporary local copy of the BloomDesktop repository.

    <pre>
        cd ~/tmp
        rm -rf BloomDesktop
    </pre>

7. Go back to https://crowdin.com/project/SIL-Bloom/settings#integration and click on the
   "Resume" button to allow translation changes to be committed to the l10n_master branch once
   again.  The l10n_master branch (and a new pull request) will be automatically created by
   crowdin when it is first needed.
