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

## Analytics: the AI Image Editor owns its own vocabulary

The AI Image Editor names its own analytics events and chooses their properties. Bloom forwards
them (`case "analytics"` in `aiImageEditorOverlay.ts`), changing only the name prefix from
`AI Editor ` to `AI Image Editor `. What each event means is documented in
`lib/analyticsEvents.ts` in BloomBooks/bloom-ai-image-tools.

**Don't add a list of allowed event names or properties.** Both ends are ours, so a list only
protects us from ourselves, and it fails badly: an unlisted event is silently dropped, which looks
exactly like nobody using the feature. A stray event name in Segment, by contrast, is easy to spot
and fix. The same goes for any library Bloom hosts.

Bloom adds one property on the way through: `aiEditorSessionId`, a new id for each launch, so the
events from one visit can be grouped together. Don't use `launchData.sessionToken` for this; it is
a capability token and must not go to Segment.

Bloom also sends one event of its own per session, `AI Image Editor Closed` (from `reportClosed`),
because only Bloom knows what actually reached the book and how long the overlay was open. Its
counts come from the C# commit replies, not from the AI Image Editor's events.

**Don't count the AI Image Editor's events in Bloom** (for example, how many generations a session
had). That means knowing its event names. Group its events by `aiEditorSessionId` instead. If a
single number is ever needed, have the AI Image Editor send it over the bridge, or compute it in
the analytics pipeline.

When the `bloom-ai-image-tools` pin moves off `dist-v0.2.5` (which sends only
`AI Editor Generate`), go to `dist-v0.2.10` or later: `dist-v0.2.9` sends an `AI Editor Close`
event that would now be forwarded alongside Bloom's own.

## Tests

- `*.test.ts` are Vitest and run in the normal suite. `aiImageEditorOverlay.test.ts` needs no page DOM
  at all, which is one of the points of the split — keep it that way.
- `bloom-exe-*.uitest.ts` are Playwright against a **running** `Bloom.exe`, excluded from Vitest
  and matched by glob (`**/bloom-exe*.uitest.ts`), so they are fine anywhere in the tree. They
  exercise the HTTP endpoints rather than this front-end code. To drive the real UI end to end,
  see `.claude/skills/run-bloom/ai-image-editor-driving.md`.
