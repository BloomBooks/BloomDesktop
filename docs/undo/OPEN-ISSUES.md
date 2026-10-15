# Undo design: issues to pick up next

Where the conversation about the page history API stopped (2026-10-15), and what is still open.
The design itself is in APPROACHES.md; section numbers below refer to it.

## Proposed, waiting for a decision

1. **The functions of a change outside the page neither read nor write the page.**
   `bidirectional`, `doAndRedo` and `undo` only set the state outside the page.
   `addChangeOutsidePage` returns what the function returned when the history called it to make the
   change, and the action writes any page change from that result itself, inside its transaction,
   where the history records it. During undo and redo any page write by those functions would be
   redundant, because the page restore puts back exactly what was recorded. The rule can be checked:
   a page change that the observer reports while the history is calling those functions during undo
   or redo throws in development. The Copyright and license example in 11.8.4 still writes the page
   inside `bidirectional` and would change to:

   ```ts
   await pageHistory().doTransaction(kCopyrightLicenseL10nKey, async (transaction: Transaction) => {
       const changed = await transaction.addChangeOutsidePage({
           before: props.data,
           after: gatherData(),
           bidirectional: (value: ICopyrightAndLicenseData) =>
               postJsonAsync("copyrightAndLicense/bookCopyrightAndLicense", value),
       });
       writeChangedFields(changed.pageFields);
   });
   ```

   This also settles the next item.

2. **Order between page changes and changes outside the page.** Undo and redo make every change
   outside the page first (in reverse order on undo, in the original order on redo) and then put back
   the page. The order in which the action made them, mixed, is not kept. That is safe only if the
   functions of a change outside the page do not depend on the page: item 1.

## API questions not yet discussed

3. **What `onAfterUndoOrRedo` passes its listeners.** Section 6.1 says `(roots: Element[])`; 11.8.1
   says `(restored: RestoredContent)`, that is `{ roots, direction }`. If no listener needs
   `direction`, plain `roots` is simpler and the name `RestoredContent` goes away.
4. **`pageHistory` or `pageHistory()`.** The examples mix both. A function works the same in every
   frame and returns the current page's history; the suggestion is `pageHistory()` everywhere.
5. **Whether `undo()` and `redo()` report a refusal** (`Promise<boolean>` instead of
   `Promise<void>`), so that the caller can tell the user that older history was discarded.
6. **The Undo button's read side** (John set this aside). Nothing tells the button when to change.
   Suggested: one `onUndoRedoChanged(listener)` that hands over `undoDescriptionL10nKey` and
   `redoDescriptionL10nKey` (`undefined` means nothing to undo), called on subscribe and after every
   change, replacing the read properties; and the root window subscribes once, with each new page's
   history reporting into it, so the button never has to know about page loads.

## Failure modes (John: "we will need to work on these at some point")

7. **A failure partway through several changes outside the page.** Each function is all or nothing,
   but a list of them is not. The design says the history calls the ones that already succeeded again
   with their other value and refuses the undo with the page unchanged; that repair can fail in turn,
   and what Bloom does then is open (11.8.4, Order).
8. **A transaction that is never ended.** Code that begins one at pointer down also ends it on
   pointercancel and loss of focus; a dialog ends or cancels its own however it closes; the history
   ends any open one when the page unloads. A history-side safety net (an element that the
   transaction's own input comes from, and ending the transaction on input outside it) was discussed
   and set aside; not recorded in APPROACHES at John's request.
9. **What Undo does while a transaction is open** (a drag in progress, a dialog that previews live).
10. **Typing during an async transaction** joins it (rule 7), so a slow action can take in the user's
    typing. A UX cost to weigh.

## Files and state outside the page

11. **Snapshots of files** (John's idea, not decided; 11.8.5): an action lists the files that it will
    change in place, the history moves them aside and swaps them back on undo and redo. Open: whether
    to check that an action changed no file that it didn't list, and how (John thinks a listing of the
    book folder at every transaction is overkill; the lighter form is for C#'s media-writing code to
    refuse a file that the open transaction did not list).
12. **"State held in C#" may be the wrong axis.** C# is where some code runs today, not a kind of
    state, and Bloom is moving to Electron with .NET kept for legacy code. The lasting axis may be
    where state lives: the page, book files, memory in some process. Then `meta.json` is the book's
    metadata and C#'s in-memory copy is a cache. Not actionable yet; revisit while digging into
    `addChangeOutsidePage`.
13. **Every caller of `CleanupUnusedSupportFiles`** must be checked for whether it can run while the
    Edit tab has a page open: `CollectionSettingsApi.cs:299`, `BrandingPreviewApi.cs:172`,
    `BookCommandsApi.cs:474`, CollectionModel, SpreadsheetImporter, the publish and upload paths,
    E2eTestingApi. If one can, it must clear the history first or wait.
14. **The paste route that rewrites a picture's metadata in place** (`editView/pasteImage`) must write
    a new file before C#'s refusal to overwrite a media file ships, since that request is not on
    `kRequestsThatEndTheHistory`.
15. **The leveled reader level** (answered by CODE-WALK.md): the body attribute is display only and
    never reaches the book from the page, so the level stays outside the history; the reload that
    every level change causes today can become a write in `doOutsideHistory`. APPROACHES case 7.6
    still needs updating to say so.
16. **Table operation keys.** bloom-table reports operations by English name ("Resize Column");
    `tableL10nKey(label)` maps them to keys, which must exist in the English XLF.

## Found in the code (CODE-WALK.md, 2026-10-15)

CODE-WALK.md tests the API against real places in the code; the § numbers in this part refer to
its Part 1. Its findings divide into code that Bloom should change, whatever the plan, and places
where the plan is wrong or says nothing. A few findings have a part of each and appear in both
lists. A few more are product decisions.

### Change Bloom's code

Deleting or overwriting a file that a page names:

- **Sign language Delete** (`signLanguage/deleteVideo`) moves the video file to the Recycle Bin.
  Make it work like removing a picture: a transaction removes the `<video>`, the file stays, and
  `CleanupUnusedSupportFiles` removes it when no history exists. The request then no longer needs to
  end the history. (§4)
- **`DeleteSupersededAiImageFiles`**: remove it, and have `CleanupUnusedSupportFiles` cover `.jpeg`
  and `.webp` so that those files are still removed later. (§4)
- **`editView/addImageFromUrl`** writes `bookButton-<bookId>.png` over the old one; write a new name.
  (§4)
- **The Games tool's built-in sounds**: skip the copy when the file is already there. (§4)
- **File names**: `GetUnusedFilename` should never give out a name that a file had earlier in the
  session, so that a path in C#'s picture caches always means one picture. (§4)

Writes to the page that happen without a click or key press, and that the save keeps:

- Hover buttons (the widget chooser button, `moveButton`, `deleteButton`) get `bloom-ui`.
- The picture tooltip's `title` and `data-title` are written in `doOutsideHistory` (see image
  metadata under "Improve the plan").
- Video selection stops being stored as `bloom-selected` in the saved markup. (§2)

Playback: `fixHighlighting` changes to CSS highlight ranges, as the rest of the highlighting already
does. This also fixes two bugs that users can hit today: typing during a paused playback is erased,
and the Music tool's preview leaves spans in the page. (§7)

Pictures: C# returns each new picture's pixel size with the change, so that the refit runs at once,
in the same transaction, instead of on the picture's `load` event. (§1)

Saving:

- `recordFractionOfPageOnImageSlots` writes to the copy that is saved, not to the live page. (§6)
- A save that renames the book folder leaves the open page's `<base>` pointing at the old folder.
  Either update `<base>`, or don't rename while the page stays loaded. PR 8209 needs this before the
  history does. (§6)
- C# stops changing page-div attributes that the live page never sees (`EditingViewApi.cs:265`
  removes `data-tool-id`). (§6)

Talking Book:

- `data-duration`: C# computes it from the audio files at save, instead of the player writing it
  when it loads the file. (§10)
- A too-short Speak press clears the whole history. That ends with the planned change that gives
  each recording a new id, so that C# never replaces a file. (§10)
- The visibility observer reruns `handleNewPageReady` after any class change on `<html>` or
  `.bloom-wordChoice`; its markup pass should write nothing when the markup is already right. (§8)
- The toolbox's records of which files exist (`haveAudio`, the video stats) refresh after undo and
  redo. That can be asynchronous, because it doesn't write the page. (§8)

Components:

- The Games tool's toggles read the page when clicked instead of writing the inverse of a cached
  value. (§8)
- The Canvas tool panel's 250 ms poll compares node objects, so it never sees an undo; it should
  listen to `onAfterUndoOrRedo`. (§8)
- The `ResizeObserver`s in the Motion tool and `CanvasElementEditingSuspension` each need their own
  fix. (§8)
- The colour picker can close through `hideColorPickerDialog`, which runs neither OK nor Cancel; and
  Cancel probably doesn't restore the colour that was there before (inferred, a bug today). (§11)
- The Motion tool's preview cleanup removes the page's `Landscape` class (probably a bug today).

### Improve the plan

Besides changing the code, the plan has two easy ways out for a finding: declare the state outside
the history, or make the action end the history.

- **Image metadata is outside the history.** Set Image Information, "Add this info to all images" and
  pasting a slot's own file onto that slot keep writing the metadata into the existing file, and
  undo doesn't touch it. Any copy of it on the page (the tooltip) is written in `doOutsideHistory`,
  since Bloom can recompute it from the file. So these requests don't end the history, C#'s refusal
  to overwrite a picture file must allow a metadata write, and §11.8.5's rule that a picture file
  is never overwritten gets that exception. A metadata edit keeps changing the credits on every page
  that shows the picture, as it does today. (§4, §13, §14)
- **Not decided:** three items under "Change Bloom's code" could take one of those ways out instead.
  `editView/addImageFromUrl` could end the history; video selection (`bloom-selected`) could be
  written outside the history, if `doOutsideHistory` is allowed for more than recomputed values; and
  the Games tool's built-in sound files could be declared outside the history.
- **`addPage` needs nothing.** It overwrites smart-page scripts, but it goes to the new page, which
  ends the history anyway. (§4)
- **Page changes after an `await`, a reply or an event are the normal case.** APPROACHES §10 says
  that the first step of the move wraps nothing. Every action whose page change comes after a reply
  needs `beginTransaction` at the click and `end()` once the reply is applied, from the first step.
  (§1)
- **Rule 1 assumes that Bloom's reactions finish before the next input.** Anything that waits on a
  load breaks that, and an undo can then return to a page that existed for a moment but was broken
  (BL-16870). The pixel-size change above fixes pictures; the plan must say what happens to any other
  reaction that lands late. (§1)
- **Page load has no defined end.** The plan needs a moment when the page is ready (after the
  toolbox's second `newPageReady`, the placeholder text and the background picture conversion),
  before which the history accepts no transaction; and a rule for the setup that a tool does when the
  user opens it: outside the history, or a boundary. The Games tool's random shuffle depends on that
  rule. (§3)
- **Rule 6's filter** was derived from the save path. It needs an audit of `mouseenter`,
  `mouseleave` and selection handlers too, and the plan must say which nodes the observer watches
  (zoom writes the style of `#page-scaling-container`). (§2)
- **Rule 1 counts only a net change.** `classList.add` of a token that is already there still queues
  a record, and the quiz and placeholder observers do that after every undo. (§8)
- **What listeners of `onAfterUndoOrRedo` may do later.** React renders, `useEffect` writes and
  `ResizeObserver` callbacks run after the restore. The plan must say how a listener that writes the
  page in a later task avoids becoming a new step. (§8)
- **Files.** "No action deletes a file while editing" becomes "no action deletes a file that any page
  state may name". C#'s refusal to overwrite needs a test that C# can apply: media files only, not a
  file that the same request created, and a flag that the front end keeps up to date for "a page
  history exists", because many writes run after their request has ended or with no request at all.
  The whole-folder passes (`ProcessBook`, the media level 1 migration) end the history. (§4)
- **`kRequestsThatEndTheHistory`**: add content languages from the top bar, topic, custom page
  layout, the leveled reader's reload and `editView/jumpToPage` after a custom layout conversion. Say
  how the reloads that C# starts (Report a Problem, a UI language change, a video edited in another
  program) clear the history; how to handle requests that act after replying
  (`pageList/contextMenuItemClicked`, `editView/duplicatePageMany`) and GETs that change state; and
  that axios runs request interceptors asynchronously. (§5)
- **Saving.** The plan assumes that the page DOM is what is saved; for the page div only a listed set
  of attributes is saved, and the plan should say so. Whole-book changes need the current page saved
  first, which conflicts with item 1; §11.5 must say who saves, and when.
  `_featureRequirementsBeforeEdits` must be retaken with `SaveStateForFullSaveDecision`. (§6)
- **The Games tool's Play tab** needs a way to stop recording, with Undo disabled, from entering Play
  to leaving it. The plan must also say what `clearUndoAndRedo()` in the middle of a click means for
  the rest of that event. (§9)
- **The planned change that gives each recording a new id** must allow for elements that share an
  audio id (data-book fields, custom layouts, copies from the data div) and for `_timings.txt`, which
  is named by the id. (§10)
- **Ending a drag's transaction.** The plan says pointercancel, but Bloom's drags use mouse events
  and end on `buttons === 0` at the next move, and the Motion tool's jQuery UI drags have only `stop`.
  The rule should say that the code ends the transaction on whatever ends its drag. (§11)
- **Which frame's clicks open an "Undo edit" transaction** (toolbox, top bar, root-window dialogs).
  (§12)
- **The AI image editor.** Launching it on a book that needs its page layout update is a boundary
  that §3.3 does not list. (§13)
- **The answers in §14** go into APPROACHES: the leveled reader level stays outside the history,
  Adjust Timings is not a boundary, and `toolbox/decodable` and `toolbox/leveled` fit
  `addChangeOutsidePage` with `bidirectional`.

### Product decisions

- **Page list actions** (Add, Duplicate, Delete, Move, Paste Page) navigate to another page, so a
  history per page never makes them undoable. (§13)
- **An AI image editor commit** changes and saves other pages too, so only the current page's part of
  it can be undone. (§13)

## Documents

33. **MINIMUM.md** proposes a first version that matches today's undo, which replaces APPROACHES
    §10.1's first step if it is agreed. Its choices to discuss: input outside the page frame that
    changes the page ends the history; every POST that is not on a short list ends the history; and
    the page is ready once no page content delay is pending. It also conflicts with work already on
    master or planned (RETIRE-CKEDITOR-COMPARED.md section 6): it switches off the table entries on
    the one stack (retire-ckeditor decision 8, merged), keeps redo where decision 6 discards the whole
    stack, and replaces `UndoStack`, on which PROGRESS.md's next step (Stage 3) and draft PR 8451
    build. Only one of the two can go next.
34. **RETIRE-CKEDITOR-COMPARED.md section 4** lists facts that our documents should take in: the
    paste filter bypass (case 11.1 says the opposite), Ctrl+Y doing redo in a CKEditor box (EXPERIMENTS
    §3.2 says it did nothing; one of the two is wrong), same-page reloads missing from §3.3, and the
    code of PR 8387, which APPROACHES §10.1 says was not read.

17. **The two Claude docs are paused** (John prefers to work in the Markdown). Since they were last
    updated: `doTransaction` (was `run`), `beginTransaction` (was `beginGesture`), descriptions are an
    l10n key only, `addToLatestTransaction` and `doOutsideHistory` (were `untracked`), `Transaction`
    passed to `fn` and returned by `beginTransaction`, `addChangeOutsidePage` with `bidirectional` or
    `doAndRedo`/`undo` (was `setBackEndState`), `registerOutsideState` dropped, nested transactions.
18. **The explainer page** has not been republished since version 12; its local file has the later
    edits.
19. **OUTLINE §16** still says "A file an undo would need could be gone after it" (the orientation
    change); under the cleanup rule the history is cleared before any cleanup runs. John's sentence;
    ask before changing it.
20. **Goal 7** (making a toolbox action undoable cheaply after the fact) reads weaker since
    `registerOutsideState` was dropped: case 7.6's leveled reader button now waits for an API for
    JavaScript state.
21. **Uncommitted:** dropping `descriptionEnglish`, nested transactions, the closed-transaction rule,
    this file and CODE-WALK.md.
32. **APPROACHES.md has not absorbed CODE-WALK.md yet.** The items under "Improve the plan" should
    go into APPROACHES once they are discussed.
