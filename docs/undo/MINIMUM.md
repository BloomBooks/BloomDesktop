# The minimum that matches today's undo

The smallest version of the page history (APPROACHES.md, recorded mutations with the API of section
11.8) that undoes everything that today's undo undoes. It is the pull request "Replace the old undo
with the page history" of REVIEW-PLAN.md, built on the three before it (the engine, the markup filter,
and input, keys and page readiness), which add code that nothing calls. This one switches the Edit
tab over. The pull requests after it add to it one component at a time. Section numbers refer to
APPROACHES.md, and "issue N" to Part 1 of CODE-WALK.md.

"Today" and "today's undo" mean master before this pull request.

"Matches" means three things:

- every action that today's undo covers can be undone and redone, one step per action, from Ctrl+Z,
  Ctrl+Y and the Undo button;
- no undo damages the page, including undos of actions that today's undo leaves alone (rules 1 and 4
  of section 3.0.2 hold from the start);
- redo is not lost to things that don't lose it today, such as moving the mouse.

Some history that survives today is lost in the minimum, because the minimum ends the history where
a later pull request records more. Those losses are listed at the end.

## What today's undo covers

From `bookEdit/undo/` (PR 8387), `ImageUndoManager.ts`, the inline images (BL-16822) and their
callers.

| Action | Today's mechanism | Undo offered when |
|---|---|---|
| Typing and formatting in a text box | CKEditor's undo plugin, one history per box, 20 steps | always, for the box that has the focus |
| Typing while a reader tool is open | CKEditor's undo for Ctrl+Z and Ctrl+Y; the reader tools' own history of each box's HTML and caret (`readerToolsModel.ts`) for the Undo button | a reader tool is active |
| Table operations | `bloom-table`'s history, as entries on the one stack (`undo/tableUndo.ts`) | not in Change Layout mode |
| Replacing a picture: image chooser, gallery, paste, AI image editor (`changeImage`, `changeImageByElement` in `bloomEditing.ts`) | the image undo restores `src`, `data-copyright`, `data-creator`, `data-license`, the crop, the box's geometry and its rotation | that picture is selected |
| Rotate right and flip | the image undo | that element is selected and its text hasn't changed since |
| Rotating a canvas element by dragging | the image undo; text boxes too | as for rotate right |
| Removing a background picture, back to the placeholder | the image undo | that picture is selected |
| Inline images: insert, delete, dock, move, resize, crop, flip, change picture, the commands on their toolbar | an entry on the one stack for each operation, holding the markup of the block's pictures in every language (`inlineImages.ts`); `inlineImageInteractions.ts` takes Ctrl+Z when the stack's next undo is one of them, and Ctrl+Y when the box's CKEditor has no redo of its own | always from the Undo button; from Ctrl+Z while no typing is newer |
| Change Layout mode | origami's own copies of `.marginBox`, with its own redo | the mode is on |

The one stack (`undo/UndoStack.ts`, 50 entries) lives in the page frame and starts empty with each
page load. It holds the table and inline image entries and asks the other mechanisms in turn, with a
change counter deciding between an entry and CKEditor's typing (`undo/changeOrder.ts`); typing in a
box discards what the stack could redo (`undo/ckeditorChanges.ts`). Only the Undo button goes through
it: the button posts `editView/topBarButtonClick`, C# runs `topBarButtonClick` in the page frame,
which calls `handleUndo`, and C# polls `canUndo` on a timer. Ctrl+Z is taken in the page frame by
origami, the inline images or CKEditor, each with its own history. There is no Redo button.
`BloomField.PreventRemovalOfSomeElements` counts each box's protected elements and reverses a
deletion that removes one with the browser's own `execCommand("undo")`. `runUndoable` exists but
nothing calls it, so deleting a canvas element is not undoable. Moving, resizing or cropping a canvas
element, adding one, styles, dialogs, Talking Book, video, games and book settings have no undo today.

## What this pull request builds

Items 1 to 3 and 6 are the earlier pull requests' code, which this one starts using.

1. **The history itself**: `pageHistory()` for each page load, the `MutationObserver`, `doTransaction`
   and `beginTransaction`, undo and redo with the check of rule 4, and `clearUndoAndRedo()`. Rule 1
   counts only a net change, so that a `classList.add` of a class that is already there doesn't clear
   redo. A change to a node outside the page counts only if undo could need that node
   (`nodesThatUndoMayNeed`). The history writes each step that it makes, undoes or redoes, and each
   clear with its reason, to Bloom's log, by its description's l10n key.
2. **The filter of rule 6** (`editTimeMarkup.ts`), so that undo neither records nor puts back what the
   save strips, nor what Bloom works out again or what only marks a selection. This pull request adds
   `bloom-inlineImage-selected`, which `removeEditingDebris` takes off the selected inline image.
3. **Rule 2 for input** (`pageHistoryInput.ts`): a real click or key press in the page frame that
   changes the page opens a transaction with the general description, `EditTab.Undo.Edit`, and
   typing, deleting, paste, cut and drop name their own steps. Typing breaks into steps as Chromium's
   own undo does (TYPING-STEPS.md). Table operations, canvas element moves, resizes, crops and
   rotations, and every inline image gesture and toolbar command, are undoable through it without
   wrapping.
4. **Input elsewhere that changes the page with no transaction of its own ends the history** (`endHeld`
   in `pageHistoryInput.ts`). In this pull request no control in the toolbox, the top bar or a dialog
   has a transaction of its own, so a change that one of them makes ends the history, apart from
   these, which are made outside the history (`doOutsideHistory`) and keep it:
   - Talking Book's playback highlighting, and what it writes when it opens on a page, apart from
     its sentence split, which joins the latest step (`addToLatestTransaction`);
   - Show Invisibles, which also puts the characters back before an undo or redo that comes while
     the keys are held;
   - origami's own undo and redo in Change Layout mode;
   - the reader tools' removal of markup that earlier versions of Bloom saved;
   - the picture tooltip's `title` and `data-title`, and the temporary id that C# gives a picture to
     find it.

   Bloom's delayed reactions that go through `addToLatestTransaction` (the toolbox's markup pass, the
   fit of a canvas element to a new picture, an inline image's shape when its picture loads,
   Talking Book's sentence split) join the latest step even when a press has opened a transaction
   that has changed nothing yet.
5. **Explicit transactions for the covered actions that rule 2 can't see.**
   - The bodies of `changeImage` and `changeImageByElement`, which C#, the image gallery and the AI
     image editor call, each in an async `doTransaction` that ends once no page content delay is
     pending (`doTransactionUntilPageSettles` in `bloomEditing.ts`), so that the refit after the
     picture loads can't join the user's next input. If the delays never clear, the transaction ends
     after 4 s. The step is described as `EditTab.Image.ChangeImage`. Each picture that one AI image
     editor commit swaps on the current page is a step of its own.
   - The JavaScript functions that C# calls for text paste and cut (`pasteClipboard`, and the cut case
     of `topBarButtonClick`), each in a `doTransaction`, so that a paste is its own step as it is
     today.
   - Adding a canvas element and its layer moves, each in a transaction with the general description
     (`doAddAsUndoStep` and `doAsUndoStep` in `CanvasElementManager.ts`), so that an add at the end of
     a drag from the Canvas tool is a step.
6. **A moment when the page is ready** (`pageReadiness.ts`). The history starts when the page loads;
   until the page is ready, a change made outside any transaction is not recorded. The toolbox says
   when the running tool's two `newPageReady` passes for the page have run (`useToolLifecycle.ts` and
   `newPageReadyPasses.ts`), and the page waits for that too, so that Talking Book's split and the
   Games tool's shuffle are part of the page as the user found it.
7. **Requests to C#: everything that isn't known to be harmless ends the history.** An axios request
   interceptor in `utils/bloomApi.ts` clears the history before any request to C# whose method is not
   GET, HEAD or OPTIONS and whose path is not on `kRequestsThatKeepThePageHistory`: requests that
   change nothing that the page names, or only write a file under a new name. A request that ends the
   history writes "Page history: cleared by request" and its path to Bloom's log.
   `aiImageEditor/commit` is not on the list: a commit can delete an `ai-image` file that it replaced
   on another page (`DeleteSupersededAiImageFiles`), and an undo after it could put back a page naming
   that file. The commit's request goes out before the swaps on the current page, so those swaps are
   undoable and the history before them is gone. Requests sent with jQuery's `$.ajax` pass by the
   interceptor.
8. **The reloads stay.** Every save still reloads the page, so a save ends the history as it does
   today.
9. **The old histories are switched off and deleted**: CKEditor's undo plugin
   (`config.removePlugins`), the image undo (`ImageUndoManager.ts`), the reader tools' history and
   redo, the inline images' stack entries, their Ctrl+Z and Ctrl+Y handler and their resets of
   CKEditor's histories, and the one stack with its providers and change counter (`bookEdit/undo/`).
   `bloom-table` still keeps its history inside the library, but nothing in Bloom reaches it. With the
   undo plugin gone, CKEditor fires no `change` event, so Bloom fires it from each box's `input`
   events. Origami keeps its own undo inside Change Layout mode, and entering or leaving the mode ends
   the history.
10. **The keys and the button** (`pageHistoryKeys.ts`): the Undo button keeps its route through C#
    (`handleUndo`, `canUndo`), and carries `data-runs-undo`, so its press opens no transaction. Ctrl+Z,
    Ctrl+Y and Ctrl+Shift+Z are taken in the capture phase of the page frame's `keydown`, ahead of
    CKEditor, the reader tools and the inline images; in Change Layout mode they are left to origami.
    `PreventRemovalOfSomeElements` becomes a `beforeinput` handler on every box that refuses an edit
    whose range covers a `.bloom-preventRemoval` element, such as an inline image, instead of calling
    the browser's undo; it looks at the box at each edit, so it protects an inline image inserted
    after the box was set up. The keys that undo and redo don't schedule the markup pass, and the
    toolbox abandons a pass that is already scheduled before an undo or redo.
11. **`onAfterUndoOrRedo` for the components that the covered actions touch** (`listenToPageHistory`
    in `bloomEditing.ts`):
    - the canvas element manager notes the canvas elements and drops pending fits before an undo, and
      after it re-reads the page, draws Comical's bubbles again and selects the canvas element that
      the step changed (`updateAfterUndoOrRedo`);
    - the reader tools paint their highlights again from the restored text, without writing to the
      page;
    - Talking Book stops playback and puts back the spans that playback made before an undo, and
      after it drops the records that its visibility observer has not handled yet, paints its
      highlight again, and builds the playback-order arrows again while they show;
    - the selected inline image gets its handles and toolbar again where the undo left it, or loses
      its toolbar when the undo took it out of the page (`updateInlineImagesAfterUndoOrRedo`).
12. **Code that writes the page with no click or key press**, changed so that moving the mouse or
    selecting doesn't clear redo: the hover buttons get `bloom-ui`, and the picture tooltip is written
    outside the history.

## Left to the pull requests after this one

Each is in REVIEW-PLAN.md's table, and each removes a loss from the list below.

- Canvas commands, the Canvas tool's controls, the colour picker and layer moves as named steps, and
  an async canvas command no longer joining the step before it.
- The Format dialog, Book and Page Settings, the link grid, Original Title, Set Destination and Set Up
  Hyperlink, which stop ending the history.
- The other toolbox tools: Games, Music, Motion, Image Description and the Impairment Visualizer.
- Talking Book's recording-mode switch, its playback-order arrows and Adjust Timings' OK as named
  steps.
- Video and Sign Language: choosing a video without a reload; Record, Import and Delete as steps.
- The AI image editor: one step per commit, and a commit that deletes no file, so that it can keep
  the history.
- A Redo button, and Show Page History.
- Later still: removing the reloads (PR 8209), `addChangeOutsidePage` and the C# side of section
  11.8.4, the file rules of section 11.8.5, StyleEditor's `<style>` text, and CSS highlight ranges for
  playback (issue 7).

## Where this pull request is worse than today

- **History is lost on a page change from the toolbox, the top bar or a dialog that item 4 does not
  keep outside the history**: a control in the Canvas tool, the colour picker, the Format dialog, Book
  and Page Settings, Motion, Music, Games, Image Description's checkbox, Sign Language, Talking Book's
  recording-mode switch, Record, Clear and Import, and changing the Leveled Reader's level. Today
  CKEditor's history of a box survives these.
- **History is lost on every request that isn't on item 7's list**, including an AI image editor
  commit.
- **A change that a tool makes after an `await`** (Sign Language's recording, a hyperlink from Set Up
  Hyperlink, some canvas commands) joins the step before it, so undoing that step undoes both.
- **History is lost when an undo is refused** because a change made outside the history altered a
  value that the step holds. The refusal empties the undo stack.
- **Playback still writes the page** (issue 7), outside the history, and typing during a paused
  playback is erased, as it is today.
- **Ctrl+Z undoes the latest change on the page**, not the latest change in the box that has the
  focus, as CKEditor's history for each box does today, and a picture change is undone whether or
  not the picture is selected. An undo of an inline image operation also undoes, in order, any typing
  done after it, where today's stack entry left that typing alone. Users will notice these.
- **Undo waits while a transaction is open**: during a drag, and after a picture change until its
  page content delays clear, for up to 4 s.
- **Every step but typing, paste, cut, drop and a picture change has the general description.**
  Nothing shows the descriptions yet.

Gains over today: canvas element moves, resizes, crops, rotations, additions and deletions, and every
other page-frame action, are undoable; tables and pictures have redo; Ctrl+Z and the Undo button do
the same thing; with a reader tool open, one Ctrl+Z is one undo; and one order covers everything, so
undo never puts back a box's text over a table or picture change that came after it.
