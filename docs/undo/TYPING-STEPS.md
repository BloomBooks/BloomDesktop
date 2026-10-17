# When typing starts a new undo step

Typing in a text box is undone in steps. Each Ctrl+Z takes back one step. These are the rules for
where one step ends and the next begins, as the code has them now (`TypingRun` in
`src/BloomBrowserUI/bookEdit/pageHistory/pageHistoryInput.ts`).

The rules are the browser's own. Bloom runs in WebView2, which is Chromium. The browser's undo of
typing in a contenteditable box was measured in Edge 155 (Chromium 155.0.4283.45): real key presses
through Playwright, then Ctrl+Z pressed until nothing more changed, with the box recorded after
each press. The page history follows what that measured, except in the places listed under
[Where Bloom differs from the browser](#where-bloom-differs-from-the-browser).

## What a typing step is

A typing step is the key presses in one text box, from the first key that types or deletes until
something in the list below ends it. The step stays open between key presses, so whatever Bloom
does to the box in reaction to the typing joins the same step. Examples are Talking Book splitting
the text into sentences and the overflow check. Undoing the typing undoes those reactions too.

A typing step starts at a key press that types or deletes. It also starts at typing or deleting
that comes with no key press: a spelling suggestion chosen from the context menu, dictation, or
an input method that sends no key presses.

A step that begins with typing is described as `EditTab.Undo.Typing`. One that begins with
Backspace or Delete is described as `EditTab.Undo.DeleteText`.

## What ends a typing step

| What happens | What it does |
| --- | --- |
| **Backspace or Delete right after typing** | Ends the step, and the deleting starts a new one. Typing after the deleting joins the deleting's step. So "abc", Backspace twice, "xy", Backspace is three steps: "abc"; the two Backspaces with "xy"; the last Backspace. A delete that changes nothing, such as Delete at the end of the text, doesn't count. |
| **An Enter or Shift+Enter that leaves nothing after the caret on its line** | The Enter joins the step that is open. The next key that types starts a new step. So typing three lines makes three steps, each a line with the Enter that ends it. An Enter in the middle of a line joins the typing before and after it. |
| **A key that moves the caret** | The arrow keys, Home, End, Page Up and Page Down. The browser ends the step at these even when the caret doesn't move, as with End when the caret is already at the end. |
| **A click in the box that moves the caret, or a drag that selects** | A click that leaves the caret where it was doesn't end the step. |
| **A press of the mouse outside the box** | In another box, in the toolbox, on the top bar. |
| **The focus leaving the box, or Bloom's window losing it** | For example, Tab to the next box. |
| **The end of an IME composition** | The composition joins the step that is open, and its end ends that step. |
| **A key with Ctrl, Alt or Cmd** | Such as Ctrl+B with text selected, Ctrl+A, or Ctrl+Backspace (delete a word). Each is a step of its own if it changes the page, and no step if it doesn't. AltGr, which types characters on many keyboards, is typing. |
| **Paste, Cut, or a drop** | Each is a step of its own (`EditTab.Undo.Paste`, `Cut`, `Drop`), and typing after one starts a new step. |
| **A key press in another text box** | Each box's typing is a step of its own. There is one history for the whole page, as in the browser. |
| **Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z, or the Undo or Redo button** | The typing step ends first, then the undo or redo runs. |

## What does not end a typing step

| What happens | Notes |
| --- | --- |
| **The length of the typing** | There is no limit. 200 characters typed without a break are one step. |
| **A pause** | There is no time limit. |
| **Spaces and the ends of sentences** | Words and sentences don't end a step. |
| **Deleting, then typing** | The typing joins the deleting's step. |
| **A delete that changes nothing** | Backspace at the start of the box, Delete at the end. |
| **A click in the box that leaves the caret where it was** | |
| **A modifier key on its own, Escape, a function key, a dead key** | |
| **Bloom's reactions to the typing** | They join the open step. |

## What the browser does, case by case

Each row is a case from the experiment. "Steps" lists what each Ctrl+Z took back, newest first.

| Case | Browser (Edge 155) | Bloom |
| --- | --- | --- |
| "one", Enter, "two", Enter, "three" | "hree"; "wo⏎t"; "one⏎t" | "three"; "two⏎"; "one⏎" |
| "abcd", ← ←, Enter, "XY" | "⏎XY"; "abcd" | same |
| 30 or 200 characters | one step | same |
| " hello world foo bar", " One. Two. Three." | one step | same |
| "abc", Backspace ×2, "xy" | Backspaces with "xy"; "abc" | same |
| "abc", Backspace ×2, "xy", Backspace | Backspace; Backspaces with "xy"; "abc" | same |
| Backspace ×30 | one step | same |
| "abc", Delete at the end, "xy" | one step | same |
| Backspace, ← ←, Delete | Delete; Backspace | same |
| "ab", ←, "c" (and with End, Home, ↑) | "c"; "ab" | same |
| "ab", ← →, "c" | "c"; "ab" | same |
| "ab", 3-second pause, "cd" | one step | same |
| "ab", click where the caret is, "cd" | one step | same |
| "ab", click elsewhere in the box, "cd" | "cd"; "ab" | same |
| "ab", click another field and back, "cd" | "cd"; "ab" | same |
| "one", Shift+Enter, "two" | "wo"; "one⇧⏎t" | "two"; "one⇧⏎" |
| "ab", Ctrl+B, "cd" (nothing selected) | "d"; "abc" | "cd"; Ctrl+B if it changed the page; "ab" |
| "abc", Shift+←, Ctrl+B, →, "d" | "d"; bold; "abc" | same |
| "ab", Ctrl+A, "new" | "new"; "ab" | same |
| "abc", Shift+←, "X" | "X"; "abc" | same |
| "ab", paste, "cd" | "cd"; paste; "ab" | same |
| "abc", Shift+←, Cut, "Q" | "Q"; cut; "abc" | same |
| " hello world", Ctrl+Backspace ×2 | each Ctrl+Backspace; the typing | same |
| "ab", Escape or Shift alone, "cd" | one step | same |
| "ab", PageDown in a one-line box, "c" | one step | "c"; "ab" |
| "ab", IME composes か, then さ, "cd" | "cd"; "さ"; "abか" | same |
| typing in one box, then another | the second box's typing; the first's | same |

## Where Bloom differs from the browser

- **The first character of a new line.** The browser ends the step one character after an Enter at
  the end of a line, so Ctrl+Z after typing three lines leaves the first letter of each line
  behind ("three" goes back to "t"). This happens only when the typing goes on into an empty line;
  typing into an empty box as the first thing is one ordinary step. It is a by-product of how
  Chromium fills the placeholder of an empty line, so Bloom ends the step at the Enter instead.
- **Ctrl+B and the like with nothing selected.** The browser just notes the style for the next
  character, which joins the typing. In Bloom, CKEditor handles these keys, and a key with Ctrl is
  a step of its own and ends the typing step.
- **Page Up or Page Down that moves nothing.** The browser keeps the step; Bloom ends it at every
  navigation key. (End ends the step in the browser even when the caret doesn't move.)

## Typing with an input method

The browser's undo ends a step at the end of each composition, so every committed piece of a
Japanese or Chinese input is a step. Bloom does the same. Chromium sends the `compositionend` of a
commit as an untrusted event, so the page history takes it from any source.

Bloom found in 2018 that Keyman sends `compositionend` events without key presses (BL-5440). Input
with no key press starts a typing step, so Keyman's typing is undoable. But if a Keyman keyboard
commits each key as a composition, each key would be a step of its own, in the browser and in
Bloom. That has not been checked with Keyman.

## Where it is tested

- Unit tests: `pageHistoryInputSpec.ts`, `describe("typing")`.
- Real key presses in a browser: `pageHistoryInput.browser.spec.ts` (no limit on length, Backspace
  after typing, a Delete that changes nothing, Enter at the end of a line).
- Real key presses in Bloom: `src/BloomE2E/tests/undo-typing.spec.ts`. It covers three lines,
  Enter mid-line, 60 characters, typing and deleting, a Delete that changes nothing, clicks that
  keep and move the caret, an arrow key, Shift+Enter, an IME composition, and typing with Talking
  Book open.
