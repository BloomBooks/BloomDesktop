This folder contains Canvas Tool UI and related Canvas utilities.

## Automated Canvas E2E tests
Canvas Playwright tests live in:
- `src/BloomBrowserUI/bookEdit/canvas-e2e-tests`

Run them from `src/BloomBrowserUI`:
- `cd src/BloomBrowserUI`
- `yarn install` (first time)
- `yarn e2e canvas`
- `yarn e2e canvas specs/01-toolbox-drag-to-canvas.spec.ts`

To watch the tests run in a visible browser:
- `yarn e2e canvas --headed`

To use Playwright's interactive UI (rerun and inspect while editing):
- `yarn e2e canvas --ui`

## Frame targeting rules
Bloom Edit Tab uses multiple iframes.

- Toolbox frame should be resolved by name `toolbox` (URL usually contains `toolboxContent`).
- Editable page frame should be resolved by name `page` (URL usually contains `page-memsim-...htm`).
- Do not treat top `CURRENTPAGE` frame as editable page frame.

## Canvas tool activation
- Use `h3[data-toolid="canvasTool"]` for the Canvas tab.
- Check `#canvasToolControls` first; if already visible, do not click the tab again.

## Drag/drop testing rules
- Use real Playwright mouse interactions for drag/drop.
- Do not use synthetic JS drag event dispatch as a substitute.
- Prefer assertions on DOM state changes, for example:
  - `.bloom-canvas-element` count changes
  - expected classes/attributes

## Test design guidance
- Keep tests short and scenario-focused.
- Put repeated behavior in shared helpers under `bookEdit/canvas-e2e-tests/helpers`.
- Keep selector definitions centralized.
- Do not use fragile time-based waiting without explicit user approval, recorded in a comment int the code.
- Prefer one robust helper over repeated in-spec frame/query logic.
