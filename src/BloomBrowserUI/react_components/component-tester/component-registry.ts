// This registry is used by the component-under-test process (the vite dev server)
// to dynamically load and render components for testing.
// It is NOT used by the playwright test process.
//
// To add a new component to the test harness:
// 1. Create a component-test.config.ts file in your component's folder (or e2e subfolder)
// 2. Export a default object that implements IBloomComponentConfig
// 3. That's it! The config will be automatically discovered and registered.

import {
    ComponentRegistryEntry,
    ComponentRenderRequest,
    IBloomComponentConfig,
} from "./componentTypes";

// Use Vite's import.meta.glob to automatically discover all component-tester.config.ts files
// The pattern must be relative to this file's location
// This file is in: component-tester/component-registry.ts
// We want to find: */component-tester.config.ts and */e2e/component-tester.config.ts
// Since we're in component-tester/, we go up one level (..) to reach react_components/
const configModules = import.meta.glob<{
    default: IBloomComponentConfig<any>;
}>(["../**/component-tester.config.ts"], { eager: true });

// Build the registry from discovered configs
const componentRegistryInternal: Record<
    string,
    ComponentRegistryEntry<any>
> = {};

for (const [path, module] of Object.entries(configModules)) {
    const config = module.default;
    if (config && config.modulePath && config.defaultProps) {
        const componentName = config.exportName || "";
        if (componentName) {
            componentRegistryInternal[componentName] = () => ({
                descriptor: {
                    modulePath: config.modulePath,
                    exportName: config.exportName,
                },
                props: structuredClone(config.defaultProps),
            });
        }
    }
}

export const componentRegistry = componentRegistryInternal;

export const listComponentNames = (): string[] =>
    Object.keys(componentRegistryInternal).sort();

export const getComponentRequestByName = (
    name: string,
): ComponentRenderRequest<any> | undefined => {
    const factory = componentRegistryInternal[name];
    if (!factory) {
        return undefined;
    }
    return factory();
};
