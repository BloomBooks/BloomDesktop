# Bloom component-tester

Canonical entry point:

-   `src/BloomBrowserUI/react_components/component-tester`

Helpful commands:

-   `yarn dev` (start harness)
-   `yarn test` / `yarn test:headed` (Playwright)
-   `cd src/BloomBrowserUI/react_components/<component> && yarn scope [exportName]` (shared remote-debugging manual workflow)
-   `cd src/BloomBrowserUI/react_components/<component> && yarn scope --backend [exportName]` (use real Bloom backend)
