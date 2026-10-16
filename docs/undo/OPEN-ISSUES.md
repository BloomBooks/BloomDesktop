# Undo design: issues to pick up next

What is still open in the undo design (2026-10-16), with the page history built on this branch as
MINIMUM.md describes it. The design itself is in APPROACHES.md; section numbers below refer to it.

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

35. **Where the knowledge of which requests keep the history lives** (John is considering it; not
    decided). The first version keeps it in the front end, as `kRequestsThatKeepThePageHistory` in
    `utils/bloomApi.ts`, and ends the history before any other request whose method is not GET,
    HEAD or OPTIONS (section 11.8.7). Two other places are under consideration:
    - **A required flag on each C# endpoint registration** (`RegisterEndpointHandler`), which the page
      frame fetches once per page load. The decision sits beside the code that knows what the
      endpoint changes, and every new endpoint has to make it. The page frame needs the table before
      the first request that ends the history, and one endpoint whose effect depends on its data
      (`fileIO/copyFile`, which copies over an audio file for Talking Book's Import, and copies one
      of the Games tool's built-in sounds into the book) needs a flag that says more than yes or no,
      or splitting into one endpoint that writes a new file and one that overwrites, so that each
      endpoint has one effect to declare.
    - **The caller**, which says at each call whether the request keeps the history. The caller
      knows why it sends the request, so it can tell Import from the copy of a sound. The knowledge is
      spread over every call site, two callers can say different things about one endpoint, and a
      call that says nothing must still end the history.
    - **The list in `bloomApi.ts`, as built**: one place to review, away from both the endpoint and
      the caller. A request that nobody has added ends the history, which loses history and damages
      nothing; an entry that stays after its endpoint starts changing something that the page names
      is the risk.
36. **C# taking part in transactions** (a proposal, not decided). C# records its own change to its
    state and returns a token with its reply; `bloomApi.ts` attaches the token to the open transaction,
    and undo, redo and discarding a token go to C# through three fixed endpoints. Beside
    `addChangeOutsidePage` (section 11.8.4), where the front end supplies the values before and after
    and the functions that set them, the knowledge of what changed stays in C#, which holds state that
    the front end cannot read whole, and a request inside a transaction needs no code at its call
    site. Still to work out: what a request outside any transaction does; the order between page
    changes and C#'s (item 2); a failure partway (item 7); and when C# discards what a token holds
    (a cleared history, a page unload, a step dropped past the 50 that the history keeps).

## API questions not yet discussed

5. **Whether `undo()` and `redo()` report a refusal** (`Promise<boolean>` instead of
   `Promise<void>`), so that the caller can tell the user that older history was discarded.
6. **The Undo button's read side** (John set this aside). Nothing tells the button when to change:
   C# polls `canUndo` on a timer, and nothing shows the descriptions. Suggested: one
   `onUndoRedoChanged(listener)` that hands over `undoDescriptionL10nKey` and
   `redoDescriptionL10nKey` (`undefined` means nothing to undo), called on subscribe and after every
   change, replacing the read properties; and the root window subscribes once, with each new page's
   history reporting into it, so the button never has to know about page loads. A description key
   that no control's label already uses, such as `EditTab.Undo.Edit`, gets its XLF entry when the
   button shows the descriptions.

## Failure modes (John: "we will need to work on these at some point")

7. **A failure partway through several changes outside the page.** Each function is all or nothing,
   but a list of them is not. The design says the history calls the ones that already succeeded again
   with their other value and refuses the undo with the page unchanged; that repair can fail in turn,
   and what Bloom does then is open (11.8.4, Order).
8. **A transaction that is never ended.** Code that begins one at pointer down also ends it on
   pointercancel and loss of focus, as the first version's input transactions do; a dialog ends or
   cancels its own however it closes; the history ends any open one when the page unloads
   (`pagehide`). The "Undo edit" transaction waits for transactions begun inside it, so a dialog's
   transaction that is never ended keeps that one open too, and undo does nothing until the page
   unloads or something clears the history. A history-side safety net (an element that the transaction's own input comes from, and
   ending the transaction on input outside it) was discussed and set aside; not recorded in
   APPROACHES at John's request.
9. **What Undo does while a transaction is open** (a drag in progress, a dialog that previews live, a
   picture change waiting for its page content delays). The first version does nothing, a choice
   marked provisional in `pageHistory.ts`; `canUndo` counts an open transaction, so the Undo button
   is enabled then and its click does nothing. The alternative is to end the transaction first. The
   typing step is not affected: the press on the button or the undo key ends it before undo runs.
10. **Typing during an async transaction** joins it (rule 7), so a slow action can take in the user's
    typing. A picture change keeps its transaction open until no page content delay is pending, for
    up to 4 s (MINIMUM.md item 5), so typing in that time is undone with the picture. A UX cost to
    weigh. Two async `doTransaction` calls can overlap, when the second begins while the first
    awaits: the second nests in the first, the first waits for it if it settles first, and the step
    is made when the last of them ends. If the second then fails, every change since it began is
    put back, the first one's writes after that point included, since the history cannot tell which
    of two interleaved async functions made a change.

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
    a new file before C#'s refusal to overwrite a media file ships, since that request is on
    `kRequestsThatKeepThePageHistory`.
15. **The leveled reader level** (answered by CODE-WALK.md): the body attribute is display only and
    never reaches the book from the page, so the level stays outside the history; the reload that
    every level change causes today can become a write in `doOutsideHistory`. APPROACHES case 7.6
    still needs updating to say so.
16. **Table operation keys.** bloom-table reports operations by English name ("Resize Column");
    `tableL10nKey(label)` maps them to keys, which must exist in the English XLF. In the first
    version each table operation is an "Undo edit" step, and `bloom-table` still keeps its own
    history inside the library, though nothing in Bloom reaches it.
37. **The overflow checker's `padding-bottom`.** It writes `padding-bottom` into the `style` of a
    `bloom-padForOverflow` box (for the book title, only on the front cover), worked out from how
    far the text's descenders reach, and the save keeps it. The filter records it like any style
    property, so a write after an undo is a change outside any transaction, which joins the latest
    step and clears redo (rule 8). Undo leaves alone the `min-height` that the overflow checker
    writes into a box's `style` (`kBoxStylePropertiesIgnoredByUndoOnly` in `editTimeMarkup.ts`).
    Open: whether to treat `padding-bottom` the same.
38. **Undo while a save is in flight** (`SavePending`, `EditingStateMachine.cs:14`), which the save
    can overtake (retire-ckeditor PLAN.md:657-663). Still to decide (MINIMUM.md item 8).

## Found in the code (CODE-WALK.md, 2026-10-15)

CODE-WALK.md tests the API against real places in the code; the § numbers in this part refer to
its Part 1. Its findings divide into code that Bloom should change, whatever the plan, and places
where the plan is wrong or says nothing. A few findings have a part of each and appear in both
lists. A few more are product decisions.

### Change Bloom's code

Deleting or overwriting a file that a page names:

- **The Games tool's built-in sounds**: skip the copy when the file is already there. (§4)
- **File names**: `GetUnusedFilename` should never give out a name that a file had earlier in the
  session, so that a path in C#'s picture caches always means one picture. (§4)

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
- **Ways out taken:** `editView/addImageFromUrl` keeps the history, since the thumbnail's name
  depends only on the linked book's id, so every page that names it shows that book's cover; and
  undo leaves `bloom-selected` alone, as view state (`kClassesIgnoredByUndoOnly` in
  `editTimeMarkup.ts`).
- **Not decided:** the Games tool's built-in sound files could be declared outside the history
  instead of skipping the copy. The sound attributes on the page change outside the history; the
  copy, through `fileIO/copyFile`, ends it.
- **`addPage` needs nothing.** It overwrites smart-page scripts, but it goes to the new page, which
  ends the history anyway. (§4)
- **Rule 1 assumes that Bloom's reactions finish before the next input.** Anything that waits on a
  load breaks that, and an undo can then return to a page that existed for a moment but was broken
  (BL-16870). For pictures, the first version keeps the change's transaction open until no page
  content delay is pending (MINIMUM.md item 5), and the pixel-size change above would let it close
  at once. The plan must say what happens to any other reaction that lands late. (§1)
- **Setup that a tool does when the user opens it** is outside the history (Motion's rectangles,
  Image Description's wrapper and the description box that it adds, what Talking Book writes on
  opening), except Talking Book's sentence split, which is saved content and joins the latest step
  (MINIMUM.md item 4). Still open: the Games tool's random shuffle. (§3)
- **Rule 6's filter** was derived from the save path. It needs an audit of `mouseenter`,
  `mouseleave` and selection handlers too. (§2)
- **What listeners of `onAfterUndoOrRedo` may do later.** React renders, `useEffect` writes and
  `ResizeObserver` callbacks run after the restore. The plan must say how a listener that writes the
  page in a later task avoids becoming a new step. (§8)
- **Files.** "No action deletes a file while editing" becomes "no action deletes a file that any page
  state may name". C#'s refusal to overwrite needs a test that C# can apply: media files only, not a
  file that the same request created, and a flag that the front end keeps up to date for "a page
  history exists", because many writes run after their request has ended or with no request at all.
  The whole-folder passes (`ProcessBook`, the media level 1 migration) end the history. (§4)
- **Requests.** Under the first version's rule, the requests that CODE-WALK found missing from
  `kRequestsThatEndTheHistory` (content languages from the top bar, topic, custom page layout, the
  leveled reader's reload, `editView/jumpToPage` after a custom layout conversion) end the history
  already, since none is on `kRequestsThatKeepThePageHistory`; they matter again if the rule turns
  round (section 11.8.7). Still open: how the reloads that C# starts (Report a Problem, a UI language
  change, a video edited in another program) clear the history; requests that act after replying
  (`pageList/contextMenuItemClicked`, `editView/duplicatePageMany`); GETs that change state, and
  requests sent with jQuery's `$.ajax` or with `fetch`, which pass by the interceptor; and a page
  change that a caller makes synchronously right after a request that ends the history, which is
  dropped with the rest, since axios runs the interceptor a microtask after the call. (§5)
- **Saving.** The plan assumes that the page DOM is what is saved; for the page div only a listed set
  of attributes is saved, and the plan should say so. Whole-book changes need the current page saved
  first, which conflicts with item 1; §11.5 must say who saves, and when.
  `_featureRequirementsBeforeEdits` must be retaken with `SaveStateForFullSaveDecision`. (§6)
- **The Games tool's Play tab**: decided, recording stops, with Undo disabled, from entering Play to
  leaving it; not built. The plan must also say what `clearUndoAndRedo()` in the middle of a click
  means for the rest of that event. (§9)
- **The planned change that gives each recording a new id** must allow for elements that share an
  audio id (data-book fields, custom layouts, copies from the data div) and for `_timings.txt`, which
  is named by the id. (§10)
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

33. **Work that builds on the removed stack.** Draft PR 8451 and retire-ckeditor PROGRESS.md's next
    step (Stage 3) build on `UndoStack`, which the first version removes, along with the table
    entries on the one stack of retire-ckeditor decision 8 (RETIRE-CKEDITOR-COMPARED.md section 6).
34. **RETIRE-CKEDITOR-COMPARED.md section 4** lists facts that our documents should take in: the
    paste filter bypass (case 11.1 says the opposite), Ctrl+Y doing redo in a CKEditor box (EXPERIMENTS
    §3.2 says it did nothing; one of the two is wrong), and same-page reloads missing from §3.3.

17. **The two Claude docs are paused** (John prefers to work in the Markdown). Since they were last
    updated: `doTransaction` (was `run`), `beginTransaction` (was `beginGesture`), descriptions are an
    l10n key only, `addToLatestTransaction` and `doOutsideHistory` (were `untracked`), `Transaction`
    passed to `fn` and returned by `beginTransaction`, `addChangeOutsidePage` with `bidirectional` or
    `doAndRedo`/`undo` (was `setBackEndState`), `registerOutsideState` dropped, nested transactions,
    and the built API of 11.8.1.
18. **The explainer page** has not been republished since version 12; its local file has the later
    edits.
19. **OUTLINE §16** still says "A file an undo would need could be gone after it" (the orientation
    change); under the cleanup rule the history is cleared before any cleanup runs. John's sentence;
    ask before changing it.
20. **Goal 7** (making a toolbox action undoable cheaply after the fact) reads weaker since
    `registerOutsideState` was dropped: case 7.6's leveled reader button now waits for an API for
    JavaScript state.
32. **APPROACHES.md has not absorbed CODE-WALK.md yet.** The items under "Improve the plan" should
    go into APPROACHES once they are discussed.
