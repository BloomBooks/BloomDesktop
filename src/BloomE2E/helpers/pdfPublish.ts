// Make a PDF of the selected book through the Publish tab's PDF & Print screen, the way a person
// does: click one of its three mode buttons and wait for the preview to show the PDF.

import { expect, type Page } from "@playwright/test";
import { selectPublishDestination } from "./publish";

/** The PDF & Print screen's mode buttons, by the label each shows. */
export type PdfMode = "Simple" | "Booklet cover" | "Booklet Insides";

/**
 * Open PDF & Print, click the button for `mode`, wait until the preview shows the PDF Bloom made,
 * and return that PDF's path on disk. A book must already be selected.
 */
export async function makePdfInPublishTab(
    page: Page,
    mode: PdfMode,
    timeoutMs = 300000,
): Promise<string> {
    await selectPublishDestination(page, "PDF & Print");
    // The preview is the iframe showing a PDF; the shell has other iframes.
    const preview = page.locator('iframe[src*=".pdf"]');
    const before = (await preview.count())
        ? await preview.first().getAttribute("src")
        : undefined;
    const button = page.getByRole("button", { name: mode, exact: false });
    await button.first().waitFor({ state: "visible", timeout: 30000 });
    await button.first().click();
    let src: string | null | undefined;
    await expect
        .poll(
            async () => {
                src = (await preview.count())
                    ? await preview.first().getAttribute("src")
                    : undefined;
                return !!src && src !== before;
            },
            {
                timeout: timeoutMs,
                message: `The PDF & Print preview never showed a new PDF after clicking "${mode}".`,
            },
        )
        .toBe(true);
    return pdfPathFromPreviewUrl(src!);
}

/**
 * The file a preview URL names. Bloom serves a file by its full path under /bloom/, e.g.
 * http://localhost:8089/bloom/C%3A/Users/me/AppData/Local/Temp/Book-0.pdf.
 */
export function pdfPathFromPreviewUrl(src: string): string {
    const path = decodeURIComponent(new URL(src).pathname);
    const marker = "/bloom/";
    const start = path.indexOf(marker);
    if (start < 0)
        throw new Error(
            `The preview URL ${src} does not name a file Bloom serves.`,
        );
    return path.substring(start + marker.length);
}
