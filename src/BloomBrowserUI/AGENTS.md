This contains instructions specifically for the browser UI. Also read the AGENTS.md file at the root of this workspace.

# Front-end
## Directory
When working in the front-end, cd to src/BloomBrowserUI

## Stack
- typescript
- react
- MUI
- Emotion
- pnpm 11.5.2
- Never use npm or yarn commands
- Never use CDNs. This is an offline app.
- WebView2 (the Evergreen runtime; the SDK version is pinned in `src/BloomExe/BloomExe.csproj`)

## Code Style

- React components are arrow function components (`export const Foo: React.FunctionComponent<{…}> = (props) => {…}`), as the root `AGENTS.md` says.
- For other top-level functions (helpers, utilities), prefer a `function foo() {}` declaration over `const foo = () => {}`.

- Avoid removing existing comments.
- Avoid adding a comment like "// add this line".

- When writing less, use new css features supported by our current version of webview2. E.g. "is()".

- Style elements using the css macro from @emotion/react directly on the element being styled, using the css prop. E.g. `<div css={css`color:red`}>`

- We rarely use `null` in typescript, preferring `undefined` for values that have not been set. E.g.
    - YES:  `const foo?: string;`
    - YES:  `const [foo, setFoo] = useState<string>();`
    - NO: `const [foo, setFoo] = useState<string | null>(null);`

- Do not destructure props. `props.foo` is easier to understand.


## About React useEffect

See {repository root}/.claude/skills/react-useeffect

If you read that and decide that a useEffect is warranted, you must add a comment justifying why it is necessary.

When the effect should run only on mount (and optionally clean up on unmount), prefer the `useMountEffect` helper in `utils/useMountEffect.ts` over a bare `useEffect(..., [])`. It states the "run on mount" intent clearly and keeps the empty-dependency-array eslint suppression in one place.

## UI Tests

We use Playwright.

Tests for components under /react_components have a playwright test system based on "*.uitest.ts" files. See src/BloomBrowserUI/react_components/component-tester/README.md and the `component-test` skill.


Don't check for styles in tests as a way to know the status of something. That is fragile. If necessary have components add css classes or whatever that tests can check.

Don't use timeouts in tests, that slows things down and is fragile. If a timeout is justified, get my approval and add a comment explaining it.

## Troubleshooting UI Problems

Usually if you get stuck, the best thing to do is to look at the real thing: attach to the running Bloom's WebView2 over CDP with the `run-bloom` skill (DOM, console, network, screenshots), or get the component showing in the component-tester harness (`component-test` skill). Add console messages that should show, then read the browser's console to test your assumptions. When the backend is running, you can open http://localhost:8089/bloom/CURRENTPAGE to inspect and interact with the screen.

## Localization

Localizable strings live in `DistFiles/localization/en/Bloom*.xlf`. Whenever you add, change,
review or retire one, follow `.claude/skills/xlf-strings/SKILL.md`; the root `AGENTS.md` states
the two rules that hold even outside that skill (edit only `en/`; never delete a `<trans-unit>`).

## Other notes

- When code makes changes to the editable page dom using asynchronous operations, it should use wrapWithRequestPageContentDelay to make sure any requests for page content wait until the async tasks complete. Check this in code reviews also.

## Building / testing the front-end while Bloom is running

The developer usually launches Bloom with `./go.sh`, which starts a **Vite dev server** and
has Bloom's WebView2 load the UI from it (not from a `vite build --watch`). Two consequences:

- **Editing `.ts`/`.tsx`/`.less` needs no build at all.** The dev server pushes your change
  into the running Bloom; to see it, attach and observe via the `run-bloom` skill — do
  **not** build. How the change lands varies: a `.less`/CSS edit hot-swaps in place (no
  reload); a `.tsx` edit often triggers a Vite full page reload (React Fast Refresh falls back
  to it), and for app-shell / entry components that reload briefly blanks the view until Bloom
  re-navigates. So when observing over CDP, wait for the page to settle (or switch tabs and
  back) before concluding an edit "didn't apply". (A few entry points aren't served by the dev
  server and rely on a separate `pnpm watch` = `vite build --watch`; if the developer is
  running that instead, your edits are still rebuilt for you — you still don't build.)
- **Don't run `pnpm build` here** (see below): it wipes and repopulates the shared
  `output\browser` via `clean.js`, disrupting the Bloom running against it, and it does
  nothing useful anyway because the running Bloom loads JS from the dev server, not from
  `output\browser`.

**Automated front-end checks are always safe — run them freely.** None of these build or
touch `output\browser`, so they never disturb the dev server or a watch:

- `pnpm test` (Vitest) — runs in jsdom and transforms modules in memory. This is your primary
  "does my logic/component work" check. (`pnpm lint` and `pnpm typecheck` are likewise safe.)

**To confirm the real production bundle compiles** — bundling / CommonJS-interop errors and
the manifest post-build step that the lenient dev server never exercises — use the isolated
wrapper, the front-end twin of `build/agent-dotnet.sh`:

```bash
build/agent-vite.sh
```

(PowerShell: `build/agent-vite.ps1`.) It sets `BLOOM_UI_OUTDIR` so the whole Vite build lands
in a private per-terminal tree under `output/agent/<key>/browser`, never touching the shared
`output\browser` or any running dev server / watch, so multiple terminals can run it at once.
Like the C# wrapper it is **build-only**: it confirms the bundle compiles; it does *not* let a
running Bloom load those bundles (Bloom reads the fixed `output\browser` / dev server). It
skips the pug/LESS/markdown/static-copy steps, so it is a fast pure-bundle check.

### Don't run the full `pnpm build` yourself

You have a complete set of faster, non-disruptive alternatives, so don't run the full `pnpm build`:
- **Checks** — `pnpm lint`, `pnpm typecheck`, `pnpm test`. None of these build or touch `output\browser`.
- **Confirm the real production bundle compiles** — `build/agent-vite.sh`, which builds into an isolated tree and leaves `output\browser` alone.
- **See a change in the running Bloom** — just edit the source; the dev server pushes it in. No build.

The full `pnpm build` exists to (re)populate the shared `output\browser` — `clean.js` plus content assets plus the bundle. It's slow, and it wrecks any running Vite dev server / `--watch` and the Bloom loading from it, so it's a developer/CI job, not something to spring on a live session.

**"Live" means live in *this* worktree.** Each worktree has its own `output\browser`, so a
`Bloom.exe` or dev server belonging to another one is irrelevant — on a machine with many
worktrees, a bare "is Bloom running?" is the wrong question. Check whether a process is using the
tree you are about to rebuild:

```powershell
Get-CimInstance Win32_Process -Filter "Name='Bloom.exe' OR Name='node.exe'" |
  Where-Object { $_.CommandLine -like "*$(Get-Location)*" } | Select-Object CommandLine
```

If something here is live, it is the developer's call — ask them; they can stop Bloom first. If
nothing is, run it when you genuinely need it, and say that you did. (The usual trigger is the e2e
suite refusing to run against a stale bundle. `BLOOM_E2E_VITE_PORT` with a dev server tests the
working tree without a rebuild — see `src/BloomE2E/README.md`.)

### If the front-end test suite seems to hang

On some machines `pnpm test` (`vitest run`) has stopped dead part way through the files — no
error, no failing test, no summary. That is vitest's worker pool wedging, **not** a broken test
and not the branch you are on. `vite.config.mts` now sets `pool: "threads"`, which is the
configuration that has run the whole suite clean where the default forks pool wedged. If it still
stalls, re-run with fewer workers rather than hunting for "the test that hangs" (it moves):

```bash
# from src/BloomBrowserUI
pnpm exec vitest run --no-file-parallelism
```

Report *that* result. Never use `yarn` here.
