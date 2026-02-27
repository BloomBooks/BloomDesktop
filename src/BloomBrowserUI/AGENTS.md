This contains instructions specifically for the browser UI. Also read the AGENTS.md file at the root of this workspace.

# Front-end
## Directory
When working in the front-end, cd to src/BloomBrowserUI

## Stack
- typescript
- react
- MUI
- Emotion
- yarn 1.22.22
- Never use npm commands
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
Rule 1 — Use useEffect when synchronizing with external systems:

Subscriptions, timers, or event listeners.

API calls or other asynchronous external operations.

Updates to things outside React control (e.g., document.title, localStorage).

Any side effect that cannot be computed during render.

Rule 2 — Avoid useEffect when data can be derived or handled internally:

State can be derived from props, context, or other state — compute in render.

User interactions can be handled directly in event handlers.

Local state reset/initialization can be handled by component keys or conditional rendering.

Computed values can use useMemo or useCallback instead of syncing in an effect.

Rule 3 — Validation heuristic:

If removing the effect does not break external behavior, the effect is unnecessary.

Implementation Tip for AI:

Prefer pure render computation first.

Add useEffect only when necessary for external side effects.

Keep effects minimal and specific to their purpose; avoid overuse.

Always include a comment before a useEffect explaining what it does and why it is necessary.

## UI Tests

We use Playwright.

Tests for components under /react_components have a playwright test system based on "*.uitest.ts" files. See src/BloomBrowserUI/react_components/AGENTS.md for more info.


Don't check for styles in tests as a way to know the status of something. That is fragile. If necessary have components add css classes or whatever that tests can check.

Don't use timeouts in tests, that slows things down and is fragile. If a timeout is justified, get my approval and add a comment explaining it.

## Troubleshooting UI Problems

Usually if you get stuck, the best thing to do is to get the component showing in a browser and use chrome-devtools-mcp to to check the DOM, the console, and if necessary a screenshot. You can add console messages that should show, then read the browser's console to test your assumptions. If you want access to chrome-devtools-mcp and don't have it, stop and ask me.

## Other notes

- When code makes changes to the editable page dom using asynchronous operations, it should use wrapWithRequestPageContentDelay to make sure any requests for page content wait until the async tasks complete. Check this in code reviews also.
