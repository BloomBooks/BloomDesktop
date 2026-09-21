This folder is the front end of the **"Edit with AI…"** integration: Bloom's side of the separate
`bloom-ai-image-tools` web app, which we load into an iframe overlay. The C# half is
`src/BloomExe/web/controllers/AiImageEditorApi.cs` — read its header first for the whole picture.

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

## Localization

The editor's own strings are translated by Bloom, not by the editor. On startup the iframe POSTs
its whole string table (every id with its English) to **`aiImageEditor/localizations`** and shows
what comes back, falling back to its own English for every id we leave out. An editor build newer
than Bloom's XLF files therefore runs in English for the new strings and nothing else changes.

That endpoint exists rather than Bloom's general-purpose `i18n/loadStrings` (which is what the
image gallery uses) for one reason: `loadStrings` *reports* every id it cannot find, and on a
Developer channel build that means a toast plus a `CopyToDistributionXlf_` entry in the local xlf
— per string, on every launch. With several hundred editor ids waiting to reach the xlf files that
is unusable, so `HandleLocalizations` looks the same ids up through
`I18NApi.GetAvailableTranslations`, which reports nothing. An editor build older than this
endpoint falls back to `loadStrings` on its own.

The ids are `AiImageEditor.*`, except where the editor reuses a string Bloom already has
(`Common.Close`, `EditTab.PasteButton`, and so on). Nothing here decides them: they live in the
editor repo, which exports the whole table as `ALL_IMAGE_EDITOR_STRINGS`.

## Tests

- `*.test.ts` are Vitest and run in the normal suite. `aiImageEditorOverlay.test.ts` needs no page DOM
  at all, which is one of the points of the split — keep it that way.
- `bloom-exe-*.uitest.ts` are Playwright against a **running** `Bloom.exe`, excluded from Vitest
  and matched by glob (`**/bloom-exe*.uitest.ts`), so they are fine anywhere in the tree. They
  exercise the HTTP endpoints rather than this front-end code. To drive the real UI end to end,
  see `.claude/skills/run-bloom/ai-image-editor-driving.md`.
