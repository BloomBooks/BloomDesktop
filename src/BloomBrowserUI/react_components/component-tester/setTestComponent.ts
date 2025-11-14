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
import { ComponentRenderRequest } from "./componentTypes";
import { bypassLocalization } from "../../lib/localizationManager/localizationManager";

bypassLocalization(true);

/**
 * Sets the React component to render in the test harness with full type checking.
 *
 * @param page - The Playwright page object
 * @param modulePath - The path to the module that exports the component
 * @param exportName - The named export of the component (optional, defaults to default export)
 * @param props - Component props (must be JSON-serializable)
 */
export async function setTestComponent<TProps>(
    page: Page,
    modulePath: string,
    exportName: string | undefined,
    props: TProps,
): Promise<void> {
    // Serialize the component and props
    const renderRequest: ComponentRenderRequest<TProps> = {
        descriptor: {
            modulePath,
            exportName,
        },
        props,
    };

    let elementJson: string;
    try {
        elementJson = JSON.stringify(renderRequest);
    } catch (error) {
        throw new Error(
            `setTestComponent props must be JSON-serializable. ${String(error)}`,
        );
    }

    // Inject the element data before the page loads
    await page.addInitScript((json) => {
        (window as any).__TEST_ELEMENT__ = JSON.parse(json);
    }, elementJson);

    // Navigate to the dev server
    await page.goto("/");

    // Wait for React to render
    await page.waitForLoadState("load");
}
