# Splitting BL-17020 into PRs that a person can review

PR 8484 (https://github.com/BloomBooks/BloomDesktop/pull/8484) changes 221 files: about 14,000
lines of product code, 27,000 lines of tests and 8,000 lines of docs. Its 13 parts were cut by
component, and none of them can ship alone: parts 2 to 4 add code that nothing calls, part 5
switches the Edit tab over, and parts 6 to 13 each bring one more component onto the page history.
Until all 13 merged, there was no point at which master was releasable with the new undo.

This plan cuts the same work so that master is releasable after every PR, and so that one named
PR is the point where Bloom's undo does about as much as master's does today. Every PR after
that one adds undo coverage that master never had or removes a loss of history that the new undo
brings, and each can be reviewed, merged and shipped on its own.

Line counts are from the commits on `BL-17020-undo`. "Product" counts added plus deleted lines
outside tests and docs; a third of the product lines of "Replace the old undo" are deletions.

## What "about as much as master" means here

Master's undo covers the actions in the table at the top of MINIMUM.md: typing and formatting in a
box (CKEditor, one history per box), typing with a reader tool open, table operations, replacing a
picture (chooser, gallery, paste, AI image editor), rotate and flip, rotating by dragging,
removing a background picture, and origami's own undo in Change Layout mode.

"Replace the old undo", the PR that moves the Edit tab onto the page history, has to keep each of those undoable, and it has to keep two
promises that the design makes from the start:

- no undo damages the page (an undo whose step no longer matches the page is refused, which loses
  history but changes nothing);
- nothing that ran before that PR now writes something into the page that a later undo or a
  save would turn into damage.

It may lose history where master keeps it. Master keeps a box's CKEditor history while the toolbox,
a dialog or a request changes the page; after "Replace the old undo", a change from the toolbox or a dialog
that has no undo step of its own ends the page's history. That is the main way in which the
PR is worse than master, and the PRs after it remove those losses one component at a time.

## Before "Replace the old undo": PRs that change nothing a user sees

These four can be reviewed in parallel. The first three add code that nothing calls yet, so
merging them needs no testing in Bloom. They merge in order: the markup filter's tests drive the
engine, and `pageHistorySetup.ts` in the third starts the engine with the filter. The fourth is
based on master and can merge at any time.

### Engine

`pageHistory.ts`, `pageHistoryDom.ts`, `pageHistoryTypes.ts`, `pageHistoryDetails.ts`,
`pageHistoryRecorder.ts`, in their final form: the engine fixes from the later commits (the five bugs that the randomized test found,
a step that both adds and removes a node, a refused undo that says what differed, descriptions
built from a template and a noun) go in here, not in fix-up commits on top.

3,246 product lines, 3,357 test lines (`pageHistorySpec.ts`). A reviewer reads `pageHistoryTypes.ts` first, then APPROACHES.md
section 11.8, then the implementation.

### Markup filter

`editTimeMarkup.ts` and the filter that the history uses to leave out what a save strips. 767
product lines, 5,039 test lines (`editTimeMarkupSpec.ts`, `pageHistoryImplSpec.ts`, the browser
specs, and the randomized engine test `pageHistoryRandomSpec.ts`, which needs the filter). The reviewer's question for each rule is "does the save path
strip this?", and each rule names the step of the save path that it mirrors.

### Input, keys and page readiness

`pageHistoryInput.ts`, `pageHistoryKeys.ts`, `pageReadiness.ts`, `pageHistorySetup.ts`,
`pageHistoryAccess.ts`, and origami's exports, in their final form: typing steps follow
Chromium's own undo (TYPING-STEPS.md), a pause of a second ends a typing step, Enter starts a new
step. 1,436 product lines, of which 26 are exports added to `origami.ts`, `editablePage.ts` and
`workspaceRoot.ts`, and 2,432 test lines.

### Preparation that is right on master without the new undo

Small changes that "Replace the old undo" depends on, each correct on its own, so they can merge
before it and get exercised in alphas first:

- The reader tools paint their highlights with CSS instead of rewriting the box (from part 11,
  without deleting their own undo, which "Replace the old undo" deletes). With the box no longer
  rewritten, the reader tools' undo starts to work, and on Ctrl+Z it ran together with CKEditor's
  and emptied the box on the next Ctrl+Y. So Ctrl+Z and Ctrl+Y now go to CKEditor alone, which is
  what master's keys did in practice, and the Undo button keeps the reader tools' undo. About 330
  product lines, 495 test lines.
- Show Invisibles puts back the same node objects when the keys are released, instead of
  rewriting the box's HTML (from commit `35a8454a88`, without its `doOutsideHistory`, which
  "Replace the old undo" adds). About 420 product lines. Without this, a press and a release of
  the keys become two undo steps after "Replace the old undo", and I expect, without having tried
  it, that undoing the release puts the symbols back into the page where a save can keep them.
- A resize drag fits the canvas element to its picture at the end of the drag, and duplicating a
  canvas element copies its recording through `audio/copyAudioFile` (from part 7). The fit
  measures from the style's lengths and writes nothing when the box already fits, without which
  every selection of a fitted picture moved it by a fraction of a pixel. About 150 product lines.

The branch's two toolbox fixes, `653879d116` and `99a054fc74`, are not needed: master's
`7f3236a615` fixes both bugs another way, and "Replace the old undo" keeps master's version.

## "Replace the old undo with the page history": after it, Bloom's undo matches master's

One PR, and the one that needs the most careful review. After it merges, Bloom can ship.

- Wiring from part 5: `editablePage.ts` starts the history; CKEditor's undo plugin goes;
  `changeImage` and `changeImageByElement` are transactions that last until the picture settles;
  cut and paste are transactions; the request interceptor in `bloomApi.ts` ends the history on any
  request that is not on `kRequestsThatKeepThePageHistory`; `BloomField` refuses the deletion of a
  protected element instead of calling the browser's undo.
- Deletions: `bookEdit/undo/` (about 1,000 lines), `ImageUndoManager.ts` and its spec, the reader
  tools' own undo and redo.
- The canvas element manager's listeners from part 7, so that undoing a rotation, a flip or a
  removed background picture redraws the Comical bubbles and the handles.
- The part of part 10 that keeps Talking Book's playback and set-up writes out of the history,
  with its listeners before and after an undo. Talking Book is used on most pages that people
  record, and without this part, playback writes spans into the text that an undo step then
  disagrees with, so the undo is refused and the history is gone.
- Show Invisibles inside `doOutsideHistory`.
- The page history's lines in Bloom's log (`a7e8c20ae2`, 80 lines), so that a problem in the field
  can be traced.
- E2E: `undo-typing`, `undo-routing`, `undo-reader-tools`, master's `rotate-and-flip-images`, and
  the table tests.

About 4,000 product lines, of which about 1,500 are deletions, and about 6,500 test lines.

What it does compared with master:

| | Master | After "Replace the old undo" |
|---|---|---|
| Typing, formatting, paste, cut | Undo and redo, per box | Undo and redo, one history for the page |
| Typing with a reader tool | Two undos at once | One undo |
| Tables | Undo button only | Keys and button, with redo |
| Replacing a picture, rotate, flip, removing a background picture | Only while that picture is selected | Always, with redo |
| Moving, resizing, cropping, adding, deleting a canvas element | No undo | Undo and redo, all named "Edit" |
| Change Layout mode | Origami's own undo | The same, and entering or leaving the mode ends the history |
| A control in the Canvas tool, the colour picker, the Format dialog, Book and Page Settings, Motion, Music, Games, Image Description's checkbox, Sign Language | Box histories survive | Ends the history |
| A change that a tool makes after an `await` (Sign Language's recording, some canvas commands) | Box histories survive | Joins the step before it, so undoing that step undoes both |
| Talking Book's Record, Clear, Import | Box histories survive | Ends the history (and still does at the end of this plan) |
| Ctrl+Z in a box | Undoes that box's latest change | Undoes the page's latest change |

Before this PR is marked ready, the evidence for the table is: the coverage map
(https://claude.ai/artifact/9NSe7dTahj28eBwjALM8Uo) redone for this PR's tree, the e2e tests above
passing headless at the nightly's window size, and the checks in a running Bloom that PR 8484
lists, repeated on this tree.

## After "Replace the old undo": each PR makes undo better than master

Any order, each one shippable. Each adds the undo steps, descriptions and listeners of one
component, with that component's tests.

| PR | From | What gets better | Product | Tests |
|---|---|---|---|---|
| Canvas commands and the Canvas tool | part 8, `a6d1806d8b`, `1da4f5c367` | Each command, Canvas tool control, colour picker session and layer move is one named step; an async command no longer joins the step before it | about 500 | about 3,200 |
| Format dialog, Book and Page Settings, link grid, Original Title, Set Destination, Set Up Hyperlink | part 12 | These stop ending the history; the last three become named steps | about 650 | about 1,300 |
| Other toolbox tools | part 13, game theme from `2b8dda6e18` | Games, Music, Motion, Image Description and the Impairment Visualizer stop ending the history; a Motion drag is a step | about 700 | about 2,200 |
| Talking Book's steps | rest of part 10, Adjust Timings from `2b8dda6e18` | The recording mode, the playback-order arrows and Adjust Timings' OK are named steps | about 300 | about 800 |
| Video and Sign Language | part 9 | Choosing a video no longer reloads the page; Record, Import and Delete are steps | about 200 | about 850 |
| AI image editor | part 6 | One step per commit; the editor no longer deletes the files that an undo needs | about 170 | about 550 |
| Redo button | `3c643efb36` | A Redo button; Undo and Redo reach the page without a round trip through C# | about 180 | about 20 |
| Show Page History | `2af6bd54d8`, `05736e941c` | The developer's window that shows the steps and the Stack tab | about 2,100 | about 1,600 |

Show Page History goes last, so that its window shows the descriptions of every component's steps.

The descriptions of steps travel with their call sites: the engine takes a description from the
start, "Replace the old undo" gives the general "Edit" to everything, and each PR above names its own steps.

## Docs

The 8,000 lines in `docs/undo/` are reference, not something to review line by line. They go in
with the engine PR, and the reviewer's reading list is OUTLINE.md, MINIMUM.md and APPROACHES.md
sections 3 and 11.8. MINIMUM.md is rewritten in "Replace the old undo" to describe that PR as this plan cuts it, and its
list of what is worse than master becomes the table above.

## How the PRs get built

From the final tree on `BL-17020-undo`, rebased on current master, not by replaying the old
commits: each PR takes the final version of the files that belong to it, and the files that
several PRs touch (`bloomEditing.ts`, `CanvasElementManager.ts`, `bloomApi.ts`, `toolbox.ts`) are
split by hunk. Each PR must build, pass the front-end tests and lint on its own, and, from "Replace the old
undo" on, pass the undo e2e tests that exist at that point. The last PR leaves master with the
same tree as `BL-17020-undo`, which `git diff` checks.

PR 8484 stays open as the reference until "Replace the old undo" merges, then closes, and BL-17020 links each
new PR in order.
