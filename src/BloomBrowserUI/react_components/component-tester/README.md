These Playwright-driven ui tests are not currently run as part of CI or other script. Once we have switched the build to vite, then
presumably we can alter playwright.config.ts to work with that system. For now, this is just useful for developer
testing.

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
yarn manual # will list all the components
yarn manual StarChart # open to a specific component
```

### Working with components

Components are automatically discovered from `bloom-component.config.ts` files in component folders.

To add a new component to the test harness:
1. Create `bloom-component.config.ts` in your component's folder (or `e2e` subfolder)
2. Export a default config object that implements `IBloomComponentConfig` with:
   - `modulePath`: relative path to your component module
   - `exportName`: the exported component name
   - `defaultProps`: default props for testing
3. Add *.e2e.spec.ts files.

**For testing components that get or submit data via APIs**, the methods provided by apiInterceptors.ts.

To launch a component:
- `yarn manual` - lists all available components and opens the dev server
- `yarn manual ComponentName` - starts dev server and opens that specific component
- Or use URLs directly: `http://127.0.0.1:5173/?component=ComponentName`

To customize props for manual testing, edit the `defaultProps` in your component's `bloom-component.config.ts` file.

### What's Mocked

The dev server mocks:
- **Bloom APIs** - Channel info and error reporting endpoints (mocked in vite.config.ts)
- **Localization** - Bypassed via bypassLocalization() so English strings are returned directly
