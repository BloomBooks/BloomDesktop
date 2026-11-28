// Whether Bloom is the active application, as a person sees it: its window has the foreground (a
// coloured title bar, and the first click goes to a control rather than to activating the window)
// and its page has the keyboard (tooltips show, keys reach it).

import type { Page } from "@playwright/test";
import { expect } from "../fixtures/bloomTest";
import { getForegroundWindowProcess } from "./nativeDialogs";

export interface IBloomFocus {
    /**
     * Who owns the foreground window: "Bloom" when the Bloom process does, otherwise the other
     * process's name and id, or "no window".
     */
    foreground: string;
    /** document.hasFocus() of the page: it has the keyboard, inside the window that has it. */
    pageHasFocus: boolean;
}

/** What a Bloom that is the active application, with its page holding the keyboard, reads. */
export const kBloomActive: IBloomFocus = {
    foreground: "Bloom",
    pageHasFocus: true,
};

/** Read whether the Bloom with process id `bloomPid`, showing `page`, is the active application. */
export async function readBloomFocus(
    page: Page,
    bloomPid: number,
): Promise<IBloomFocus> {
    const owner = await getForegroundWindowProcess();
    let foreground: string;
    if (owner.pid === bloomPid) foreground = "Bloom";
    else if (owner.pid === 0) foreground = "no window";
    else foreground = `${owner.name} (process ${owner.pid})`;
    const pageHasFocus = await page.evaluate(() => document.hasFocus());
    return { foreground, pageHasFocus };
}

/**
 * Wait up to `timeoutMs` for the Bloom with process id `bloomPid` to be the active application,
 * with `page` holding the keyboard. Fails with `what` and the last reading when it never is.
 */
export async function expectBloomActive(
    page: Page,
    bloomPid: number,
    what: string,
    timeoutMs = 15000,
): Promise<void> {
    await expect
        .poll(() => readBloomFocus(page, bloomPid), {
            timeout: timeoutMs,
            message: what,
        })
        .toEqual(kBloomActive);
}
