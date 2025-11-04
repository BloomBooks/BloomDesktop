/**
 * Utilities for intercepting API calls in Playwright tests.
 * This allows tests to capture results that would be sent to backend APIs.
 */

import { Page, Route } from "@playwright/test";

/**
 * Object returned by preparePostReceiver with methods to interact with the intercepted POST request
 */
export interface PostReceiver<T> {
    /**
     * Wait for and return the captured POST data
     */
    getData: () => Promise<T>;

    /**
     * Check if the POST request was called (non-blocking)
     */
    wasCalled: () => boolean;
}

/**
 * Intercepts POST requests to a given URL pattern and captures the request body.
 * Returns an object with methods to check if the request was made and get the data.
 *
 * Example Usage:
 * ```typescript
 * const receiver = preparePostReceiver<RegistrationInfo>("/bloom/api/registration/userInfo");
 * await page.click('button[name="Register"]');
 * const result = await receiver.getData();
 * expect(result.email).toBe("foo@example.com");
 *
 * // Or to check if it wasn't called:
 * await page.click('button[name="Register"]');
 * await page.waitForTimeout(500);
 * expect(receiver.wasCalled()).toBe(false);
 * ```
 *
 * @param page - The Playwright page object
 * @param urlPattern - URL or pattern to intercept (e.g., "/bloom/api/registration/userInfo" or "**\/api/save")
 * @returns An object with getData() and wasCalled() methods
 */
export function preparePostReceiver<T>(
    page: Page,
    urlPattern: string,
): PostReceiver<T> {
    let capturedData: T | undefined;
    let wasRequestMade = false;
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
            wasRequestMade = true;

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

    // Return an object with both getData and wasCalled methods
    return {
        getData: async () => {
            // If already captured, return immediately
            if (capturedData !== undefined) {
                return capturedData;
            }
            // Otherwise wait for it
            return await waitPromise;
        },
        wasCalled: () => wasRequestMade,
    };
}

/**
 * Intercepts GET requests to a given URL pattern and returns a mock response.
 * This allows tests to provide fake data for API endpoints.
 *
 * @param page - The Playwright page object
 * @param urlPattern - URL or pattern to intercept (string glob or RegExp)
 * @param responseData - The data to return in the response
 * @param options.wrapBody - Set true to wrap the payload in a { data: ... } envelope
 */
export function prepareGetResponse<T>(
    page: Page,
    urlPattern: string | RegExp,
    responseData: T,
    options: { wrapBody?: boolean } = {},
): void {
    const shouldWrap = options.wrapBody === true;
    void page.route(urlPattern, async (route: Route) => {
        const bodyPayload = shouldWrap ? { data: responseData } : responseData;
        await route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(bodyPayload),
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
    prepareGetResponse(page, urlPattern, responseValue);
}
