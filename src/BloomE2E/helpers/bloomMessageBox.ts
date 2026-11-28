// Bloom's message box (BloomMessageBox.cs), which C# shows as a React dialog in a window of its own,
// instead of a Windows MessageBox. Its page is one of Bloom's browser's pages, so CDP reads it.

import { expect, type Page } from "@playwright/test";
import { closeReactDialogFromNode } from "./problemDialogs";

/** Every page of Bloom's browser but DevTools. */
function bloomPages(page: Page): Page[] {
    const browser = page.context().browser();
    const pages = browser
        ? browser.contexts().flatMap((c) => c.pages())
        : page.context().pages();
    return pages.filter((p) => !p.url().startsWith("devtools://"));
}

/** The text of every Bloom message box showing in any page of Bloom's browser. */
async function readBloomMessageBoxes(page: Page): Promise<string[]> {
    const texts: string[] = [];
    for (const candidate of bloomPages(page)) {
        try {
            const message = await candidate.evaluate(() => {
                // A message box's page (a temp file, ReactControl.cs) runs messageBoxBundle, which
                // its title names, whether Bloom serves the built bundle or loads it from a Vite
                // dev server. Its window is the dialog's frame, so the page is the dialog: its
                // message above its buttons.
                if (!document.title.includes("messageBoxBundle"))
                    return undefined;
                const buttons = new Set(
                    Array.from(document.body.querySelectorAll("button")).map(
                        (b) => b.innerText.trim(),
                    ),
                );
                return document.body.innerText
                    .split("\n")
                    .map((line) => line.trim())
                    .filter((line) => line && !buttons.has(line))
                    .join(" ");
            });
            if (message !== undefined) texts.push(message);
        } catch (error) {
            // A page that closed while we asked shows no box; any other failure is real.
            if (candidate.isClosed()) continue;
            throw error;
        }
    }
    return texts;
}

/**
 * Wait for Bloom to show exactly one message box, read its message, and close it the way its
 * button does. Returns the message, its lines joined by single spaces. `beforeClose`, if given,
 * runs once the box shows and before it is closed: for work that must be done while Bloom is
 * still waiting on the box, such as arming the dialog that Bloom shows as soon as it closes.
 */
export async function readAndCloseBloomMessageBox(
    page: Page,
    timeoutMs = 60000,
    beforeClose?: () => Promise<void>,
): Promise<string> {
    let shown: string[] = [];
    await expect
        .poll(
            async () => {
                shown = await readBloomMessageBoxes(page);
                return shown.length === 1 && shown[0] !== "" ? 1 : shown;
            },
            {
                timeout: timeoutMs,
                message: "Bloom should show exactly one message box",
            },
        )
        .toBe(1);
    if (beforeClose) await beforeClose();
    await closeReactDialogFromNode(page);
    await expect
        .poll(async () => (await readBloomMessageBoxes(page)).length, {
            timeout: 30000,
            message: `the message box that said "${shown[0]}" never closed`,
        })
        .toBe(0);
    return shown[0];
}
