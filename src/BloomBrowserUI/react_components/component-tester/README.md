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
- **jQuery** - The localization system uses jQuery promises (mocked in component-harness.tsx)
- **Bloom APIs** - Channel info and error reporting endpoints (mocked in vite.config.ts)
- **Localization** - Bypassed via bypassLocalization() so English strings are returned directly




