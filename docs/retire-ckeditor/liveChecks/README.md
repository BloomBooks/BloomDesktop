# Live checks for the undo work (BL-6681)

Scripts that drive a running Bloom over CDP and say which undo mechanism a gesture actually reached.
They exist because Stage 1's whole claim is "nothing changed", and jsdom cannot see the frames; each
later stage moves a mechanism onto the shared stack, and these are what shows it moved and nothing
else did. Kept in the repo, like `../verifyCaretPreservation.mjs`, so the next session does not
rebuild them.

## Running

```sh
node .github/skills/bloom-automation/launcherControl.mjs --ensure-running --wait-ready --json
node docs/retire-ckeditor/liveChecks/gotoBook.mjs "Decodable highlight"   # any Decodable Reader book
node docs/retire-ckeditor/liveChecks/verifyReader.mjs
node docs/retire-ckeditor/liveChecks/gotoBook.mjs "A house for mouse"     # any Basic Book with an image
node docs/retire-ckeditor/liveChecks/verifyCk.mjs
node docs/retire-ckeditor/liveChecks/verifyOrigami.mjs
node docs/retire-ckeditor/liveChecks/verifyImage.mjs
```

Ports come from `output/bloom-launcher.json`; set `BLOOM_CDP_PORT` / `BLOOM_HTTP_PORT` if Bloom was
started another way. Each script exits non-zero if any check failed. They type into and restore a
text box, so use a test book, and the collection must not be a disconnected Team Collection (its
books cannot be checked out, so Edit is disabled).

## What each proves

| Script | Book | Proves |
| --- | --- | --- |
| `verifyReader.mjs` | Decodable/Leveled Reader, tool active | Undo button → reader-tools undo only, with the BL-16558 markup update; our Ctrl+Y never fires while the reader tool claims it |
| `verifyCk.mjs` | Basic Book | Undo button → CKEditor undo only; Ctrl+Z/Ctrl+Y run CKEditor's commands exactly once; our Ctrl+Y declines |
| `verifyOrigami.mjs` | Basic Book (customPage) | Undo button → origamiUndo; origami's own Ctrl+Z/Ctrl+Y fire once; ours declines |
| `verifyImage.mjs` | any page with an image | Undo button → imageOperationUndo after an undoable copyright change |
| `handlerAccumulation.mjs` | any book with a text box | Stage 0 / inventory X4: edit key handlers accumulate when `SetupElements` runs again (fails today by design — it is the repro) |

Helpers: `gotoBook.mjs "<title>"` opens a book in Edit; `activateTool.mjs <toolId>` (e.g.
`talkingBook`, `decodableReader`) switches the toolbox tool through the ToolBox's own
`activateToolFromId`.

How attribution works: `verifyCommon.mjs` wraps the cross-frame functions the workspace bundle
reaches each mechanism through, and listens to CKEditor's `afterCommandExec`, so a gesture is
attributed by counters, not by "the text changed back".

## Expected failures, on purpose

`verifyReader.mjs` A4, A6 and A7 fail today and record two pre-existing problems (PROGRESS.md,
2026-09-07): the reader-tools undo restores a snapshot that still contains a CKEditor bookmark span,
and Ctrl+Z with a reader tool active runs *both* the reader-tools undo and CKEditor's, which breaks
the Ctrl+Y round trip. They should start passing when Stage 3 replaces both mechanisms; until then a
run of `verifyReader.mjs` is green when only those three fail.
