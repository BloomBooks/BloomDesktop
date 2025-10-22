/**
 * Utilities for intercepting API calls in Playwright tests.
 * This allows tests to capture results that would be sent to backend APIs.
 */

import { Page, Route } from "@playwright/test";

/**
 * Intercepts POST requests to a given URL pattern and captures the request body.
 * Returns a function that waits for and returns the captured data.
 *
 * Example Usage:
 * ```typescript
 * const receiver = preparePostReceiver<RegistrationInfo>("/bloom/api/registration/userInfo");
 * await page.click('button[name="Register"]');
 * const result = await receiver();
 * expect(result.email).toBe("foo@example.com");
 * ```
 *
 * @param page - The Playwright page object
 * @param urlPattern - URL or pattern to intercept (e.g., "/bloom/api/registration/userInfo" or "**\/api/save")
 * @returns A function that waits for the request and returns the parsed POST data
 */
export function preparePostReceiver<T>(
    page: Page,
    urlPattern: string,
): () => Promise<T> {
    let capturedData: T | undefined;
    let resolvePromise: ((value: T) => void) | undefined;
    let rejectPromise: ((reason: any) => void) | undefined;

    const waitPromise = new Promise<T>((resolve, reject) => {
        resolvePromise = resolve;
        rejectPromise = reject;
    });

    // Set up the route interception immediately
    void page.route(urlPattern, async (route: Route) => {
        try {
            const postData = route.request().postDataJSON() as T;
            capturedData = postData;

            await route.fulfill({
                status: 200,
                contentType: "application/json",
                body: JSON.stringify({ success: true }),
            });

            // Resolve the wait promise
            resolvePromise?.(postData);
        } catch (error) {
            rejectPromise?.(error);
            // Still fulfill the route to not break the page
            await route.fulfill({
                status: 500,
                contentType: "application/json",
                body: JSON.stringify({ error: "Test interceptor error" }),
            });
        }
    });

    // Return a function that waits for the captured data
    return async () => {
        // If already captured, return immediately
        if (capturedData !== undefined) {
            return capturedData;
        }
        // Otherwise wait for it
        return await waitPromise;
    };
}

/**
 * Intercepts GET requests to a given URL pattern and returns a mock response.
 * This allows tests to provide fake data for API endpoints.
 *
 * @param page - The Playwright page object
 * @param urlPattern - URL or pattern to intercept (e.g., "/bloom/api/data" or "**\/api/data")
 * @param responseData - The data to return in the response
 */
export function prepareGetResponse<T>(
    page: Page,
    urlPattern: string,
    responseData: T,
): void {
    void page.route(urlPattern, async (route: Route) => {
        await route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify({ data: responseData }),
        });
    });
}

/**
 * Intercepts GET requests to a given URL pattern and returns a boolean response.
 * This is specifically for Bloom API endpoints that return boolean values directly.
 *
 * @param page - The Playwright page object
 * @param urlPattern - URL or pattern to intercept (e.g., "/bloom/api/check" or "**\/api/check")
 * @param responseValue - The boolean value to return
 */
export function prepareGetBooleanResponse(
    page: Page,
    urlPattern: string,
    responseValue: boolean,
): void {
    void page.route(urlPattern, async (route: Route) => {
        await route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify({ data: responseValue }),
        });
    });
}
