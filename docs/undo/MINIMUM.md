# The minimum that matches today's undo

The smallest version of the page history (APPROACHES.md, recorded mutations with the API of section
11.8) that undoes everything that today's undo undoes. Later steps add to it one at a time. Section
numbers refer to APPROACHES.md, and "issue N" to Part 1 of CODE-WALK.md.

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
   placeholder observers after every undo, issue 8) doesn't clear redo.
2. **The filter of rule 6**, so that undo neither records nor puts back what the save strips: the
   declarative rules of section 3.7 in one module (`editTimeMarkup.ts`), tested value by value
   (class token by token, style property by property), and CKEditor's tracked filling character
   taken out of text values. On master the save strips the live page and then reloads it, so the
   history never meets what the save strips; the save path takes up the same module when PR 8209
   stops the reloads, and until then the module's rules are checked against master's save path.
3. **Rule 2 for input in the page frame.** A real click or key press in the page frame that changes
   the page opens an "Undo edit" transaction. A drag is one transaction from pointer down to the
   event that ends it (with mouse events, `buttons === 0` at the next move, issue 11). Typing in one
   box breaks into steps where CKEditor's plugin breaks them today: every 25 keystrokes, when the
   kind of input changes, and around commands; like CKEditor, it ignores IME key presses (keyCode
   229) and doesn't count a paste or a drop as typing. With rule 2, table operations and canvas element
   rotation are undoable without wrapping, and so are canvas element moves and other page-frame
   actions that today's undo leaves alone.
4. **Input elsewhere that changes the page ends the history.** A click or key press in the toolbox
   frame, the top bar or a root-window dialog that changes the page, with no explicit transaction
   open, clears the history instead of opening an "Undo edit" transaction. The Undo button is
   exempt: its click changes the page by undoing. This takes the place of
   the open question of which frame's clicks count (issue 12), and it makes the components whose
   state the minimum doesn't restore (Talking Book, sign language, music, Games, Motion, the reader
   tools' controls) boundaries without touching them. Opening a tool that changes the page (Talking
   Book's spans, Motion's rectangles, issue 3) ends the history the same way.
5. **Explicit transactions for the covered actions that rule 2 can't see.**
   - The bodies of `changeImage` and `changeImageByElement`, which C#, the image gallery and the AI
     image editor call, each in a transaction kept open with `beginTransaction` until the picture's
     `load` reaction has refit it, so that the refit can't join the user's next input (issue 1). The
     AI image editor's swaps in one commit go in one `doTransaction` around them all.
   - A `doTransaction` at the canvas command routing point (`control.action(...)`), so that a command
     from the Canvas tool (removing a background picture, rotate, flip) is a step and not a boundary.
   - The JavaScript functions that C# calls for text paste, cut and Paste Image (`editView/paste`,
     the cut case of `topBarButtonClick`, `editView/pasteImage`), each in a `doTransaction`, so that
     a paste is its own step as it is today, not joined to the typing before it.
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
   toolbox's second `newPageReady`, 600 ms later, has run (issue 3). This keeps the background picture conversion (BL-16870) and the Games
   tool's shuffle out of the user's first step.
7. **Requests to C#: everything that isn't known to be harmless ends the history.** The interceptor
   clears the history on any POST that is not on a short list of requests that change nothing that
   the page names, or only write a file under a new name: the class a requests of CODE-WALK Part 3,
   `imageGallery/imageGalleryResult`, `editView/pasteImage`, `editView/paste`,
   `editView/topBarButtonClick`, `aiImageEditor/commit`, `audio/copyAudioFile`, `fileIO/chooseFile`.
   `aiImageEditor/commit` is on the list only once `DeleteSupersededAiImageFiles` is gone, with
   `CleanupUnusedSupportFiles` covering `.jpeg` and `.webp` instead. Today, undoing a commit that
   replaced an earlier `ai-image*` picture can put back a file that this function has just deleted
   (inferred, not tried; issue 4). This inverts
   `kRequestsThatEndTheHistory`: a request that nobody has classified loses history instead of
   risking a file or C# state that undo can't put back. The minimum needs no `addChangeOutsidePage`,
   no refusal to overwrite in C#, and no complete list of the requests that end the history
   (issues 4 and 5).
8. **The reloads stay.** Every save still reloads the page, so a save ends the history as it does
   today, and nothing that the save does to the live page, the page div or the book folder matters yet
   (issue 6). Still to decide: what an undo does while a save is in flight (`SavePending`,
   `EditingStateMachine.cs:14`), which today the save can overtake (retire-ckeditor PLAN.md:657-663).
9. **The old histories are switched off**: CKEditor's undo plugin, `bloom-table`'s history, the image
   undo, the reader tools' history and redo, and the one stack with its providers and change
   counter. The reader tools' rewrite of a box is recorded like any other
   change and joins the typing that caused it (section 5.3). Origami keeps its own undo inside Change
   Layout mode, and entering or leaving the mode ends the history (section 11.6).
10. **The keys and the button**: the Undo button keeps its route (C# calls `handleUndo` and polls
    `canUndo`), which now answers from the page history. Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z are taken in
    the capture phase of the page frame's `keydown` (section 8.2), ahead of CKEditor and the reader
    tools, and the other frames forward them. In Change Layout mode the handler leaves the keys to
    origami, as `undo/redoKeyBinding.ts:66` does today. `beforeinput` with `historyUndo` or
    `historyRedo` is cancelled, and `PreventRemovalOfSomeElements` becomes a `beforeinput` handler
    that refuses a deletion over a `.bloom-preventRemoval` element instead of calling the browser's
    undo. The keys that undo and redo don't schedule the
    markup pass (section 5.3). Every step shows the general description.
11. **`onAfterUndoOrRedo` for the components that the covered actions touch**: the canvas element
    manager (active element, handles, comicaljs; not through `refreshCanvasElementEditing` as it
    stands, because it reruns `SetupElements`, which adds the edit key handlers to `document` again
    each time), `bloom-table`'s selection UI (case 7.8), the reader
    highlights (already repainted after an undo), and Talking Book's visibility observer, which must
    not start a markup pass after a restore (issue 8).
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
- Input in the toolbox and dialogs as undo steps, one component at a time, each with its
  `onAfterUndoOrRedo` work (issue 8): Talking Book, sign language, music, Games (with a way to stop
  recording during Play, issue 9), Motion, the reader tools' controls.
- StyleEditor writing `<style>` text (rule 5, case 7.5), which makes format changes undoable.
- Descriptions, wrapping actions at their routing points, and nested transactions.
- `doOutsideHistory` and `addToLatestTransaction` beyond item 12, and the development log of
  section 10.7.
- CSS highlight ranges for playback (issue 7).

## Where the minimum is worse than today

- **History is lost on any page change from the toolbox, the top bar or a dialog**: opening Talking
  Book, Motion or Games, recording, Play, the Canvas tool's controls other than its commands. Today
  CKEditor's history of a box survives these.
- **History is lost on every request that isn't on item 7's list**, including canvas element
  duplication with audio (`fileIO/copyFile`, which the interceptor can't tell apart from Import).
- **Playback still writes the page** (issue 7): Play ends the history (item 4), and typing during a
  paused playback is erased, as it is today.
- **Ctrl+Z undoes the latest change on the page**, not the latest change in the box that has the
  focus, as CKEditor's history for each box does today, and a picture change is undone whether or
  not the picture is selected. Users will notice both.

Gains over today, without any extra work: canvas element moves, resizes, crops, additions and
deletions, and every other page-frame action, are undoable as "Undo edit" steps; Ctrl+Z and the
Undo button do the same thing; with the reader tools' history gone, Ctrl+Z no longer runs two undos
or puts back stale CKEditor bookmark spans; and one order covers everything, so undo never puts back a box's
text over a table or picture change that came after it.
