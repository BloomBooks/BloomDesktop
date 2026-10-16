# The minimum that matches today's undo

The smallest version of the page history (APPROACHES.md, recorded mutations with the API of section
11.8) that undoes everything that today's undo undoes. Later steps add to it one at a time. Section
numbers refer to APPROACHES.md, and "issue N" to Part 1 of CODE-WALK.md.

It is built (`src/BloomBrowserUI/bookEdit/pageHistory/`, with the changes that the items name
elsewhere), first in commit `ec71549342`, and the items below describe the code on this branch,
which also takes some of the later steps: dialogs and toolbox controls that make undo steps of their
own or change the page outside the history (item 4). "Today" and "today's undo" mean Bloom before
commit `ec71549342`.

"Matches" means three things:

- every action that today's undo covers can be undone and redone, one step per action, from Ctrl+Z,
  Ctrl+Y and the Undo button;
- no undo damages the page, including undos of actions that today's undo leaves alone (rules 1 and 4
  of section 3.0.2 hold from the start);
- redo is not lost to things that don't lose it today, such as moving the mouse.

Some history that survives today is lost in the minimum, because the minimum ends the history where
a later step would record more. Those losses are listed at the end.

## What today's undo covers

From `bookEdit/undo/` (PR 8387), `ImageUndoManager.ts` and their callers.

| Action | Today's mechanism | Undo offered when |
|---|---|---|
| Typing and formatting in a text box | CKEditor's undo plugin, one history per box, 20 steps | always, for the box that has the focus |
| Typing while a reader tool is open | the reader tools' own history of each box's HTML and caret (`readerToolsModel.ts`); Ctrl+Z runs it and CKEditor's undo both | a reader tool is active |
| Table operations | `bloom-table`'s history, as entries on the one stack (`undo/tableUndo.ts`) | not in Change Layout mode |
| Replacing a picture: image chooser, gallery, paste, AI image editor (`changeImage`, `changeImageByElement`, `bloomEditing.ts:450-500`) | the image undo restores `src`, `data-copyright`, `data-creator`, `data-license`, the crop and the box's geometry | that picture is selected |
| Rotate right and flip (`CanvasElementManager.ts:1446-1467`) | the image undo | that element is selected and its text hasn't changed since |
| Rotating a canvas element by dragging (`CanvasElementHandleDragInteractions.ts:381`) | the image undo; text boxes too | as for rotate right |
| Removing a background picture, back to the placeholder (`CanvasElementManager.ts:3036-3044`) | the image undo | that picture is selected |
| Change Layout mode | origami's own copies of `.marginBox`, with its own redo | the mode is on |

The one stack (`undo/UndoStack.ts`, 50 entries) lives in the page frame and starts empty with each
page load. It holds the table entries and asks the other four mechanisms in turn, in a fixed order,
with a change counter deciding between a table entry and CKEditor's typing (`undo/changeOrder.ts`).
Only the Undo button goes through it: the button posts `editView/topBarButtonClick`, C# runs
`topBarButtonClick` in the page frame, which calls `handleUndo` (`bloomEditing.ts:1714-1717`), and C#
polls `canUndo` on a timer. Ctrl+Z is taken in the page frame by origami, the reader tools or
CKEditor, each with its own history; with a reader tool active, the reader tools' undo and
CKEditor's both run. `undo/redoKeyBinding.ts` binds Ctrl+Y to the stack's redo, but no entry on the
stack can redo, so Ctrl+Y always goes to CKEditor, origami or the reader tools. There is no Redo
button. `BloomField.PreventRemovalOfSomeElements` reverses a deletion with the browser's own
`execCommand("undo")` (`bloomField/BloomField.ts:887-897`). `runUndoable` exists but nothing calls it, so deleting a canvas
element is not undoable. Moving, resizing or cropping a canvas element, adding one, styles, dialogs,
Talking Book, video, games and book settings have no undo today.

## What the minimum builds

1. **The history itself**: `pageHistory()` for each page load, the `MutationObserver`, `doTransaction`
   and `beginTransaction`, undo and redo with the check of rule 4, and `clearUndoAndRedo()`. Rule 1
   counts only a net change, so that a `classList.add` of a class that is already there (the quiz and
   placeholder observers after every undo, issue 8) doesn't clear redo. A change to a node outside
   the page counts only if undo could need that node (`nodesThatUndoMayNeed`), so that the overflow
   checker's hidden clone, which it makes and throws away to measure text, doesn't clear redo.
2. **The filter of rule 6**, so that undo neither records nor puts back what the save strips: the
   declarative rules of section 3.7 in one module (`editTimeMarkup.ts`), tested value by value
   (class token by token, style property by property), and CKEditor's tracked filling character
   taken out of text values; a text node that holds only that character is left out of child lists.
   Besides what the save strips, it leaves out what Bloom works out again or what only marks a
   selection: `data-bloom-active`, `bloom-selected` on a video container, the overflow checker's
   classes and the `min-height` that it writes into a box's `style`, `bloom-imageLoadError`,
   Comical's generated SVG, and the attributes that CKEditor sets on a box. It leaves a
   description box (`.bloom-imageDescription`) out of the child lists of the `.bloom-canvas` that
   holds it, and records what is inside it, so that the box that Image Description and Talking
   Book add to a picture when they open, outside the history (item 4), does not make undo refuse
   a step that holds that child list, such as adding a canvas element; nothing takes a
   description box out of the page. Section 3.7, "The
   module as built", lists where its rules differ from that section's lists. The `padding-bottom`
   that the overflow checker writes into a `bloom-padForOverflow` box's `style` is recorded
   (OPEN-ISSUES.md item 37). On master the save strips the live page and then reloads it, so the
   history never meets what the save strips; the save path takes up the same module when PR 8209
   stops the reloads, and until then the module's rules are checked against master's save path.
3. **Rule 2 for input in the page frame.** A real click or key press in the page frame that changes
   the page opens an "Undo edit" transaction (`pageHistoryInput.ts`, with capture-phase listeners on
   the window). A drag is one transaction from pointer down to the event that ends it (a release,
   `pointercancel`, the window losing the focus, or, with mouse events, `buttons === 0` at the next
   move, issue 11); it closes from `setTimeout(0)` after that event's task, so that Bloom's handlers
   for `pointerup`, `mouseup` and `click` run inside it, where a microtask would close it before them.
   Typing in one box breaks into steps where CKEditor 4.5.1's undo plugin broke them: every 25 input
   events, when the user goes from typing to deleting or back, and at a navigation key; a command (a
   key press with Ctrl or Alt, AltGr excepted) is a step of its own, and so are a paste, a cut and a
   drop. Like CKEditor, it ignores IME key presses (keyCode 229). A typing step also ends when the
   focus leaves the box and at any press of a pointer. A modifier or lock key pressed alone opens
   nothing, so that the Ctrl of a Ctrl+Y can't make a step and clear redo. With rule 2, table
   operations and canvas element rotation are undoable without wrapping, and so are canvas element
   moves and other page-frame actions that today's undo leaves alone.
4. **Input elsewhere that changes the page with no transaction of its own ends the history.** A
   click or key press in the toolbox frame, the top bar or a root-window dialog opens an "Undo edit"
   transaction as in the page frame. When that transaction closes, if it made a step that still has
   the general description, the history is cleared (`endHeld` in `pageHistoryInput.ts`). A
   `doTransaction` or `beginTransaction` begun inside it, such as a canvas command's or a dialog's,
   gives the step its own description, so the step stays. The "Undo edit" transaction waits for
   one that is still open, and closes when that one ends; if that one is cancelled while something
   else in the "Undo edit" transaction changed the page, the step keeps the general description and
   the history is cleared. A change that the action makes after an `await`, once the task of the
   press has ended, is made outside any input and joins the latest step (rule 1). The Undo button
   is exempt: it carries `data-runs-undo`, and its press opens nothing, since its click changes the
   page by undoing. This takes the place of the open question of which frame's clicks count (issue
   12).

   Bloom's delayed reactions that go through `addToLatestTransaction` (the toolbox's markup pass,
   the fit of a canvas element to a new picture or video, Talking Book's sentence split) join the
   latest step even when a press has opened an "Undo edit" transaction that has changed nothing
   yet (`isStandInThatHasChangedNothing` in `pageHistory.ts`). So such a reaction never makes a
   step of its own, and a press in another window that it happens to follow does not end the
   history because of it.

   The dialogs and toolbox controls that change the page fall into three groups:
   - **Undo steps with their own description**: Original Title, Set Destination, Set Up Hyperlink,
     Choose Video on a canvas element (with the box fitted to the new video),
     the Sign Language tool's Record, Import and Delete, dragging a Motion rectangle, Talking Book's
     playback-order arrows and its recording-mode switch, and the Canvas tool's controls, with a
     colour picker session as one step from opening to OK.
   - **Changes made outside the history** (`doOutsideHistory`): the page settings of Book and Page
     Settings, with its live preview and its Cancel, which puts the page's pictures back too; the
     books chosen for a link grid; Image Description's checkbox and the wrapper that it puts around
     each picture, which it takes away before each undo and redo and puts back after; the
     description box that Image Description and Talking Book add to a picture that has none; the Music
     tool's settings; the Games tool's three options and its sounds; trimming a sign language
     video; the Motion checkbox and preview; Talking Book's playback highlighting and the setup that
     it writes on opening, apart from its sentence split; and, for now, every page change of the
     Format dialog, until one session of the dialog becomes one undo step. None of these is
     undoable, and none ends the history. The cost: a later undo whose step holds a value that one
     of them changed, or names a node that one of them took out of the page, is refused by the
     check of rule 4, and the refusal empties that stack.
   - **Ends the history**: what still sends a request that is not on item 7's list (Talking Book's
     Record, Clear and Import; posting book settings; copying a Games built-in sound into the book),
     or reloads the page (changing the Leveled Reader's level, through
     `EditingModel.EnsureLevelAttrCorrect`; Set Image Information; the book's Copyright and
     license), and any press elsewhere whose change has no transaction of its own.

   Opening a tool is no boundary: Talking Book's sentence split joins the latest step, and what
   Motion and Image Description write on opening is outside the history (issue 3).
5. **Explicit transactions for the covered actions that rule 2 can't see.**
   - The bodies of `changeImage` and `changeImageByElement`, which C#, the image gallery and the AI
     image editor call, each in a transaction begun with `beginTransaction` and ended once no page
     content delay is pending, so that the refit after the picture loads, which holds one, can't
     join the user's next input (issue 1). If the delays never clear, the transaction ends after
     4 s (`kMaxWaitTimeMs`, when a save also stops waiting). This is `doTransactionUntilPageSettles`
     in `bloomEditing.ts`; a call made inside another's change joins it. The AI image editor's swaps
     on the current page in one commit go in one such transaction around them all. The pictures
     that the same commit replaces on other pages, which C# changes and saves, are outside the
     history: undo puts back only the current page's. Paste Image needs nothing more: C#
     calls `changeImage` with the result.
   - A `doTransaction` at each canvas command routing point (`runCommandControlAction` in
     `canvasControlRegistry.ts` for menu rows, `runControlCallback` for toolbar buttons and rows
     with their own `onSelect`), described by the control's label, so that a command from a menu, a
     toolbar or the Canvas tool (removing a background picture, rotate, flip) is a step and not a
     boundary.
   - The JavaScript functions that C# calls for text paste and cut (`pasteClipboard` after
     `editView/paste`, `cutSelection` from the cut case of `topBarButtonClick`), each in a
     `doTransaction`, so that a paste is its own step as it is today, not joined to the typing
     before it.
     `topBarButtonClick` also runs the Undo button's undo, which opens no transaction, so only its
     cut case is wrapped; the top bar's paste goes straight to C#
     (`EditingViewApi.HandleTopBarButtonClick`), which then calls the paste function.
6. **A moment when the page is ready.** The history starts when the page loads, so that a click or
   key press in the first seconds is recorded and undoable like any other. Until the page is ready, a
   change made outside any transaction (tool setup, CKEditor taking over a box, the background
   picture conversion) is not recorded, as if made in `doOutsideHistory`; if one of them later
   changes a value that a user's step holds, undo's check refuses that undo, which loses history and
   damages nothing. An e2e run showed the cost of starting the history only at the ready moment: an
   action right after a page load was not undoable. The page is ready when no page content delay
   (`wrapWithRequestPageContentDelay`) is pending, every CKEditor instance has fired `instanceReady`
   (CKEditor writes `<p><br></p>` into an empty box after `CKEDITOR.inline()` returns), and the
   toolbox's second `newPageReady`, 600 ms later, has run (issue 3), and after it CKEditor and the
   delays again, since the toolbox's passes can start both (`pageReadiness.ts`). This keeps the
   background picture conversion (BL-16870) and the Games tool's shuffle out of the user's first
   step.
7. **Requests to C#: everything that isn't known to be harmless ends the history.** An axios request
   interceptor in `utils/bloomApi.ts` clears the history before any request to C# whose method is
   not GET, HEAD or OPTIONS and whose path is not on `kRequestsThatKeepThePageHistory`: requests that
   change nothing that the page names, or only write a file under a new name. The list holds the
   class a requests of CODE-WALK Part 3, `imageGallery/imageGalleryResult`, `editView/pasteImage`,
   `editView/paste`, `editView/topBarButtonClick`, `aiImageEditor/commit`, `audio/copyAudioFile`,
   `fileIO/chooseFile`, the Sign Language tool's requests that write a video under a new name or
   only choose or cancel one, the progress dialog's `progress/ready`, `progress/cancel` and
   `progress/closed`, `editView/addImageFromUrl` (a link grid's thumbnail, whose name depends only
   on the linked book's id), and requests that only read, or that change only the Edit tab's UI,
   user settings, the clipboard, logs or files that no page names. `aiImageEditor/commit` can be on the list because a commit deletes no file:
   `DeleteSupersededAiImageFiles` is gone, and `CleanupUnusedSupportFiles` covers `.jpeg` and
   `.webp` instead. This inverts `kRequestsThatEndTheHistory`: a request that nobody has classified
   loses history instead of risking a file or C# state that undo can't put back. axios runs the
   interceptor a microtask after the call, before the request goes out, and requests sent with
   jQuery's `$.ajax` pass by it. The minimum needs no `addChangeOutsidePage`,
   no refusal to overwrite in C#, and no complete list of the requests that end the history
   (issues 4 and 5).
8. **The reloads stay.** Every save still reloads the page, so a save ends the history as it does
   today, and nothing that the save does to the live page, the page div or the book folder matters yet
   (issue 6). Still to decide: what an undo does while a save is in flight (`SavePending`,
   `EditingStateMachine.cs:14`), which today the save can overtake (retire-ckeditor PLAN.md:657-663).
9. **The old histories are switched off**: CKEditor's undo plugin (`config.removePlugins`), the
   image undo (`ImageUndoManager.ts`), the reader tools' history and redo, and the one stack with its
   providers and change counter (`bookEdit/undo/`), all removed. `bloom-table` still keeps its
   history inside the library, but nothing in Bloom reaches it: the page frame takes the keys first,
   and nothing calls it. With the undo plugin gone, CKEditor fires no `change` event, so Bloom fires
   it from each box's `input` events for the listeners that need it. The reader tools do not
   rewrite a box: their `doMarkup` reads the text and paints CSS highlights, and the only change
   that it makes to a box, removing markup that earlier versions of Bloom saved, is outside the
   history (section 5.3). Origami keeps its own undo inside Change Layout mode, run in
   `doOutsideHistory`, and entering or leaving the mode ends the history (section 11.6). The Text
   Box Properties dialog opens only in that mode, so the step that it makes, named
   `EditTab.TextBoxProperties.Title`, can never be undone: leaving the mode ends the history first.
10. **The keys and the button**: the Undo button keeps its route (C# calls `handleUndo` and polls
    `canUndo`), which now answers from the page history. Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z are taken in
    the capture phase of the page frame's `keydown` (section 8.2), ahead of CKEditor and the reader
    tools, and the other frames forward them. In Change Layout mode the handler leaves the keys to
    origami, as `undo/redoKeyBinding.ts:66` did. The other frames don't forward a key pressed in a
    text field or a dialog, whose own undo the user means, and the page frame leaves the keys to an
    `input`, a `textarea`, a `select` or a dialog in it too, but not to the page's text boxes.
    `beforeinput` with `historyUndo` or `historyRedo` is cancelled, except in those fields, and
    `PreventRemovalOfSomeElements` becomes a `beforeinput` handler that refuses an edit whose range
    covers a `.bloom-preventRemoval` element instead of calling the browser's undo
    (`pageHistoryKeys.ts`, `BloomField.ts`). The keys that undo and redo don't
    schedule the markup pass (section 5.3): the keyups that follow them stop at the page window, so
    the toolbox's keyup handler in the page never sees them, and the toolbox abandons a pass that is
    already scheduled in `onBeforeUndoOrRedo`. `canUndo` counts an open transaction, because typing holds one open
    between key presses and its step is made only when something ends it, such as the press on the
    Undo button. The steps of items 4 and 5 carry their own descriptions, and every other step the
    general one. Nothing shows them yet, so a description key that no control's label already uses,
    such as the general one, `EditTab.Undo.Edit`, has no entry in the XLF files until the Undo button
    shows the descriptions.
11. **`onAfterUndoOrRedo` for the components that the covered actions touch**
    (`listenToPageHistory` in `bloomEditing.ts`): the canvas element manager's
    `updateAfterUndoOrRedo` (active element, `data-bloom-active`, comicaljs, handles; not through
    `refreshCanvasElementEditing` as it stands, because it reruns `SetupElements`, which adds the
    edit key handlers to `document` again each time); the reader highlights, painted again from the
    restored text without rewriting the box; and Talking Book, whose `updateAfterUndoOrRedo` drops
    the records that its visibility observer has not handled yet, so that a restore starts no markup
    pass (issue 8), and paints its highlight again. The Motion tool draws its rectangles again, and
    Image Description takes its wrapper away in `onBeforeUndoOrRedo` and puts it back after (item
    4). `bloom-table`'s selection UI (case 7.8) has no listener; the e2e table tests pass without
    one.

    Undo and redo restore the caret and selection before these listeners run, with recording off,
    so what a focus handler writes then is undo's own and is not recorded (`undoOrRedo` in
    `pageHistory.ts`). In `onBeforeUndoOrRedo` the canvas element manager notes the canvas elements
    on the page and the active one, and drops the fits to a picture or video that are still waiting;
    after the undo it selects the canvas element that the step changed, or else starts from the
    element that was active as the undo began, not from one that restoring the caret made active.
    In `onBeforeUndoOrRedo` Talking Book also stops playback and puts back what its playback
    highlighting replaced.
12. **Code that writes the page with no click or key press** (issue 2), changed so that moving the
    mouse or selecting doesn't clear redo: the hover buttons get `bloom-ui`, and the picture tooltip's
    `title` and `data-title` are written in `doOutsideHistory`.

## Left out of the minimum

Each of these is a later step, in any order, and each removes a boundary or a loss from the list
below.

- Removing the reloads (section 3.3, PR 8209), with the saving fixes of issue 6.
- `addChangeOutsidePage` and the C# side of section 11.8.4; moving requests off item 7's rule one at
  a time, as each is recorded or shown to be harmless.
- The file rules of section 11.8.5 and the code fixes of issue 4.
- The input in the toolbox and dialogs that still ends the history (item 4) as undo steps, one
  component at a time, each with its `onAfterUndoOrRedo` work (issue 8): Talking Book's Record,
  Clear and Import, which need recordings under new file names (issue 10); the Leveled Reader's
  level, whose change saves and reloads the page; and the changes that item 4 makes outside the
  history, wherever one should become undoable.
- The Games tool's Play tab: decided, recording stops, with Undo disabled, from entering Play to
  leaving it (issue 9); not built.
- StyleEditor writing `<style>` text (rule 5, case 7.5), and one session of the Format dialog as
  one undo step, which makes format changes undoable.
- Descriptions for the actions that still get the general one, by wrapping them at their routing
  points.
- Marking the rest of Bloom's reactions with `addToLatestTransaction` or `doOutsideHistory`, and
  the development log of section 10.7.
- CSS highlight ranges for playback (issue 7).

## Where the minimum is worse than today

- **History is lost on a page change from the toolbox, the top bar or a dialog that item 4 neither
  makes an undo step nor keeps outside the history**: recording, changing the Leveled Reader's
  level, and any control that has no transaction of its own. Today CKEditor's history of a box
  survives these.
- **History is lost on every request that isn't on item 7's list**, including copying one of the
  Games tool's built-in sounds into the book (`fileIO/copyFile`, which the interceptor can't tell
  apart from Talking Book's Import).
- **History is lost when an undo is refused** because a change made outside the history (item 4)
  altered a value that the step holds, or took out of the page a node that the step names, such as
  undoing a sign language recording after trimming it. The refusal empties the undo stack.
- **Playback still writes the page** (issue 7), outside the history, and typing during a paused
  playback is erased, as it is today.
- **Ctrl+Z undoes the latest change on the page**, not the latest change in the box that has the
  focus, as CKEditor's history for each box does today, and a picture change is undone whether or
  not the picture is selected. Users will notice both.
- **Undo does nothing while a transaction is open**: during a drag, and after a picture change
  until its page content delays clear, for up to 4 s. The Undo button stays enabled then.

Gains over today, without any extra work: canvas element moves, resizes, crops, additions and
deletions, and every other page-frame action, are undoable as "Undo edit" steps; Ctrl+Z and the
Undo button do the same thing; with the reader tools' history gone, Ctrl+Z no longer runs two undos
or puts back stale CKEditor bookmark spans; and one order covers everything, so undo never puts back a box's
text over a table or picture change that came after it.
