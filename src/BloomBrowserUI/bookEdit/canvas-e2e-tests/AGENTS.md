# Canvas Playwright Suite Scaffold

This folder contains a dedicated Canvas Playwright suite for behavior of working with a canvas on a page and interacting with the Canvas Tool in the toolbox.

- Target URL context: `http://localhost:8089/bloom/CURRENTPAGE`
- Tests should use real drag gestures (not synthetic event dispatch).
- Use shared helpers in `helpers/` to keep tests minimal.

## Running

From `src/BloomBrowserUI`:

- `pnpm e2e canvas`
- `pnpm e2e canvas specs/01-toolbox-drag-to-canvas.spec.ts`

Execution mode:

- Default (`shared`): one browser page is reused and each test cleans canvas elements back to baseline. This is much faster because page loads are slow.
- Optional (`isolated`): each test gets a fresh page load.
- Shared mode defaults to `--workers=1` so the whole run stays on one page (override by passing `--workers`).

Mode flags:

- `pnpm e2e canvas --shared`
- `pnpm e2e canvas --isolated`

Watch tests in a visible browser:

- `pnpm e2e canvas --headed`

Use Playwright UI mode for interactive reruns and debugging:

- `pnpm e2e canvas --ui`

The command fails fast if `http://localhost:8089/bloom/CURRENTPAGE` is not reachable.

Set `BLOOM_CANVAS_E2E_URL` to target a Bloom on another port. 8089 is
first-come-first-served across worktrees (Bloom falls forward to the next
port block when it is taken), so before trusting a run, verify which Bloom
owns the port you are driving — launcher `--status` in the right worktree, or
`bloomProcessStatus.mjs --running-bloom` — or you may be testing another
worktree's build.

## Frame model and selectors

Bloom's Edit tab is several iframes. Resolve frames by **name**, never by position:

- Toolbox frame: name `toolbox` (URL usually contains `toolboxContent`).
- Editable page frame: name `page` (URL usually contains `page-memsim-...htm`).
- The top `CURRENTPAGE` document is the shell, not the editable page.

Selectors the helpers rely on (keep them centralized in `helpers/`):

- Canvas tool tab header: `h3[data-toolid="canvasTool"]`. Check whether `#canvasToolControls`
  is already visible first; if it is, do not click the tab again.
- Canvas surface: `.bloom-canvas`. Created elements: `.bloom-canvas-element`.
- Speech/comic palette item: `img[src*="comic-icon.svg"]`.

A minimal non-trivial proof test opens `CURRENTPAGE`, resolves the two frames, ensures the Canvas
tool is active, drags a palette item onto `.bloom-canvas` with real mouse gestures
(`page.mouse.down/move/up`), and asserts the `.bloom-canvas-element` count increased.

Troubleshooting: "No tests found" means the path filter is not relative to the config
`testDir`; `playwright: not found` means `pnpm install` in `src/BloomBrowserUI`; a canvas wait
that times out usually means you selected the top frame instead of `page`.

## Native dialogs (safety rule)

Do **not** let a native OS dialog open unprepared: Playwright cannot see or dismiss it and the
run hangs. `Change image` and `Choose image from your computer...` are safe (they open the web
image gallery); the gallery's `Open File...` under `This Computer`, and either command on a GIF,
reach the native file picker. This suite attaches to an ordinary running Bloom, not one started
with `--e2e`, so the `e2e/nextFileToChoose` hook is **not** registered (posting to it raises
Bloom's missing-endpoint problem dialog). To get past a picker here use `winformsUia.ps1` in the
`bloom-automation` skill, which fills the picker over UI Automation with no pointer input. Do not
invoke `Choose Video from your Computer...` or `Record yourself...`; if coverage needs them,
verify presence/enabled state only.

## Stability notes for future agents

- Shared mode teardown is implemented in fixtures using `CanvasElementManager` APIs (not click-based selection), because overlay canvases can intercept pointer events.
- Prefer visible-only locators for context controls and menu lists (`:visible`), because hidden duplicate portal/menu nodes can appear during long headed runs.
- Keep real drag/drop for tests that validate drag behavior.
- Prefer close-to-user-behavior setup in specs: create the same element type the test is validating, using real drag/drop.
- If a test is flaky, prefer bounded retries around the same user-like interaction. Any any non-user-like setup shortcuts require explicit human approval, recorded in a code comment. For example, avoid substituting different element types just to reduce flakiness unless explicitly approved and clearly documented in the spec.
- `specs/11-shared-mode-cleanup.spec.ts` is a regression check that shared-mode per-test cleanup restores baseline element count.


## Creating tests

- Keep tests minimal by moving complexity into shared helpers.
- Group coverage by behavior and by underlying canvas modules.
- Use real Playwright drag gestures (no synthetic JS drag/drop dispatch).
- Prefer semantic assertions over style-only assertions.
- Keep design helper-first and data-driven (avoid repetitive long test bodies).
- You are encouraged to add `data-test-id` attributes to elements (by modifying the react or other code) as needed if helpful in selecting them.
