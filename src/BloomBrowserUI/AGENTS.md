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
- WebView2 112

## Code Style

- Always use arrow functions and function components in React

- Avoid removing existing comments.
- Avoid adding a comment like "// add this line".

- For functions, prefer typescript "function" syntax over const foo = () ==> functions.
- When writing less, use new css features supported by our current version of webview2. E.g. "is()".

- Style elements using the css macro from @emotion/react directly on the element being styled, using the css prop. E.g. `<div css={css`color:red`}>`

- We rarely use `null` in typescript, preferring `undefined` for values that have not been set. E.g.
    - YES:  `const foo?: string;`
    - YES:  `const [foo, setFoo] = useState<string>();`
    - NO: `const [foo, setFoo] = useState<string | null>(null);`

- Do not destructure props. `props.foo` is easier to understand.


## About React useEffect

See {repository root}/.github/skills/react-useeffect

If you read that and decide that a useEffect is warranted, you must add a comment justifying why it is necessary.

When the effect should run only on mount (and optionally clean up on unmount), prefer the `useMountEffect` helper in `utils/useMountEffect.ts` over a bare `useEffect(..., [])`. It states the "run on mount" intent clearly and keeps the empty-dependency-array eslint suppression in one place.

## UI Tests

We use Playwright.

Tests for components under /react_components have a playwright test system based on "*.uitest.ts" files. See src/BloomBrowserUI/react_components/AGENTS.md for more info.


Don't check for styles in tests as a way to know the status of something. That is fragile. If necessary have components add css classes or whatever that tests can check.

Don't use timeouts in tests, that slows things down and is fragile. If a timeout is justified, get my approval and add a comment explaining it.

## Troubleshooting UI Problems

Usually if you get stuck, the best thing to do is to get the component showing in a browser and use chrome-devtools-mcp to to check the DOM, the console, and if necessary a screenshot. You can add console messages that should show, then read the browser's console to test your assumptions. If you want access to chrome-devtools-mcp and don't have it, stop and ask me.

## Localization

Localizable strings live in xlf files under `DistFiles/localization/`. We write the English in
`en/Bloom*.xlf`; translators work in Crowdin, and their work lands in the other language
subdirectories.

**Two documents own this subject; read the relevant one rather than working from memory.**

- **`.github/skills/xlf-strings/SKILL.md`** — how to add, change, review, or retire a string:
  which priority file to use, the note conventions, and the checks each operation needs. Open it
  whenever you touch an XLF entry.
- **`DistFiles/localization/README.md`** — how Crowdin actually works, and *why* these rules
  exist: what each kind of xliff edit does to existing translations, and (in "Why we can't just
  delete a string") the route translations travel from Crowdin through master to a release
  branch. Read it before concluding that any deletion or id change is harmless.

The rules themselves, which apply whether or not you have opened those:

- **Only ever edit `DistFiles/localization/en/`.** Never touch the other language subdirectories,
  and never touch an existing translation.
- **Never pick the priority file yourself.** Which of `Bloom.xlf` /
  `BloomMediumPriority.xlf` / `BloomLowPriority.xlf` a new string belongs in is the
  developer's call, not yours. Stop and ask, offering a recommendation and a reason; do not
  infer it from where neighbouring ids happen to live. This applies however you arrived at
  adding the string -- including when a task that started as something else turns into
  adding one.
- **Do not change the `@id` of a `<trans-unit>`** unless it is marked `@translate="no"`. Changing
  an id loses its translations. If asked to do it anyway, refuse; if you notice it during a
  review, point it out.
- **Do not delete a `<trans-unit>` that is no longer used.** Mark it obsolete instead; the
  skill has the exact note format and where to read the current version number.
- **Only mark an entry obsolete once nothing references it.** Check first — code (`l10nKey` /
  `l10nId` / `useL10n` / `GetString`), shipped content under `src/content` (sample shells are
  `.htm`, and page label ids are composed at runtime as `"TemplateBooks.PageLabel." + label`),
  and the rest of the XLF. A note claiming a live string is obsolete is worse than no note: it
  invites the next person to delete a string we are still using.
- **Never delete an entry on your own initiative**, even an obsolete one, and even when you are
  confident it is safe. There is exactly one case where deletion loses nothing — a string that
  was always `translate="no"` and so never reached Crowdin — and even then it is the developer's
  decision, the evidence has to go in the commit message and the PR reply, and the skill has the
  commands that establish it.

## Other notes

- When code makes changes to the editable page dom using asynchronous operations, it should use wrapWithRequestPageContentDelay to make sure any requests for page content wait until the async tasks complete. Check this in code reviews also.

## Building / testing the front-end while Bloom is running

The developer usually launches Bloom with `./go.sh`, which starts a **Vite dev server** and
has Bloom's WebView2 load the UI from it (not from a `vite build --watch`). Two consequences:

- **Editing `.ts`/`.tsx`/`.less` needs no build at all.** The dev server pushes your change
  into the running Bloom; to see it, attach and observe via the `bloom-automation` skill — do
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

The full `pnpm build` exists to (re)populate the shared `output\browser` — `clean.js` plus content assets plus the bundle. It's slow, and it wrecks any running Vite dev server / `--watch` and the Bloom loading from it, so it's a developer/CI job, not something to spring on a live session. If you think you genuinely need it, ask the developer to run it (they can stop Bloom first) rather than running it yourself.

### If the front-end test suite seems to hang, re-run it with `--no-file-parallelism`

On some machines `yarn test` (`vitest run`) gets through roughly fifteen test files and then
stops dead — no error, no failing test, no summary — until something kills it. That is vitest's
worker pool wedging, **not** a broken test and not the branch you are on: run the files one at a
time and the whole suite completes green.

```bash
cd src/BloomBrowserUI && yarn vitest run --no-file-parallelism
```

So before reporting the suite as hanging or failing, re-run it that way and report *that* result.
Do not go hunting for the "test that hangs" — it moves. Excluding whichever file it stopped after
just relocates the stall to a different one.
