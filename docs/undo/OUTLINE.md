# Undo in Bloom: outline

Status: outline for discussion (2026-10-14). This file lists every topic we need to think through
before we write a plan. Items marked **(?)** are open questions, gathered in §14. Items marked
"agreed" are decided, provisionally in the sense of §1 goal 7; the rest of the requirement lists
are drafts to argue about.

Today's undo implementation is deliberately left out of this document.

Scope: undo in the Edit tab only. Actions in the Collection and Publish tabs also change state,
but undoing them is not being considered.

---

## 1. Goal

Draft statements to argue over:

1. **One mechanism.** Every change to undoable state goes through one central system. No
component keeps its own undo stack.
2. **Undo costs a component author almost nothing.** Writing a new canvas element type, tool or
dialog should need zero or a few lines of undo code. Today a component can spend a third of its
code on undo; the goal is to stop that.
3. **Users get what they expect.** Undo undoes the last thing the user did, whatever kind of
thing it was, in the order they did it. Redo reverses that.
4. **Undo never damages a book.** A wrong or partial undo is worse than no undo. Any design that
could lead to corruption must be rejected outright. We can't just rely on extensive testing to make
sure we didn't corrupt anything. So undo and redo only ever return the page to a state that it was in
(APPROACHES §3.0).
5. **The design does not depend on CKEditor's behaviour**, whether we patch it, replace it, or
retire it. Whether CKEditor will ever be removed is uncertain, so approaches are judged on their
merits apart from CKEditor, with the cost of making CKEditor fit counted separately. If one
approach is far better except for CKEditor, we choose it and adapt CKEditor (goal 6); we do not
settle for a lesser approach because of CKEditor.
6. **We change a library when that is simpler than adapting around it.** This applies to
CKEditor (see §2.9) and equally to `bloom-table`, `comicaljs`, and any other library.
7. **Behaviour decisions stay cheap to change.** UX is settled by trying it and watching people,
not in this plan. The decisions here about which kind an action is (§4.0) are provisional; what
the plan must guarantee is that moving an action to another kind later is cheap. The bad
outcome to avoid: "we didn't foresee that X should be undoable, and now it is expensive to
retrofit." Example: some toolbox actions, even ones that change state outside the page, may
need to be undoable.
8. **Arrives in small steps.** The change ships as many small PRs, with Bloom shippable after
each, and the steps can be cut along several lines: which component moves, which of a
component's actions move, and which features come at all. Redo can come later: Bloom has no Redo
button, so few users know Ctrl+Y exists. While actions are still moving, a step may lose history
in cases not yet moved, or show an action not moved yet with the general description, but never
damages a book (goal 4).

How far back undo reaches: the current page only (§5), with the boundaries in §4.0.

---

## 2. Kinds of state

Everything a user action can change. 

### 2.1 Content of the current page (page DOM)

- **Text in `.bloom-editable`**: typing, deleting, paste, drag-and-drop of text, autocorrect or
IME composition.
- **Text formatting**: bold, italic, underline, superscript, text colour, remove formatting,
hyperlinks.
- **Text box properties**: style chosen for a box, box-level classes and attributes.
- **Translation groups**: the same box in several languages; changes to which languages show.
- **Images**: choose or replace an image, crop/pan/zoom, image description text, AI image edits.
- **Canvas elements**: add, delete, duplicate, move, resize, rotate (?), change z-order, change
bubble style or tail, change element type, background image.
- **Video**: insert, replace, trim (processing writes a file).
- **Widgets** (HTML activity elements).
- **Tables**: add/remove rows and columns, cell content.
- **Origami**: split a pane, merge panes, move a split.
- **Games**: targets, draggable items, correct answers, game settings, theme.
- **Quiz pages**: questions, answers, which answer is correct.
- **Attributes set by toolbox tools**: motion tool settings, music settings, talking book
recording mode and highlight settings, sign language video, reader-tool markup.
- **Talking book markup**: sentence spans inserted into text, recording timings.

### 2.2 Structure of the book (more than one page)

- Add page, delete page, duplicate page, duplicate a page many times, move a page.
- Change a page's template or layout.

### 2.3 Book-level data edited from a page

- Fields stored once for the whole book and shown on several pages (title, credits, ISBN,
copyright...). Editing one copy changes the others, but only when the user switches pages, which
is a boundary (4.0). Until then the edit is ordinary content of the current page.
- Copyright and license.
- Style definitions the user changes. They end up in the book's stylesheet and affect every page,
but until the page is saved they live in the current page (§16), so for undo they are ordinary
page content.

### 2.4 Book settings

- Page size and orientation, cover colour, xmatter choice, appearance/theme settings, languages
of the book, topic, features recorded in the book's metadata.
- Changing them is a boundary, with undo as a nice-to-have (§4.0, §4.2).

### 2.5 Files on disk

- Images added or replaced, audio recordings (including re-recording over an existing clip),
videos, music files, image metadata stored inside image files.
- Cleanup of files no longer referenced: if undo brings a reference back, the file must still
exist. Today a replaced image keeps its old file until a book is selected or the page
orientation changes, which is a boundary (§16).

### 2.6 Collection-level state

- Collection settings, collection membership of books, book order. Not undoable (§4.3).
- Leveled and decodable reader word lists and stages: stored for the whole collection, but edited
while working on a book.

### 2.7 UI State (User)

- Caret position and text selection.
- Which canvas element is selected.
- Scroll position, zoom level.
- Which page is current, which toolbox tool is open.
- Agreed: undo restores, but never records, these:
  - Undo puts the caret or selection back where it was just before the undone change; redo puts
  it where it was just after (as ProseMirror, Slate, CodeMirror and CKEditor 4 do).
  - For a change that isn't text, undo and redo select the thing changed (undoing a canvas
  element move leaves that element selected).
  - Undo and redo bring the change into view if it is scrolled off or otherwise hidden.
  - A change only to the selection does not clear redo (as in Excalidraw and tldraw).

### 2.8 Derived UI Status

- overflow markers, thumbnails, hint bubbles, editor chrome added to the DOM while  
editing and stripped on save.
- (?) Requirement candidate: everything here can be recomputed from content, so undo never
records it, and any capture of state ignores it.
- On the `flowText` branch the overflow marker (`bloom-overflowStart` span) is inserted into the
user's text and saved, so it is no longer derived state there (§17).

### 2.9 CKEditor

This is its own category because it is a major part of what a user will actually be undoing. We are using this as a library, but that doesn't actually mean that we can't modify it. Of course, if we can adapt it into our system without modifying it, then that simplifies things. But the keyword here is simplify. If we add a ton of complexity in order to avoid modifying it, then we aren't actually simplifying. 

- CKEditor has its own undo stack.
- CKEditor also changes the DOM itself, when it attaches to a text box and when it filters a
paste. The user did not ask for these changes (see "Made by Bloom on its own" in §3).

### 2.10 Low level

- The browser's native undo stack for `contenteditable` and `<input>`/`<textarea>`.
- React component state that mirrors the DOM.

### 2.11 things in dialogs

Things that have a Cancel buttons may have internal undo but as far as Bloom is concerned, we should be able to handle their impact on our state, when the user clicks OK, as a single undoable event.

- our AI image editor
- our image gallery dialog
- settings dialogs
- leveled and decodable reader dialogs
- copyright and license
- etc.

Cases that test the "one event on OK" principle:

- Dialogs that preview changes live on the page while open and revert them on Cancel. The
intermediate changes must not be recorded.
- One OK can change several kinds of state at once: the AI image editor writes a file and changes
the page; copyright and license change book-level data; book settings can re-lay out every page.
The single event has to span 2.1, 2.3, 2.4 and 2.5.
- The toolbox has no OK. Its tools change the page as the user works (see 2.1).

### 2.12 Parts of Bloom in external libraries

- tables (`bloom-table`, a BloomBooks repo, pinned to a dist tag). Keeps its own undo history
today (§17.3).
- speech bubbles and tails on canvas elements (`comicaljs`, also ours). It keeps bubble state in
JS objects as well as in the DOM, so it is also a 2.14 case; the JS state can be rebuilt from
the DOM (§16).

### 2.13 things in the c# variables

- The whole book's HTML, held in memory; page saves are merged into it.
- The book's metadata.

### 2.14 things in javascript variables

- Which canvas element is selected: also in the DOM, as `data-bloom-active="true"`, and saved
(§16). Agreed: it is view state that happens to be saved; undo ignores it.
- `comicaljs`'s list of bubbles.
- State held by toolbox tools.

### 2.15 Listeners and connections

- Event listeners attached to elements on the page. Replacing an element with an earlier copy of
it drops its listeners.
- Observers and timers (`MutationObserver`, `ResizeObserver`, pending timeouts) watching or acting
on the page.
- Open connections between the front end and C#, such as websockets.
- After undo or redo, every feature on the page must work as it did before (§4.4 item 4). Whether
that means restoring these or setting them up again is an implementation question.

## 3. Kinds of change

A classification of user actions, because the central system has to handle each shape.

- **Discrete**: one click, one transaction (delete a canvas element).
- **Continuous**: dragging, resizing, slider movement, typing.
  - Dragging, resizing, a slider: one transaction per gesture, from mouse down to mouse up (agreed).
  - Typing: we do not make our own rules. Users carry subconscious expectations from a decade of
  typing in the editors they use every day, so typing undoes in the steps those editors use.
  We may well leave this to CKEditor or to the browser's own `contenteditable` undo and not
  decide typing steps ourselves (§2.9, §7). Only if we take it on do we need to find out what
  Chrome, Microsoft Word and Google Docs do, and reason about any differences.
- **Compound**: one action changes several kinds of state at once (delete a canvas element that
has a recording). One transaction, never several (agreed).
- **Made inside a dialog**: one transaction per OK (§2.11); changes a dialog previews live while open
are not recorded.
  - StyleEditor format dialog: applies each change live and has no Cancel. Provisionally, one
  session of the dialog is one transaction.
- **Asynchronous**: an operation that runs in the background while the user can keep working on
the page, and changes state when it finishes. Not undoable. An operation that blocks the page
until it finishes (behind a progress dialog, say) is not in this category; it is an ordinary
action. Decided: Bloom never changes page content in the background while the user keeps
working, except values it recomputes from other content (link-grid thumbnails). A job that does
is a bug to fix: it blocks until it is done, or it finishes before the next input is handled.
Follow-on jobs such as the markup pass after typing are finished before the next input, so their
changes land in the transaction that started them. To check against this (§16): Talking Book
audio import, and short jobs registered with `wrapWithRequestPageContentDelay` (image sizing,
pasting a canvas element).
- **Made by C#**: changes performed in the back end and then shown in the page.
- **Made by Bloom on its own**: normalising or migrating a page on load, splitting text into
sentences for talking book, auto-fit layout, CKEditor's own changes to the DOM. Not undoable
(agreed for: Talking Book's sentence spans added when the tool opens, link-grid thumbnails,
which are derived state, image sizing). They are untracked changes (§4.0, kind 4): never a transaction of
their own, but undone along with a user change they follow from (§4.4 item 3).
- **Across frames**: the page, the toolbox and the page list are separate frames; a change can
start in one and land in another.

---

## 4. Requirements

### 4.0 Not every action needs undo

There is a tendency to assume things need to be undoable when they do not. Some user actions are
boundaries: they end the undo history instead of being transactions in it. Every action falls into one
of four kinds:

1. **Undoable**: recorded as a transaction in the history.
2. **Boundary**: clears the history; Undo is unavailable immediately after it.
   - Boundaries Bloom causes, not the user: with flow text, typing can hand the caret to the
    next page (a real page switch), and a refit can add pages, delete the current page, or
    navigate (§17.1). These clear the history like any boundary; acceptable under §4.4 item 2
    (undo may work poorly with flow text, but must not damage the book).
   - Agreed: switching to another page, adding a page, deleting a page (Bloom already asks the
    user to confirm in a dialog), moving a page, duplicating a page, leaving the Edit tab.
   - Agreed as the minimum, with undo as a nice-to-have (4.2):
     - changing book settings. It can re-lay out the current page, so it can't be a view
      change. A user may reasonably click OK, see the result, and want it back.
     - changing the page's layout or template, for the same reasons.
     - re-recording audio over an existing clip. As a boundary, undo never has to keep old
      audio files.
   - Not a boundary: OK in the AI image editor is undoable, the same as replacing an image (4.1).
3. **View change**: not recorded, and the history continues past it (zoom, scrolling, opening a
tool; the markup Talking Book adds when opened is an untracked change, kind 4). The name follows VS Code, whose editor calls scroll position and cursor "view state"
(`saveViewState`, `restoreViewState`) and keeps it out of undo history. Allowed only when the action changes nothing an earlier
transaction depends on; otherwise it must be a boundary. One exception: a user change kept outside
the history on purpose (for now the Format dialog, Games same-size, sign-language trim, Music) is
neither recorded nor a boundary; the history continues, and a later undo whose transaction needs the
earlier value is refused, which empties the undo history. Window resize is a view change: Bloom has no
resize listener on the page (§16); the refits that `ResizeObserver`s make to elements are
untracked changes (kind 4).
4. **Untracked**: a change Bloom makes to content on its own, not the user. Never a transaction of its
own and never clears the history. If it follows from a user's change, it is undone along with
that change; if it is a value that Bloom recomputes from other content, it survives (§4.4 item 3); any
other untracked change joins the latest transaction. Example of surviving: the
user types a sentence, a link-grid thumbnail arrives, the user invokes undo; the sentence goes
away and the thumbnail stays. The name follows Yjs, whose `UndoManager` records only changes
from `trackedOrigins` and leaves others in place. Agreed members are listed in §3 under "Made by
Bloom on its own". Undoing the user's change without reverting a later independent change is the
hard part for the mechanism (§15, point 2). A user action that no code has wrapped yet is not
untracked. Agreed: when a real click or key press changes the page and no code has opened a
transaction for it, Bloom opens one with the general description ("Undo edit", §4.1), so the
action is its own undo step from the first release; wrapping it later only gives it its own
description and better step boundaries (APPROACHES §3.0, rule 2). A change made outside any input,
such as Bloom's reaction a moment after an action (a timer, a picture's load event, code after an
await), finishes before the next input (§3, Asynchronous) and joins the transaction of the action
that caused it, which is the latest one (APPROACHES §3.0, rule 1).

Draft test for which kind an action is: it needs undo when the user can't easily get back by
hand, especially when it can happen by accident (typing over a selection, Delete with a canvas
element selected, a drag that lands wrong). A deliberate, visible action that is easy to reverse
by hand, or that already asks for confirmation, can be a boundary.

### 4.1 Must have (draft)

- Undo and redo work on the current page across text and non-text changes, in one order.
- Typing undoes in the steps users expect from the editors they use every day (§3).
- Caret, selection and scroll position are restored after undo and redo as agreed in §2.7.
Without this, the next keystroke after an undo can land in the wrong place.
- Undo of accidental or hard-to-reverse changes on the current page: delete canvas element,
replace image (including OK in the AI image editor).
- Undo of a compound action undoes all of it, never part.
- Automatic changes by Bloom never appear as separate undo steps the user has to step through.
- Undo and the page-save pipeline agree: saving never loses or corrupts what undo depends on, and
undo after a save still works.
- Undo inside a dialog field or toolbox input undoes that field, not the page.
- The user can read what undo and redo will do before invoking them ("Undo Move Image"). So
every transaction can carry a description, and those are localizable strings. Giving each action its
own description is a nice-to-have (§4.2); a transaction without one shows a general description
("Undo edit" rather than "Undo making that text bold italic").
- The undo/redo commands' enabled state is correct.

### 4.2 Nice to have (draft)

- Each action supplies its own description for the undo and redo commands ("Undo Move Image").
This may prove too many strings to be achievable; actions without one show a general
description (§4.1).
- Undo of book settings changes: the user clicks OK, sees the result, and wants it back. Without
this, changing book settings is a boundary (4.0). If it is undoable, undoing it puts back the
layout that earlier transactions were made on, so (?) history before it might survive.
- Undo of changing the page's layout or template, for the same reason. Without this, it is a
boundary (4.0).
- Undo of re-recording audio over an existing clip. Without this, it is a boundary (4.0); with
it, the old audio file has to be kept until history can no longer reach it.

### 4.3 Not needed (draft)

- Undo across a Bloom restart.
- Undo in the Collection tab or Publish tab.
- Undo that reaches back across page navigation (page switch is a boundary, 4.0).
- Undo of adding, deleting, moving, duplicating or switching pages (boundaries, 4.0).
- History surviving a trip away from the Edit tab and back (leaving Edit is a boundary, 4.0).
- Undo of collection settings.
- Undo of non-content state on its own (scrolling, tool switching, zoom).
- Branching history, or a visible history list.

### 4.4 What the mechanism must make possible (draft)

What the mechanism must allow, without saying how it works.

1. A transaction can cover more than one text box: at minimum the whole translation group (inline
images change every language's copy at once), probably the whole page.
2. A transaction can include toolbox state and book-level state outside the page (§1 goal 7). Not
extended to other pages for flow text: we do not complicate undo to support flow text, and
would rather undo work poorly with it. Working poorly must still never damage the book (goal 4).
Scenario agreed: the user types, overflow is pushed to the next page, the user invokes undo.
Undo restores this page only, and the flow pass reacts to that like any other change, pulling
text back so nothing is duplicated. This puts the work on flow text: its pass must reconcile
after a restore (today it would leave the text in two places, §17.1).
Agreed: state outside the page comes in two cases. When nothing on the page depends on it (the leveled reader
level, a toolbox setting, a user preference), it may stay outside the history, with no
registration and no boundary: undo never touches it and cannot damage anything, because no page
content has to match it, and its change is not undoable, like a view change, unless it is
recorded. When the page refers to it or must match it (audio files named by sentence ids, the
metadata inside a picture file, book metadata that the page's copyright fields show, book settings
that change page content), undoing the page alone would leave the two out of step, so the action is
either undoable as a whole, with the state recorded, or a boundary (APPROACHES §3.0, rule 5).
3. Untracked changes come in two classes (§4.0 kind 4):
   - swept up: undone along with the user change they followed;
   - survives: stays when that change is undone (the history must step around it).
   The rule follows from goal 3 (undo reverses what the user did, its consequences included,
   and nothing else): a change caused by the user's change is swept up with it. Goal 4 narrows
   what survives: only a value that Bloom recomputes from other content (§2.8), which fits whatever
   state undo returns to. Any other change made outside a transaction, independent or not, joins
   the latest transaction, except some tool setup that is left outside the history (Talking Book's
   tabindex order and its "|" split markers, Motion's default rectangles), so undo only ever returns
   the page to a state that it was in (APPROACHES
   §3.0, rules 1 and 3). Because Bloom makes no background changes to page content (§3,
   Asynchronous), every such change follows from a user change or is a recomputed value, so this
   does not conflict with goal 3. So:
   - swept up: the table picture refit after a column drag; the inline image aspect ratio
   written when a chosen image loads; image sizing after a paste; the overflow marker, which
   follows from the text;
   - survives: a link-grid thumbnail's `src` arriving while the user types, which Bloom can ask
   for again from the button's link.
   - Talking Book spans added when the tool opens are caused by opening the tool, not by an
   edit, but they rewrite text that earlier transactions hold, so they can't simply survive.
   They join the latest transaction, so undoing it takes them away too (APPROACHES §5.4).
   Where an untracked change follows from a user's change on another page (flow text), Bloom's
   own reactions restore consistency, not undo (item 2).
4. After undo or redo, every feature on the page works as it did before the change (tables
editable, inline image handles present, and so on).
5. Libraries report their operations to the central history instead of keeping their own. We
can change our libraries freely to fit; no protective layer between them and undo is wanted.
Modal dialogs probably need to know nothing about undo; libraries like `comicaljs` may.
6. Every transaction can carry a description (§4.1, with its caveat).

Not requirements: whether undo's own changes set off reactions such as the flow-text pass is a
UX question (§9). How components learn that content was restored, how actions move between
kinds, and whether a gesture marks its start are implementation questions.

---

## 5. Scope of the undo history

Decided: one history for the current page, cleared by the boundaries listed in §4.0, switching
pages among them. Histories kept per page, or one for the whole book, are not needed (§4.3).
- Book-level fields edited from a page (title, credits...): an edit changes only the current
page; other places showing the field are updated when the user switches pages, a boundary. So
for undo they are ordinary page content (2.3). Style changes: the same; they live in the
current page until saved (§16), though as CSS rules, not as HTML text.
- Flow text: typing can push text onto the next page, and C# saves that page at once (§17.1).
History still covers only the current page; flow text reconciles after an undo (§4.4 item 2).
- Operations across pages (add, delete, move, duplicate a page) are boundaries (§4.0).

---

## 6. Approaches to compare

Analysed against §1 and §4 in `APPROACHES.md` alongside this file.

Every approach handles the page's DOM, and each pairs with the same small capture/restore
interface through which components supply state outside the DOM (CSS rules, toolbox settings,
book metadata). The choice is which mechanism records and replays the page's own nodes.

- **Page snapshots**: copy the page before and after each user action; undo restores the earlier
copy. Components only mark where an action starts and ends. Questions: memory, restore cost,
React components re-reading the DOM, caret restoration.
- **Snapshot diffs**: compare parts of the page (each text box, canvas element, table) as cleaned
text, and keep the before and after text of each changed part.
- **Commands**: each change is an object with do and undo. Precise, but every component writes
its own inverse logic, which is the code volume we want to get rid of.
- **Recorded DOM mutations**: a `MutationObserver` records changes and undo applies them in
reverse. Generic for anything in the DOM; does not cover files, C#, or state outside the DOM.
- **Adjusting older transactions**: later changes adjust older transactions so they still apply. Needs a
model of the whole page.

`APPROACHES.md` recommends recorded DOM mutations, with snapshot diffs second.
- (?) Should the page DOM be the single source of truth, so that after any undo every component
re-reads its state from the DOM?
- (?) What exactly does a component register? The smallest contract that works.

---

## 7. CKEditor and the browser's native undo

- CKEditor keeps its own undo stack; the browser keeps another for `contenteditable`. Script
changes to the DOM are not recorded in the browser's, so it falls out of step with the content.
- Options: patch our copy of CKEditor to report to the central system; turn off its undo and let
the central system capture typing; retire CKEditor.
- `docs/retire-ckeditor/PLAN.md` (a previous plan) touches on undo. We treat it as a  
source of inspiration (§15), and read it only after reaching our own conclusions; it should not be treated as authoritative.

---

## 8. Files on disk and the C# side

- Keep replaced or deleted files until history can no longer reach them. Today a replaced
image's old file stays until a book is selected or the page orientation changes, both outside
any history (§16).
- Re-recorded audio overwrites a file. Re-recording is a boundary (§4.0), so the old file is only
needed if its undo, a nice-to-have, is ever added (§4.2).
- Option, not decided (APPROACHES §11.8.5): an action lists the files that it will change in
place, each is moved aside before the action runs, and undo and redo swap the versions back with
renames. That would make re-recording, Import and Set Image Information undoable without changing
Bloom's file names.
- Cleanup of unused files runs when a book is selected and when page orientation changes, not on
save or page switch (§16).
- Operations implemented in C#: page add, delete and move are boundaries; image import and other
C# work done for the current page (`changeImage`): who records them?
- Constraint to confirm: the front end owns workflows, C# offers thin APIs. Does that make the
undo history a front-end object entirely?

---

## 9. User experience

- Undo and redo can each be invoked by a button or a keyboard shortcut.
- Answered (EXPERIMENTS §4): C# takes none of Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z. The keys reach the
JavaScript of whichever frame has focus, so each frame forwards them to the page's one history.
- Undo of a change the user can't currently see: undo brings it into view (§2.7). Changes on
another page can't be undone (§5).
- Undo while a dialog or popup is open: agreed, undo acts in the dialog only (a field's own
undo), otherwise does nothing; it never changes the page underneath.
- Focus in a toolbox field: undo is bounded to that field. No tool has a place to type today.
The harder case is a toolbox button that changes state, such as the leveled reader level or
decodable stage of the book. Guide: least harm. Treating it as a view change risks a user
clicking the level, invoking undo to reverse it, and silently losing their last text edit
instead. Not decided; settled by trying it with users. The design must allow any subset of
toolbox actions, including ones that change state outside the page, to be undoable (§1 goal 7).
- Nothing to undo, including straight after a boundary: agreed, the undo button is disabled and
the shortcut does nothing, with no message.
- The user can read what undo and redo will do before invoking them (4.1).
- (?) Should undo's own changes set off the reactions a user's change would? Example: changing
page size sets off a flow-text refit; would undoing it set one off too? Probably not. But for
undo of typing on a flowed page, the flow pass reacting is wanted (§4.4 item 2).

---

## 10. Developer experience

- What a new component author must do. Target: nothing, or one call around a user action.
- How a reviewer can tell a component handles undo correctly without reading it closely.
- Guardrails: a test or lint that catches state changes made outside the system.
- Documentation: one short section in `src/BloomBrowserUI/AGENTS.md`.

---

## 11. Testing

- A generic check usable for every action: do it, undo it, compare with the state before; redo,
compare with the state after.
- Unit tests for the central system itself.
- Live checks in a running Bloom; e2e only for the few journeys that matter most.
- Interaction with talking-book audio, games, and other features that store data in the DOM.

---

## 12. Performance and limits

- Size of a page's state; how many transactions to keep.
- Cost of capturing on every keystroke vs. merging typing.
- Cost of restoring, and avoiding a full page reload on undo.

---

## 13. Moving from today's code (later)

The order of steps is in `APPROACHES.md` section 10 (§1 goal 8).

---

## 14. Open questions collected

Still open as of 2026-10-14. Behaviour questions are settled by trying them with users (§1 goal
7); the design only has to keep them possible.

Behaviour:
- Reader level and decodable stage buttons in the toolbox: undoable or a view change? (§9)
- Should undo's own changes set off the reactions a user's change would (a flow-text refit after
undoing a page size change)? (§9)
- If undo of book settings is added, does the history before it survive? (§4.2)
- Rotation of canvas elements: does it exist, and is it continuous? (§2.1)
- While an undoable action is still finishing (an async `doTransaction`, such as one that awaits C#), the
user's typing joins its transaction and is undone with it (APPROACHES §3.0, rule 7). Do users
accept that?

Design:
- Everything in §2.8 can be recomputed from content: should undo never record it, and every
capture of state ignore it? (§2.8)
- Should the page DOM be the single source of truth, with every component re-reading its state
from it after an undo? (§6) Settled for CSS: StyleEditor writes its rules as `<style>` text, so
undo sees them (APPROACHES §3.0, rule 5).
- What exactly does a component register? The smallest contract that works. (§6)
- Who records C# work done for the current page, such as image import (`changeImage`)? Is the
history entirely a front-end object, given that the front end owns workflows? (§8)
- Who decides typing steps: CKEditor, the browser, or the central history? (§3, §7)
- How a reviewer can tell a component handles undo correctly, and what guardrail catches state
changes made outside the system. (§10)
- How many transactions to keep, and the cost of capturing and restoring. (§12)

---

## 15. Sources of inspiration

Existing systems whose undo designs bear on our questions. Checked against source and docs on
2026-10-14.

- **Excalidraw**: each update says how it is recorded (`CaptureUpdateAction`: `IMMEDIATELY`,
`EVENTUALLY`, `NEVER`); `EVENTUALLY` is for steps of an async process, captured with the next
`IMMEDIATELY` update. The store compares snapshots and records the inverse difference, never a
command; undo applies it as a new change. A change only to selection does not clear redo.
Close to our three kinds (§4.0) and to operations that finish later (§3).
[store.ts](https://github.com/excalidraw/excalidraw/blob/master/packages/element/src/store.ts),
[history.ts](https://github.com/excalidraw/excalidraw/blob/master/packages/excalidraw/history.ts)
- **tldraw**: `editor.run(fn, { history: 'ignore' | 'record' | 'record-preserveRedoStack' })`.
`markHistoryStoppingPoint` sets where undo returns to, so a mark at the start of a drag makes the
whole drag one step (§3). "Only store records take part in undo/redo; state held outside the
store ... does not." Component authors write nothing.
[history](https://tldraw.dev/sdk-features/history)
- **CodeMirror 6**: `addToHistory: false` changes are not dropped; older steps are adjusted so
they still apply after them. Typing and deleting within 500 ms merge, paste never does.
`isolateHistory` forces a step boundary. `invertedEffects` lets an extension add state other
than text to undo, the only code an author writes.
[history.ts](https://github.com/codemirror/commands/blob/main/src/history.ts)
- **ProseMirror** (`prosemirror-history`): stores inverted steps, groups within 500 ms, keeps
selection as a bookmark resolved at undo time. In its upload example, the image enters the
document (and the history) only when the upload finishes.
[history.ts](https://github.com/ProseMirror/prosemirror-history/blob/master/src/history.ts),
[upload example](https://prosemirror.net/examples/upload/)
- **VS Code**: one history per file; an edit across several files asks "Would you like to undo
'{0}' across all files?" and splits the step if a file changed since. `confirmBeforeUndo` asks
first (used for file operations); `undo()` may be async with a synchronous `prepareUndoRedo()`
before it. Calls cursor and scroll position "view state". Relevant to compound actions (§3) and
state outside the page (§2.5).
[undoRedo.ts](https://github.com/microsoft/vscode/blob/main/src/vs/platform/undoRedo/common/undoRedo.ts)
- **Lexical**: each step is a whole immutable snapshot of editor state including selection;
typing within 300 ms merges, paste gets its own step. Calls `preventDefault()` on the browser's
`historyUndo`/`historyRedo` so only its own history runs (§2.10).
[lexical-history](https://github.com/facebook/lexical/blob/main/packages/lexical-history/src/index.ts)
- **Slate**: steps are lists of operations plus the selection before them; undo applies their
inverses in reverse. `HistoryEditor.withoutSaving` skips recording.
[with-history.ts](https://github.com/ianstormtaylor/slate/blob/main/packages/slate-history/src/with-history.ts)
- **CKEditor 4 undo plugin** (what Bloom runs): each step is a full HTML snapshot plus selection
bookmarks; 20 steps by default; a snapshot every 25 keystrokes. `lockSnapshot`/`unlockSnapshot`
suspend recording around script DOM changes; `resetUndo()` clears it (§2.9).
[plugin.js](https://github.com/ckeditor/ckeditor4/blob/major/plugins/undo/plugin.js)
- **CKEditor 5**: changes are grouped in a `Batch` flagged `isUndoable`, `isTyping`, and so on;
a batch reverts as a whole.
[Batch](https://ckeditor.com/docs/ckeditor5/latest/api/module_engine_model_batch-Batch.html)
- **Yjs `UndoManager`**: records only transactions whose origin is in `trackedOrigins` (relevant
to "Made by Bloom on its own", §3); changes within 500 ms merge. It does not store the caret; it
gives each stack item a `meta` map where the application saves and restores it.
[UndoManager.js](https://github.com/yjs/yjs/blob/main/src/utils/UndoManager.js)
- **redux-undo**: the plain snapshot approach (`past`, `present`, `future`), with a filter that
excludes actions and `groupBy` for merging. The baseline for "Snapshots" in §6.
[redux-undo](https://github.com/omnidan/redux-undo)
- **Immer** (`produceWithPatches`): returns the change and its inverse patches from code written
as a plain mutation (§1 goal 2). [patches](https://immerjs.github.io/immer/patches)
- **W3C UndoManager draft** (2012, never shipped): proposed that the browser record DOM
mutations and revert them on undo with no author code, and an `undoscope` attribute giving an
element its own history.
[draft](https://dvcs.w3.org/hg/undomanager/raw-file/tip/undomanager.html)
- **Undo via `MutationObserver`**: undo's own writes are observed as new edits unless blocked;
records carry no timestamps; form field values are not in the DOM and are missed.
[article](https://addyosmani.com/blog/mutation-observers/)
- **Figma**: undo many times, copy, redo back to the present, and the document must not change.
[blog](https://www.figma.com/blog/how-figmas-multiplayer-technology-works/)
- `docs/retire-ckeditor/PLAN.md` (in this repo): not read yet on purpose. Compare it with our conclusions once we have them (§7).
- **The browser**: `beforeinput` with `inputType` `historyUndo`/`historyRedo` is cancelable.
Script changes to the DOM are not recorded in the native history, so it falls out of step with
the content (§2.10). [Input Events](https://w3c.github.io/input-events/)

What matters most for us:

1. Excalidraw and tldraw compute history from differences in state; the caller only says how a
change is recorded. Component authors write nothing else. The closest equivalent for a DOM is a
snapshot of the page's HTML (CKEditor 4, Lexical).
2. With snapshots, undoing past a change that was not recorded reverts it too. CodeMirror and
ProseMirror avoid this by adjusting older steps, which snapshots cannot do. So a view change
must touch only state the history does not cover, or else be a boundary. This is the rule in
§4.0.
3. Operations that finish later become one undoable change when they complete (Excalidraw,
ProseMirror's upload example, VS Code).
4. Selection travels with each step and is restored on undo, but a change only to selection
does not clear redo (Excalidraw, tldraw).
5. Lexical and Slate cancel the browser's own undo so exactly one history runs. Bloom would also
have to silence CKEditor's.

---

## 16. Facts from the code

Gathered 2026-10-14 by reading the code, without looking at today's undo implementation.
"Inferred" means not traced every step.

- **Frames**: the Edit tab is a React app (top bar) holding three iframes: `toolbox`,
`pageList`, `page`. Page navigation swaps only the `page` iframe. Dialogs such as the AI image
editor and image gallery live in the root window (`src/BloomBrowserUI/app/EditTabPane.tsx`).
- **Style changes**: StyleEditor writes rules with `insertRule` into
`<style title="userModifiedStyles">` in the page's head. They are CSSOM changes, not HTML text,
so a capture of the page's HTML does not include them unless it reads the rules. Every save
sends them to C#, which copies them into the book; other pages see them when next built, on
navigation (`StyleEditor.ts`, `bloomEditing.ts`, `Book.cs`).
- **Book-level fields**: nothing updates another copy of a `data-book` field live (inferred).
Every save, including a save without leaving the page, takes changed values from the page
(`EditingModel.cs` `NeedToDoFullSave`, `SaveStateForFullSaveDecision`).
- **Duplicate page**: Bloom goes to the new copy. **Move page**: Bloom saves, moves the page,
then goes to the moved page, which may not be the page the user was on. Both are page switches.
- **Window resize**: no resize listener on the page (inferred). But `ResizeObserver`s on
elements change saved DOM: table image cells are refit (`tableEditing.ts`),
`.bloom-centerVertically` re-centred (`bloomEditing.ts`).
- **Zoom**: changes only `#page-scaling-container`, outside the saved page. A view change.
- **Toolbox tools**: on save, the current tool strips its markup from the copy being saved.
Reader tools, Image Description, Games and Impairment Visualizer strip everything they add. The
reader tools paint their highlights with the CSS Custom Highlight API, which writes no DOM, and
do not rewrite the text box, so they change nothing that undo sees (APPROACHES §5.3).
Talking Book keeps the `audio-sentence` spans and ids it adds when it opens on a page, so
opening it can change saved content (`audioRecording.ts`).
- **Changes Bloom makes after a page loads**, saved: reordering editables in translation
groups, inline font size on translation groups, `data-text`, OverflowChecker resizing canvas
elements and adding overflow classes and padding (inferred), comicaljs's SVG. Stripped: CKEditor
classes, `bloom-ui` elements, qTip attributes, resize handles, the empty `<p></p>` added to
empty editables. C# migrations run when the book is selected, not in the browser.
- **comicaljs**: bubble spec is in the `data-bubble` attribute; JS state (paper.js objects,
active bubble) can be rebuilt from the DOM with `Comical.update` / `startEditing` (inferred
from type declarations).
- **Selected canvas element**: `data-bloom-active="true"` in the DOM, saved, and used to
restore the selection when the page reloads. So selecting a canvas element changes saved DOM.
- **Files**: replacing an image writes a new file name and leaves the old file (unless the
source is already the book's own file, which is overwritten). Re-recording audio replaces the
file and deletes the old one once the new recording succeeds. Unused files are deleted by
`CleanupUnusedSupportFiles`, which runs when a book is selected and when page orientation
changes in Edit; not on save or page switch. When the user commits, the AI image editor deletes
no file; the files that it displaced are left for `CleanupUnusedSupportFiles`, which also removes
`.jpeg` and `.webp` (`AiImageEditorApi.cs`, `BookStorage.cs`).
- **Dialogs that change the page live**: `BookAndPageSettingsDialog` (restores the page's
attributes on Cancel), `ColorPickerDialog` (restores the initial colour on Cancel), probably
StyleEditor's colour picker. The StyleEditor format dialog applies changes live and has no
Cancel; clicking outside closes it.
- **Websocket**: `BloomWebSocketServer` (C#) and `WebSocketManager` (front end). Mostly page list,
top bar, progress and error messages. Two change page content: `makeThumbnailFile-*` sets an
`img src` on link-grid pages; `chooseVideo-results` starts a video import. Most C#-driven page
changes use direct JS calls instead (`changeImage`).
- **Background operations** (§3 Asynchronous):
  - Blocking: video import (`ProcessVideoProgressDialog`); the AI image editor (an overlay over
  the workspace, inferred); image chooser (inferred).
  - Not blocking: link-grid thumbnails (`img src` set when a websocket message arrives);
  Talking Book audio import (saves wait for it up to 4 s); short DOM jobs registered with
  `wrapWithRequestPageContentDelay` (image sizing, pasting a canvas element).
  - Not checked: forced-alignment audio segmentation from the Adjust Timings dialog.
- **Saves while staying on the page**: C# can save a snapshot the browser volunteers
(`ReceivePageSnapshot`) without reloading the page. The saved book then includes Bloom's own
changes (Talking Book spans, auto-fit, `data-bloom-active`).
- **Actions that save and reload the open page**: opening the AI image editor
(`aiImageEditorPageCommands.ts`, `AiImageEditorApi.cs`), Set Image Information
(`canvasControlRegistry.ts`), leaving origami layout mode
(`origami.ts`), changing a canvas text field's type (`canvasControlTextMenuItems.ts`), topic
choice (`EditingModel.cs`), unlocking or relocking the original credits (`editableDivUtils.ts`),
OK in the book's Copyright and license dialog, Copy Page, and (inferred, not traced) changing the
leveled reader level (`EnsureLevelAttrCorrect`).
None is a boundary in §4.0, yet a history kept in the page frame would not survive the reload.
Agreed: these reloads are to be removed (goal 6 applied to Bloom's own code, building on the
"save a page without reloading it" work, BL-13502, PR 8209).
- **Orientation change** runs `BringBookUpToDate`, which deletes unreferenced image, audio and
video files at once. A file an undo would need could be gone after it.

---

## 17. Findings from unmerged branches

### 17.1 Flow text (`flowText` branch)

Read 2026-10-14. The branch adds no undo code of its own and has no design doc.

- **Other pages change while the user types.** A `MutationObserver` runs a pass per animation
frame that moves text between linked boxes on the page. At the last box on the page,
`settleCrossPageBoundary` pushes overflowing text to the next page, or pulls text back from it,
and calls `flowText/setNextContent`; C# writes the next page's box and saves that page at once
(`flowCrossPage.ts`, `FlowTextApi.cs`).
- **The caret is handed to the next page by a real page switch.** When the caret is in text being
pushed, keystrokes are held, the typed text is sent to C#, and `jumpToPage` runs the ordinary
save-and-navigate path. On the new page the caret is placed and the held text inserted with
`execCommand("insertText")`. Under §4.0 this is a boundary in the middle of typing that the user
never asked for.
- **Queued refits.** Typing across a page, any style change, a paper size or layout change, and
deleting a page queue a refit, held in static C# state until another book is selected. It runs
on the next page change (book setting `flowTextReflowOnPageChange`, on by default) or on
"Reflow now", behind a progress dialog. It rewrites boxes across every page of the chain, adds
or removes pages, saves the whole book, replaces the current box's `innerHTML` (caret moves to
the start), and may navigate or delete the current page if emptied. A queued refit keeps the
style rules from when it was queued, so undoing a style change does not change what it uses
(inferred).
- **Its own rollback.** `FlowTextWalk.ChainSnapshot` records each box's HTML and its group's
`style` and `data-flow-chain` before a refit writes, and restores them and removes added pages if
the refit fails. Internal to one operation in the C# book DOM; never reaches the user.
- **Pages.** "Create pages and continue flow" adds pages (a user click). Afterwards, refits add
and delete pages themselves. The user's Delete Page now moves the chain's text from that page
into a neighbouring page before deleting.
- **Overflow marker.** `OverflowChecker` inserts a `bloom-overflowStart` span into every
overflowing box, with or without flow text on, and it is saved (`flowOverflowMarker.ts`).
- **Fits none of the four kinds:**
  - Text pushed or pulled across a page as a direct result of the user's typing: saved to
  another page, so not Undoable under §5; not a boundary, since history continues; not
  Untracked, since undoing the typing without it duplicates text.
  - Page switches and page deletions Bloom starts itself.
- **Undo would set off the flow pass.** A restore of the page is seen as an edit, which can write
other pages and navigate (inferred).

Issues for the design, as the agent ranked them:

1. A user types, overflow is pushed to the next page, the user undoes. The box gets its text back
while the next page still holds it, and the pull-back finds nothing to fix, so the text is in
two places (inferred). That breaks goal 4. Either a transaction must also cover the next page's box
in C#, or typing that moves text across a page must be a boundary.
2. Bloom causes boundaries in the middle of typing (caret handover, refit navigation).
3. Undo and the flow pass act on each other: undo must not set off the pass, queued refits must
be removed or updated, and the overflow marker is content Bloom inserts on its own.

### 17.2 Inline images (BL-16822 stack, PRs 8319 to 8324 and 8467)

Read 2026-10-14 at `BL-16822-inline-7-e2e`. Reported as what the feature needs from undo, not
how today's undo works.

- **State.** An inline image is a `div.bloom-inlineImage` copied into every `.bloom-editable` of
the translation group, hidden languages and the `lang="z"` prototype included, because a float
only wraps text in its own element. Position, size, offset and crop are CSS custom properties in
the wrapper's inline style; Flip is an inline `transform` on the `img`. The wrapper is always the
first (or, for the bottom dock, last) child of the editable, so dragging an image never moves it
through the text; image edits and text edits touch different nodes and share only the editable's
child list. While editing, selection is a class on the saved wrapper plus handles inside the
editable, stripped on save.
- **Kinds of action.**
  - Undoable, one transaction each: insert, delete (all copies at once), Flip, Reset Image,
  Transparency, Paste image, Set Image Information, choose a picture.
  - Undoable, one transaction per gesture: drag to a dock, corner resize, crop handles. A gesture that
  ends where it started records nothing.
  - Boundary decided by the component: a picture change C# marks not undoable clears the
  history. This conflicts with §4.1 (replacing an image is undoable).
  - View change: selecting a picture; but selection is written into the saved wrapper and into
  the editable.
- **Untracked changes on the same elements and attributes as tracked ones**, all saved: on load,
copies in other languages are normalised, ids ensured, offsets rescaled from measurements; at
any time, `--inline-image-aspect-ratio` is written on every copy when an image loads. The authors
had to exclude these from their checks and recapture state at undo time by hand.
- **Interaction with CKEditor's and the browser's histories.**
  - One picture action changes every language's editable, but CKEditor keeps one history per
  editable, so its undo would bring an old picture back in one language only. The stack resets
  every CKEditor history in the group after each picture change; so undo cannot reach typing
  done before a picture change (asserted by the e2e suite), and "type, change a picture, type,
  undo, undo" goes back in the wrong order. Direct evidence that two histories over the same
  nodes cannot keep one order (§4.1).
  - Deleting across a picture is reversed by `BloomField`'s guard using the browser's own
  `document.execCommand("undo")`, a third history. It depends on the browser's history being in
  step with the content, which script changes break (§7).
  - Paste of a wrapper goes through CKEditor unfiltered and lands in one language only.
- **History lost without a boundary.** If the pictures changed in a way the stack didn't record
(a CKEditor restore, a paste), a step fails on undo and the history is discarded. A user's
history can disappear with no boundary. Conflicts with goal 4.
- **Cost (§1 goal 2).** About 850 of roughly 5,300 non-test lines (16%) are undo code, plus about
2,000 lines of undo tests. About 300 lines record and restore snapshots of every copy; about
550 lines, two thirds, coordinate with CKEditor's and the browser's histories and rebuild handles
and the toolbar after a restore.

Issues for the design, as the agent ranked them:

1. One action changes several editables that CKEditor keeps separate histories for. Captured
state must cover the whole translation group, or the page, not one editable.
2. Untracked changes land on the same attributes as tracked ones. The central system needs one
general way to handle that, or every component repeats it.
3. Selection is stored in saved DOM, and restoring replaces elements. Captured state should
exclude selection markup, and components need one general notice that content was restored so
they can rebuild handles and listeners.

### 17.3 Tables (`tableEditing.ts`, `tableFeature.ts`, `bloom-table` library)

Read 2026-10-14, `bloom-table` at the commit `dist-v0.1.1` was built from. Reported as what tables
need from undo.

- **The DOM is the model.** All table state is in `data-*` attributes (column widths, row
heights, spans, edges, gaps, corners, content type); inline styles are output that `render()`
derives from them. C# keeps no table state. Edit-time classes, CSS variables and overlays are
stripped from the saved copy.
- **JS state** in the library: its own undo history (`TableHistoryManager`, one stack for all
tables, snapshots of the table's `innerHTML` and attributes), resize-drag state, paint-format
and border-brush modes, copied properties. Most of it is keyed by element.
- **Kinds of action.**
  - Undoable, one transaction per click: add, delete, move or duplicate rows and columns, merge or
  split cells, change a cell's content type (discards its content), Delete Table, Cut Table,
  corners, alignment, paste properties, auto-size, paint-format and border-brush clicks.
  - Undoable, one transaction per gesture: dragging a row or column boundary. The padding slider and
  fill and border colours should be too, but today record one step per input event. The gap
  sliders record nothing.
  - Typing and pasting in a cell follow the text rules; pictures and video in cells use Bloom's
  image and video tools.
  - View changes: selecting a cell, entering a nested table, entering paint or brush mode, Copy
  properties, Copy Table.
- **After a restore, the table rebuilds from the DOM but is not wired up.** A restored table
carries `data-table-attached="1"`, so Bloom skips attaching it; restored cells carry
`contenteditable="true"`, so they get no CKEditor or `BloomField` (inferred). Whatever restores
page content must strip such markers or re-run attach and wiring every time.
- **Element identity.** The library's Delete Table un-delete re-inserts the same element so its
listeners survive; an HTML snapshot does not keep element identity.
- **Untracked changes.** `refitImageCellPictures` runs a frame after a `ResizeObserver` callback,
refits every `bloom-canvas` on the page (not only in cells), and is saved; it lands just after
the user's table operation. On attach, defaults, `tabindex` and rendered styles are written and
saved. It is not a pure function of cell size: `adjustAfterContainerResize` scales canvas
elements from the previous size stored in `data-imgsizebasedon`, so re-running it after a
restore can drift. It is swept up with the table change that caused it (§4.4 item 3).
- **Changing the library (§1 goal 6).** Nearly every operation goes through
`addHistoryEntry(table, description, performOperation, undoOperation, redoOperation)`, so the
library could report the start
and end of each operation to the central history instead of keeping its own. Resize-drag
changes the DOM during the preview, so its start must be marked at mousedown.

Issues for the design, as the agent ranked them:

1. Wiring after a restore: markers in the DOM make a restored copy look wired when it is not.
2. `bloom-table`'s own history should become reports to the central history; a small library
change, plus one transaction per gesture for sliders and colours and recording for the gap sliders.
3. The picture refit is an untracked change that lands right after a user's change and is
saved, the case a snapshot approach reverts on undo (§15, point 2).
