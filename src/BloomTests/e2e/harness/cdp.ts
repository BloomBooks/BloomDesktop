// Cloud TC's "share on cloud" flow spans THREE separate WinForms-hosted WebView2 controls in
// the same Bloom.exe process (Collection tab -> Settings dialog's Team Collection tab -> Create
// Team Collection dialog), each a distinct CDP target/page under the SAME debug port. This
// helper polls for a new page matching a URL substring, since Playwright's `browser.on("page")`
// can race the WinForms dialog's WebView2 control finishing navigation.
import { Browser, Page } from "@playwright/test";

/** Waits for (and returns) a page whose URL contains `urlSubstring`, polling the browser's
 * existing contexts/pages. Use when a WinForms action (e.g. clicking a button that opens a new
 * dialog) is expected to bring up a brand-new WebView2 host. */
export const waitForPage = async (
    browser: Browser,
    urlSubstring: string,
    timeoutMs = 15_000,
): Promise<Page> => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        const pages = browser.contexts().flatMap((context) => context.pages());
        const match = pages.find((candidate) =>
            candidate.url().includes(urlSubstring),
        );
        if (match) {
            await match.waitForLoadState("domcontentloaded");
            return match;
        }
        await new Promise((resolve) => setTimeout(resolve, 300));
    }
    const seenUrls = browser
        .contexts()
        .flatMap((context) => context.pages())
        .map((p) => p.url());
    throw new Error(
        `Timed out waiting for a page containing '${urlSubstring}'. Pages currently open: ${JSON.stringify(seenUrls)}`,
    );
};
