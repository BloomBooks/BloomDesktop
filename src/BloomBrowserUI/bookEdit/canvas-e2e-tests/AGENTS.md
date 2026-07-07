# Canvas Playwright Suite Scaffold

This folder contains a dedicated Canvas Playwright suite for behavior of working with a canvas on a page and interacting with the Canvas Tool in the toolbox.

- Target URL context: `http://localhost:8089/bloom/CURRENTPAGE`
- Tests should use real drag gestures (not synthetic event dispatch).
- Use shared helpers in `helpers/` to keep tests minimal.

## Running

From `src/BloomBrowserUI`:

- `yarn e2e canvas`
- `yarn e2e canvas specs/01-toolbox-drag-to-canvas.spec.ts`

Execution mode:

- Default (`shared`): one browser page is reused and each test cleans canvas elements back to baseline. This is much faster because page loads are slow.
- Optional (`isolated`): each test gets a fresh page load.
- Shared mode defaults to `--workers=1` so the whole run stays on one page (override by passing `--workers`).

Mode flags:

- `yarn e2e canvas --shared`
- `yarn e2e canvas --isolated`

Watch tests in a visible browser:

- `yarn e2e canvas --headed`

Use Playwright UI mode for interactive reruns and debugging:

- `yarn e2e canvas --ui`

The command fails fast if `http://localhost:8089/bloom/CURRENTPAGE` is not reachable.

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
