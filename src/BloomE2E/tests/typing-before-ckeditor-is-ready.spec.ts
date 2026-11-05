// Text typed into a box while its CKEditor is still loading is still there once the editor is
// ready (BL-17022).
//
// bootstrap() in bloomEditing.ts calls CKEDITOR.inline() on each box as the page opens. The editor
// takes a copy of the box's content then, and only finishes loading later, after its config,
// language file and plugins have arrived. When it finishes, it writes its content into the box,
// and a person who types in that window, which a slow machine makes a second or two wide, would
// lose what they typed.
//
// The window is normally too short to type into reliably, so this test holds it open: it holds
// back CKEditor's config.js, which every editor on a page waits for, types while it is held, and
// then lets it through.

import type { Request, Route } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { apiPost } from "../helpers/api";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForBloomToHaveTyping,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { readBook } from "../helpers/bookHtml";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "typing-before-ckeditor", languages: ["en"] },
});

test("text typed while CKEditor is still loading survives its arrival (BL-17022)", async ({
    page,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Basic Text & Image");
    const [contentPage] = await getContentPages(page);
    const otherPage = (await getPages(page)).find(
        (p) => p.id !== contentPage.id,
    );
    if (!otherPage)
        throw new Error("test setup: the book should have more than one page");
    await goToPage(page, otherPage.id);

    // From here on, hold back the next request for CKEditor's config file.
    let heldRoute: Route | undefined;
    let sawConfigRequest: (request: Request) => void = () => {};
    const configRequested = new Promise<Request>((resolve) => {
        sawConfigRequest = resolve;
    });
    const configPattern = "**/lib/ckeditor/config.js*";
    await page.context().route(configPattern, (route) => {
        if (heldRoute) return route.continue();
        heldRoute = route;
        sawConfigRequest(route.request());
    });

    await apiPost(page, "editView/jumpToPage", contentPage.id, "text/plain");
    await configRequested;

    const box = editablePageFrame(page)
        .locator(`.bloom-page[id="${contentPage.id}"]`)
        .locator('.bloom-translationGroup .bloom-editable[lang="en"]')
        .first();
    await box.waitFor({ state: "visible", timeout: 30000 });
    await box.click();
    await expect(
        box,
        "clicking the box should give it the focus",
    ).toBeFocused();
    const typed = "Typed before the editor was ready.";
    await page.keyboard.insertText(typed);
    await expect(box).toHaveText(typed, { useInnerText: true });

    // Sanity check: the editor for this box exists and is not ready yet, so the typing really did
    // go in during the window this test is about.
    const editorStatus = () =>
        box.evaluate((element) => {
            const ckeditor = (window as unknown as Record<string, any>)
                .CKEDITOR;
            for (const key of Object.keys(ckeditor.instances)) {
                const editor = ckeditor.instances[key];
                if (editor.element.$ === element) return editor.status;
            }
            return "no editor";
        });
    expect(
        await editorStatus(),
        "test setup: the box's editor should still be loading when the text goes in",
    ).not.toBe("ready");
    expect(
        await editorStatus(),
        "test setup: the box should have an editor",
    ).not.toBe("no editor");

    await heldRoute!.continue();
    await page.context().unroute(configPattern);
    await expect
        .poll(editorStatus, {
            timeout: 30000,
            message: "The box's editor never became ready.",
        })
        .toBe("ready");

    await expect(
        box,
        "The text typed before the editor was ready should still be in the box.",
    ).toHaveText(typed, { useInnerText: true });

    // The caret stayed where the typing left it, so more typing carries on from there.
    const typedAfter = " Typed after.";
    await page.keyboard.insertText(typedAfter);
    await expect(
        box,
        "Typing after the editor was ready should carry on where the earlier typing ended.",
    ).toHaveText(typed + typedAfter, { useInnerText: true });

    // And it is what Bloom saves.
    await waitForEditTabSettled(page);
    await waitForBloomToHaveTyping(page, typed + typedAfter);
    await switchTab(page, "collection");
    const saved = (await readBook(page, bookFolder)).pages.find(
        (p) => p.id === contentPage.id,
    );
    expect(saved?.text).toContain(typed + typedAfter);
});
