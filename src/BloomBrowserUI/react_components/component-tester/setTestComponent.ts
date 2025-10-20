/**
 * This is needed because the playwright test is running in a different process than the browser that we
 * are rendering the component in. And if we try to use the actual component in the test, then that imports
 * react and then thinks we have a window but we don't (this is just nodejs), etc.
 *
 * So, we have this helper that uses playwright to manipulate the browser page to tell it what component to render
 * and with what props.
 *
 * See registration/e2e/setup.ts for an example of how to wrap this for a specific component with type checking.
 */

import { Page } from "@playwright/test";

/**
 * Sets the React component to render in the test harness with full type checking.
 *
 * @param page - The Playwright page object
 * @param componentName - Name of the component to render (must match a key in component-harness.tsx component map)
 * @param props - Component props with full TypeScript type checking
 */
export async function setTestComponent<TProps>(
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
