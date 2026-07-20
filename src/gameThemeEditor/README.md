# bloom-game-theme-editor

A **self-contained** editor for Bloom *game themes* — named sets of CSS custom
properties (`--game-primary-color`, `--game-button-bg-color`, …) that style
drag-activity game pages.

This is its own project directory on purpose. It depends on **nothing** in
`BloomBrowserUI`; the only coupling to a host application is the small contract in
[`src/host/IGameThemeEditorHost.ts`](src/host/IGameThemeEditorHost.ts) plus the
`mount()` / `unmount()` functions exported from [`src/index.tsx`](src/index.tsx).

## How it is consumed

A host (in Bloom: the editable-page iframe — see
`BloomBrowserUI/bookEdit/toolbox/games/gameThemeEditorHost.ts`) does:

```ts
import { mount, unmount } from "gameThemeEditor"; // vite alias → this project's src
mount(containerDiv, host /* : IGameThemeEditorHost */);
```

The host object owns everything application-specific:

- **Live preview** — `setLiveVariable(name, value)` sets the CSS custom property on the
  live page. The browser's native cascade (see `gamesThemes.less`'s `.apply-game-theme()`)
  derives every dependent variable, so the real game recolors instantly. There is
  deliberately **no preview component** in this project.
- **Persistence** — `saveToCollection` / `saveToFactorySource` (developer-only) /
  `canSaveToFactorySource`.
- **Seeding** — `readAppliedVariables` (computed colors) and `getThemeDefinition`
  (which variables a theme sets explicitly).

## What this project intentionally does NOT contain

Compared to the original standalone SPA it was ported from, the following were dropped
because the host's browser does the work natively or they are not wanted:

- the JS cascade-resolution engine (`resolveVariableValue`, derivation maps),
- the CSS view and copy/paste-CSS parser,
- the mock preview component, `html2canvas`, Tailwind, and the shadcn UI library.

## Build

For now the project has no independent build step in Bloom; the BloomBrowserUI Vite build
consumes its TypeScript source directly via a `resolve.alias` (`gameThemeEditor`). Its
`package.json` records its identity and dependencies so it can later be promoted to a real
workspace if install-level isolation is wanted.
