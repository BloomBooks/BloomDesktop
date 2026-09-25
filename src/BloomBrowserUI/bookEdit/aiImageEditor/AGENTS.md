This folder is the front end of the **"Edit with AI…"** integration: Bloom's side of the separate
`bloom-ai-image-tools` web app, which we load into an iframe overlay. The C# half is
`src/BloomExe/web/controllers/AiImageEditorApi.cs` — read its header first for the whole picture.

## Naming: never call it "the editor"

Bloom is itself an editor (the Bloom Editor), so "the editor" is ambiguous. Write "the AI Image
Editor" in full in code comments, test names, commits, PRs and card comments. Once it has been
named, "it" is fine. Bloom's side is just "Bloom".

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

## Analytics: Bloom forwards the AI Image Editor's events and knows nothing about them

The AI Image Editor names its own analytics events and chooses their properties, including the
session id that groups one visit's events and the session length. Bloom forwards each one to
Segment exactly as sent (`case "analytics"` in `aiImageEditorOverlay.ts`). What each event means
is documented in `lib/analyticsEvents.ts` in BloomBooks/bloom-ai-image-tools.

**Don't add a list of allowed event names or properties, rename events, add properties, or send
events of Bloom's own about the session.** Any of those means Bloom knowing the AI Image Editor's
vocabulary, and a list fails badly: an unlisted event is silently dropped, which looks exactly
like nobody using the feature. If a number is missing, have the AI Image Editor send it, or
compute it in the analytics pipeline. The same goes for any library Bloom hosts.

The one Bloom event here is `Change Picture` with source `ai-editor`, one per picture that
reached the book. It is Bloom's event about the book, shared with the other ways a picture gets
in, and only Bloom knows whether a swap on the page being edited landed.

## Localization

The editor's own strings are translated by Bloom, not by the editor. On startup the iframe POSTs
its whole string table (every id with its English) to Bloom's general-purpose
**`i18n/loadStrings`**, the same endpoint the image gallery uses, and shows what comes back.

The editor asks only for ids Bloom actually has. Its `lib/untranslated.ts` lists every string we
have not made localizable, and `ALL_IMAGE_EDITOR_STRINGS` leaves those out, so they stay hardcoded
English in the editor. That is what a string not yet chosen for translation looks like: absent from
the table, rather than present and unanswered.

The check on that is `loadStrings` itself. On Developer and Alpha channels it reports any id it
cannot find, as a toast plus a `CopyToDistributionXlf_` entry in the local xlf. So a toast when the
editor starts means an editor build added a string without either a `<trans-unit>` in
`DistFiles/localization/en` or an entry in `lib/untranslated.ts`. Nothing breaks meanwhile: an
unanswered id comes back as the English the editor sent.

The ids are `AiImageEditor.*`, except where the editor reuses a string Bloom already has
(`Common.Close`, `EditTab.PasteButton`, and so on). Nothing here decides them: they live in the
editor repo.

## Tests

- `*.test.ts` are Vitest and run in the normal suite. `aiImageEditorOverlay.test.ts` needs no page DOM
  at all, which is one of the points of the split — keep it that way.
- `bloom-exe-*.uitest.ts` are Playwright against a **running** `Bloom.exe`, excluded from Vitest
  and matched by glob (`**/bloom-exe*.uitest.ts`), so they are fine anywhere in the tree. They
  exercise the HTTP endpoints rather than this front-end code. To drive the real UI end to end,
  see `.claude/skills/run-bloom/ai-image-editor-driving.md`.
