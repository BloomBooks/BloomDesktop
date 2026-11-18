// Hold a page's text boxes in the moment before their editors are ready.
//
// A page appears before CKEditor has finished starting on its text boxes: each editor waits for its
// config file, language file and plugins. On a slow machine that moment lasts a second or two, and
// a person can click and type in it. It is normally too short for a test to act in reliably, so
// this holds it open by holding back CKEditor's config.js, which every editor on a page waits for.
// (typing-before-ckeditor-is-ready.spec.ts was the first test to do this.)

import type { Page, Route } from "@playwright/test";

const kConfigPattern = "**/lib/ckeditor/config.js*";

/** What holdCkEditorStartup() returns. */
export interface ICkEditorHold {
    /** Resolves once the next page to load has asked for CKEditor's config file and is waiting. */
    requested: Promise<void>;
    /** Let the editors finish starting. Call it exactly once, after `requested` has resolved. */
    release: () => Promise<void>;
}

/**
 * From now on, hold back the next request for CKEditor's config file, so the text boxes of the
 * next page to load stay without a ready editor until release() is called. Call this before the
 * action that changes pages.
 */
export async function holdCkEditorStartup(page: Page): Promise<ICkEditorHold> {
    let heldRoute: Route | undefined;
    let sawRequest: () => void = () => {};
    const requested = new Promise<void>((resolve) => {
        sawRequest = resolve;
    });
    await page.context().route(kConfigPattern, (route) => {
        if (heldRoute) return route.continue();
        heldRoute = route;
        sawRequest();
    });
    return {
        requested,
        release: async () => {
            if (!heldRoute)
                throw new Error(
                    "No page has asked for CKEditor's config file, so there is nothing to release.",
                );
            await heldRoute.continue();
            await page.context().unroute(kConfigPattern);
        },
    };
}

/**
 * The state of the editor on the text box that has the focus in the page being edited: CKEditor's
 * own status ("unloaded", "loaded", "ready"), or "no editor". A test uses it to check that what it
 * did really happened before the editor was ready.
 */
export async function getStatusOfFocusedEditor(page: Page): Promise<string> {
    const frame = page.frame({ name: "page" });
    if (!frame) return "no page frame";
    return frame.evaluate(() => {
        const box = document.activeElement?.closest(".bloom-editable");
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const ckeditor = (window as any).CKEDITOR;
        if (!box || !ckeditor) return "no editor";
        for (const key of Object.keys(ckeditor.instances)) {
            const editor = ckeditor.instances[key];
            if (editor.element.$ === box) return editor.status as string;
        }
        return "no editor";
    });
}
