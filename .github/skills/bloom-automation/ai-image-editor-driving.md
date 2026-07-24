# Driving the AI Image Editor over CDP

How to launch, drive, and verify the **"Edit with AI…"** feature end-to-end against a real
running `Bloom.exe`, at **zero cost** (no OpenRouter calls). This is a companion to
`SKILL.md`; it covers the parts unique to the AI editor — the linked editor dev server, the
free dummy model, the three-frame topology, and how to verify a commit persisted correctly.

Reusable driver: **`driveAiImageEditor.mjs`** in this folder.

## Why this needs special handling

- The editor is a **separate web app** (`bloom-ai-image-tools`) loaded in an **iframe overlay**
  inside Bloom's edit-tab WebView2. So the automation target spans **three frames**: the shell
  (top), the `page` content iframe, and the editor overlay iframe. A single-frame driver can't
  see the editor.
- Real AI generation costs money. Use the **"Local Dummy (No AI)"** model instead: it runs
  entirely in-browser, needs no key, and stamps a "DUMMY EDIT (no AI)" banner on the source
  image. It's a model choice on every tool, so it exercises the full edit→commit flow.
- The dummy model is **gated**: Bloom only offers it when the launch payload has
  `showDeveloperTools: true`, which `AiImageEditorApi.HandleLaunch` sets from
  `ApplicationUpdateSupport.IsDev` (true for a developer/`go.sh` build). Shipped builds never
  expose it. So this only works from a source launch, which is what you want anyway.

## 1. Launch Bloom with the editor linked (HMR) + dev tools

```bash
./go.sh --with bloom-ai-image-tools=D:/bloom-ai-image-tools > /tmp/go-bloom.log 2>&1   # background
until grep -qE "BLOOM_AUTOMATION_READY|exited shortly after" /tmp/go-bloom.log; do sleep 3; done
grep -E "BLOOM_AUTOMATION_READY|AI editor: live dev server" /tmp/go-bloom.log
# => [go] AI editor: live dev server at http://localhost:3000/ (HMR).
# => [exe] BLOOM_AUTOMATION_READY {"processId":...,"httpPort":8092,"cdpPort":8094}
```

- `--with bloom-ai-image-tools=<path>` runs the editor's **own Vite dev server** and points
  Bloom's `BLOOM_AI_EDITOR_URL` at it (full HMR on the editor). **Pass the explicit `=<path>`**:
  auto-discovery only looks at paths relative to the Bloom worktree and won't find a checkout
  elsewhere (e.g. `D:/bloom-ai-image-tools`).
- Editor-side `.ts`/`.tsx` edits hot-reload; Bloom-side C# edits hot-reload for method bodies
  (adding a field to the launch payload's anonymous object did reload here), but a rude edit
  needs a restart.
- Use the **HTTP port** as the instance identity; **CDP port is HTTP+2** (8092 → 8094).

## 2. Frame topology

```
shell page  (top, url .../bloom/…Temp/bloomXXXX.htm)
├─ toolbox
├─ pageList
├─ page                     ← current page content (editable images live here)
└─ ai-editor-overlay iframe ← the editor app, url http://localhost:3000/?mode=bloom-iframe
                              (only present while the overlay is open)
```

`driveAiImageEditor.mjs` resolves these as: shell = the `/bloom/` page; content = frame named
`page`; editor = the frame whose url contains `localhost:3000`.

## 3. Open the overlay the *real* way (so the commit handler is live)

Right-click the canvas element → **"Edit with AI…"**. Loading the iframe directly (as the
`bloom-exe-ai-editor-open.uitest.ts` smoke test does) does **not** register
`aiEditorLauncher.ts`'s postMessage handler, so the commit + current-page save path wouldn't run.

**Gotcha:** a Comical `<canvas class="comical-generated">` overlay sits on top of the image and
intercepts element-targeted clicks (Playwright reports `<canvas …> intercepts pointer events`).
Drive with **raw mouse coordinates** from the image's bounding box instead:

```js
const box = await img.boundingBox();
const cx = box.x + box.width / 2, cy = box.y + box.height / 2;
await page.mouse.click(cx, cy);                     // select the canvas element
await page.mouse.click(cx, cy, { button: "right" }); // raise its context menu
await contentFrame.locator('[role="menuitem"]', { hasText: "Edit with AI" }).first().click();
```

## 4. Editor UI recipe (free dummy edit)

Selectors mirror the editor's own e2e test `tests/bloom-host-harness.spec.ts` (the source of
truth if these drift). All run on the **editor frame**:

```js
await ef.getByRole("button", { name: /Enhance/i }).click();   // expand the Enhance section
await ef.getByText("Custom Edit", { exact: true }).click();    // pick a tool
await ef.getByTestId("tool-model-picker-custom").click();
await ef.getByText("Local Dummy (No AI)").click();             // the free model
await ef.locator("body").press("Escape");
await ef.getByTestId("input-prompt").fill("Add a dummy banner");
await ef.getByRole("button", { name: /Apply Changes/i }).click();
await ef.getByTestId("bloom-host-commit-current-result").click(); // posts commit to Bloom
```

Commit split: off-page slots are applied + saved in C# (`HandleCommit`); the **current page**
is returned as `{oldSrc,newSrc}` and applied by `aiEditorLauncher.ts` via `changeImageByElement`
on the live DOM, which then fires `common/saveChangesAndRethinkPageEvent` to persist it (you'll
see the shell URL gain a `?pageSrc=…` as the page rethinks).

## 5. Verify a commit actually persisted (all three "lost my edit/credits" bugs)

```bash
node .github/skills/bloom-automation/driveAiImageEditor.mjs --http-port 8092 dummy-edit
```

Then confirm three independent surfaces agree:

1. **Live DOM** — `… images` shows the canvas image's `src` = the new file, with
   `data-copyright/creator/license` intact.
2. **Storage on disk** — the book's main `*.htm` (e.g. `<book>/<name>.htm`) `coverImage`/img
   now references the **new** file with the credit attributes. If storage still shows the *old*
   file, the current-page save didn't fire (the original stale-storage bug: editor reopens on
   the old image, and a re-commit's `oldSrc` no longer matches the live page → "0 of N could be
   updated").
3. **Embedded file metadata** — `… credits` re-runs launch, which reads each image's **file**
   metadata (`GetCreditsForImageFile`). This is what a reload re-derives from, so it's the real
   test that credits survived — not just the DOM attributes.

```bash
node .github/skills/bloom-automation/driveAiImageEditor.mjs --http-port 8092 credits
node .github/skills/bloom-automation/driveAiImageEditor.mjs --http-port 8092 images
```

## Gotchas

- **Editor not offering the dummy model** → the launch payload lacks `showDeveloperTools:true`.
  Confirm with the `credits` subcommand (it prints the flag). Cause: not a dev build, or an
  older Bloom without the field.
- **`aiImageEditor/launch` returns 411 Length Required** when POSTed with no body — HTTP.sys
  needs a content length. Send `-d '{}'` (curl) or a `body` (fetch). The driver already does.
- **Clicks silently do nothing on the image** → the Comical canvas intercept; use raw mouse
  coordinates (above).
- **Editing a real book leaves a dummy banner** on that image. Use a scratch/test book, or Undo
  after. The commit is deliberately not undoable per-image, but Bloom's page Undo still reverts.
- Only drive the instance from **your** worktree (check `instanceInfo.executablePath`); a second
  developer Bloom may be running on 8089.
```
