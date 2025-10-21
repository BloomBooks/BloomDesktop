This folder has a package.json, vite config, and playwright config.
It serves as a kind of super-light-weight storybook for testing components with playwright or manually.
Components are sibling folders. They are automatically discovered by component-registry.ts, which uses Vite's import.meta.glob to find all component-tester-config.ts files in sibling directories.

To add a new component:
1. Create a component-tester-config.ts file in your component's folder (or e2e subfolder)
2. Export defaultProps object, modulePath, and exportName from that file
3. That's it! The component will be automatically discovered and available for testing.
