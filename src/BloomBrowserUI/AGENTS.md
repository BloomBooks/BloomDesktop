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
