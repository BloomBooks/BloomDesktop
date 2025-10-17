/**
 * Helper for testing React components with Playwright.
 *
 * Pass component props with full type checking.
 *
 * Usage:
 * ```typescript
 * import type { MyComponentProps } from "../../myComponent";
 *
 * await setTestComponent<MyComponentProps>(page, "MyComponent", {
 *     someProp: "value",
 *     onSomeEvent: () => {},
 *     // ... TypeScript will check all props!
 * });
 * ```
 */

import { Page } from "@playwright/test";

/**
 * Sets the React component to render in the test harness with full type checking.
 *
 * @param page - The Playwright page object
 * @param componentName - Name of the component to render (must match a key in component-harness.tsx component map)
 * @param props - Component props with full TypeScript type checking
 */
export async function setTestComponent<TProps = any>(
    page: Page,
    componentName: string,
    props: TProps,
): Promise<void> {
    // Inject test configuration before the page loads
    await page.addInitScript((config) => {
        (window as any).__TEST_CONFIG__ = config;
    });

    // Serialize the component and props
    const elementJson = JSON.stringify({
        type: componentName,
        props: props,
    });

    // Inject the element data before the page loads
    await page.addInitScript((json) => {
        (window as any).__TEST_ELEMENT__ = JSON.parse(json);
    }, elementJson);

    // Navigate to the dev server
    await page.goto("/");

    // Wait for React to render
    await page.waitForLoadState("networkidle");
}
