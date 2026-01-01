These Playwright-driven ui tests are not currently run as part of CI or other script. Once we have switched the build to vite, then
presumably we can alter playwright.config.ts to work with that system. For now, this is just useful for developer
testing.

### Setting up component testing

Components are automatically discovered from `component-tester.config.ts` files in component folders.

To set up a component for ui testing:
1. The component must have its own folder under react_components, "react_components/<component-name>"
2. Add a "react_components/<component-name>/component-tests" folder
3. Create a "react_components/<component-name>/component-tests/component-tester.config.ts" file. See "src/BloomBrowserUI/react_components/registration/component-tests/component-tester.config.ts" for an example.
4. Create a common.ts file where you can store common methods used by tests of this component. The most important thing is to provide a function that wraps `setTestComponent()`. See "/src/BloomBrowserUI/react_components/registration/component-tests/common.ts " for an example.
5. Create playwright tests named "*.uitest.ts";

## Complications

### Imports
`vite dev` has to be able to handle your component. Eventually that will not be a big deal, but for now, it may mean that you have to extract out the core of it with few imports. For the RegistrationDialog, we had to extract out the core behavior and test that.

### Callback Props
Component props must be JSON-serializable because the Playwright test runs in a different process than the browser. Functions cannot be serialized.

**Solution**: Change the prop to accept either a function or a string URL:
```typescript
onSomethingChanged: ((data: MyData) => void) | string
```

In the component, check the type and POST to the URL if it's a string:
```typescript
const notifyChange = (data: MyData) => {
    if (typeof props.onSomethingChanged === "string") {
        void postJson(props.onSomethingChanged, data);
    } else {
        props.onSomethingChanged(data);
    }
};
```

In tests, use `preparePostReceiver` to intercept and verify the data. See `bookLinkSetup/component-tests` for a complete example.

### API calls
These are fine, see `apiInterceptors.ts`

### L10n components
These are fine. This system tells the bloom l10n system to just return English.

## Running react_components tests

```bash
cd src/BloomBrowserUI/react_components/component-tester

# Install dependencies (first time only)
yarn install

# Start the dev server
yarn dev

# Run automated tests
yarn test              # headless
yarn test:headed       # see browser
yarn test:ui          # interactive UI

# Play with a component manually
./show.sh
./show-with-bloom.sh

# Play with a component manually in a shared, remote-debugging-enabled browser session
# (recommended when you want automation tools to interact with the same tab)
./show-scope.sh <modulePath> <exportName>

# By default, if the dev server is not already running, this script will start it in the
# current terminal and keep the terminal busy (Ctrl+C stops the dev server).
# Use --detach to start the dev server in the background instead.
# ./show-scope.sh --detach <modulePath> <exportName>

This prints the component URL and the Vite harness base URL (host/port), which helps debug whether the dev server is still running.
```

### Component-local test helpers

Every component folder includes a `test.sh` wrapper that runs Playwright against just that component's specs. Manual Playwright suites are excluded automatically.

- `./test.sh` &mdash; run the entire component suite (manual specs are skipped).
- `./test.sh component-tests/url-sync-preselection.uitest.ts` &mdash; run a single file.
- `./test.sh component-tests/url-sync-preselection.uitest.ts -g "Scrolls preselected book into view"` &mdash; run one test case by name.
- `./test.sh --grep "URL Synchronization"` &mdash; run tests matching a pattern.

Relative file paths are resolved inside the component folder, so you do not need to prefix them with `LinkTargetChooser/` when invoking the script.


## APIs
For testing components that get or submit data via APIs, use the methods provided by `apiInterceptors.ts`.

### What's Mocked

The dev server mocks:
- **Bloom APIs** - Channel info and error reporting endpoints (mocked in vite.config.ts)
- **Localization** - Bypassed via bypassLocalization() so English strings are returned directly
