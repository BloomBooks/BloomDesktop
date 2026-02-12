# Canvas Playwright Suite Scaffold

This folder scaffolds a dedicated Canvas Playwright suite for Edit Tab behavior.

- Target URL context: `http://localhost:8089/bloom/CURRENTPAGE`
- Tests should use real drag gestures (not synthetic event dispatch).
- Use shared helpers in `helpers/` to keep tests minimal.

## Running

From `src/BloomBrowserUI`:

- `yarn e2e canvas`
- `yarn e2e canvas specs/01-toolbox-drag-to-canvas.spec.ts`

Watch tests in a visible browser:

- `yarn e2e canvas --headed`

Use Playwright UI mode for interactive reruns and debugging:

- `yarn e2e canvas --ui`

The command fails fast if `http://localhost:8089/bloom/CURRENTPAGE` is not reachable.
