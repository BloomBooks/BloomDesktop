These Playwright-driven ui tests are not currently run as part of CI or other script. Once we have switched the build to vite, then
presumably we can alter playwright.config.ts to work with that system. For now, this is just useful for developer
testing.

## Running react_components tests

```bash
cd src/BloomBrowserUI/react_components/component-tester

# Install dependencies (first time only)
yarn install

# Manual browser testing
yarn dev

# Run automated tests
yarn test              # headless
yarn test:headed       # see browser
yarn test:ui          # interactive UI
```

### What's Mocked

The dev server mocks:
- **Bloom APIs** - Channel info and error reporting endpoints (mocked in vite.config.ts)
- **Localization** - Bypassed via bypassLocalization() so English strings are returned directly
