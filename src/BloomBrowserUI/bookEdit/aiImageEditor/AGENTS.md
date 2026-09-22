This folder is the front end of the **"Edit with AI…"** integration: Bloom's side of the separate
`bloom-ai-image-tools` web app, which we load into an iframe overlay. The C# half is
`src/BloomExe/web/controllers/AiImageEditorApi.cs` — read its header first for the whole picture.

## Naming: never call it "the editor"

Bloom is itself an editor -- the Bloom Editor, as against Bloom Reader or Bloom Library -- so "the
editor" is ambiguous every single time, including in a sentence where you think context makes it
obvious. Write **"the AI Image Editor"** in full, in code comments, test names, commit messages, PR
descriptions and card comments alike. When a sentence has already named it and needs to refer back,
use "it" or "its" rather than shortening to "the editor". Our own side is just "Bloom".

The feature is a command on the image context menu plus a dialog-like overlay — the same shape as
`bookEdit/copyrightAndLicense/`. The canvas menu only *offers* it: `canvasControlRegistry` imports
`launchAiImageEditor`, and `buildCanvasElementControlRegistryContext` imports
`isAiEditableImageSrc` to decide whether the item is enabled. Those two imports are the whole
coupling; keep it that way.

## The frame rule — the one thing to get right here

The files are split by **which browser frame they run in**, and mixing them up would silently
undo the reason the split exists. Bundling follows imports, not folders, so nothing but this rule
stops the wrong half ending up in the wrong bundle:

| File | Frame | Exposed on |
| --- | --- | --- |
| `aiImageEditorOverlay.ts` | **top window** (workspace root) | `workspaceBundle.openAiImageEditor` |
| `aiImageEditorPageCommands.ts` | **page iframe** | `editablePageBundle` (`launchAiImageEditor`, `applyAiImageEditorReplacements`) |
| `aiImageEditorShared.ts` | either — pure, no DOM, no api calls | — |
| `aiImageEditorImageFormats.ts` | either — pure | — |

So:

- **Never import `aiImageEditorOverlay` from page-frame code**, and never import `aiImageEditorPageCommands`
  from the top window. Each half reaches the other through `getEditablePageBundleExports()` /
  `workspaceBundle`, exactly as the overlay already does for the live page.
- **The overlay must not touch the page's DOM directly.** It asks the page frame to do it, via the
  single named call `applyAiImageEditorReplacements`.
- **Anything both halves need goes in `aiImageEditorShared.ts`** and must stay pure. `isCurrentPageSwap`
  is there specifically so the two halves cannot disagree about which commit results belong to the
  page being edited.

The overlay is in the top window because a page save reloads the page iframe, which would tear
down an overlay hosted there — the same reason the image-gallery and copyright/license dialogs
live up there (see the comments on those commands in `canvasControlRegistry.ts`). Note that this
is *not* enough on its own: a save can also reload the whole workspace root, so C# waits for the
page to come back before opening the overlay. `AiImageEditorApi.HandleSaveThenLaunch` explains
that in full.

## Analytics: the AI Image Editor owns its own vocabulary

The AI Image Editor names its own events and chooses their properties. Bloom forwards what it is
given (`case "analytics"` in `aiImageEditorOverlay.ts`), rewriting only the `AI Editor ` prefix to
`AI Image Editor ` so that a future AI editing tool for text or video is not confused with it.

**Do not add a list here of the events or properties the AI Image Editor is allowed to send.** Both
ends are ours and the names are constants in one file of the library's, so such a list guards us
only against ourselves. What it actually buys is that a mistake disappears instead of showing up: a
stray event name in Segment is visible and fixable, whereas a dropped event is indistinguishable
from nobody using the feature.

The same reasoning applies to any library Bloom hosts. What each event means and every property it
carries is documented in the library, in `lib/analyticsEvents.ts` in
BloomBooks/bloom-ai-image-tools.

Bloom does add one property on the way through: `aiEditorSessionId`, minted per launch by the
overlay. Without it nothing says which trip through the AI Image Editor a row belongs to, so its
events and Bloom's closing summary cannot be joined. That is host context the AI Image Editor
cannot supply, in the same spirit as C# adding `BookId` -- it is not Bloom second-guessing the
library's vocabulary. Do not use `launchData.sessionToken` for this: it is a capability token for
the commit endpoint and must not reach Segment.

The one event Bloom sends *about* the AI Image Editor rather than for it is
`AI Image Editor Closed`, from `reportClosed` -- one per session. That stays Bloom's, because only
Bloom knows whether a commit actually reached the book; the library deliberately has no session-end
event of its own. The pin is currently `dist-v0.2.5`, which sends only `AI Editor Generate`; when it
moves, move it to `dist-v0.2.10` or later, because 0.2.9 still sent an `AI Editor Close` that would
now be forwarded and sit next to ours. `AI Image Editor Closed` carries only counts Bloom derives
from what C# said each commit did, so it needs no name from the library at all.

**Do not add a count of the AI Image Editor's generations to that row.** Counting them means
recognizing an event name the library owns. "Generated 11, applied 0" is a group-by on
`aiEditorSessionId`, and it answers in more detail than a column could -- which tool and model each
discarded generation used, and what it cost. If a single column is wanted, get the number from the
AI Image Editor over the bridge (a message type, which Bloom legitimately co-owns), or materialize
it downstream in the analytics pipeline.

## Tests

- `*.test.ts` are Vitest and run in the normal suite. `aiImageEditorOverlay.test.ts` needs no page DOM
  at all, which is one of the points of the split — keep it that way.
- `bloom-exe-*.uitest.ts` are Playwright against a **running** `Bloom.exe`, excluded from Vitest
  and matched by glob (`**/bloom-exe*.uitest.ts`), so they are fine anywhere in the tree. They
  exercise the HTTP endpoints rather than this front-end code. To drive the real UI end to end,
  see `.claude/skills/run-bloom/ai-image-editor-driving.md`.
