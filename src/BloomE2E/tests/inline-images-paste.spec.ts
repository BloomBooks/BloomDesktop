// Tests copying text that contains a picture, and pasting it.
//
// Bloom's own paste handler (bloomEditing.ts) does nothing when the focus is in a bloom-editable,
// so CKEditor handles a paste inside a text block. CKEditor's pasteFilter (lib/ckeditor/config.js)
// applies only to content copied from outside the editor; a paste of content from inside it uses
// allowedContent = true. So the wrapper's markup is pasted unfiltered, and the result is a second
// element with the same data-bloom-inline-image-id as the first.
//
// The feature depends on each picture having its own id. syncInlineImagesFromEditable matches a
// picture's copies across the group's editables by that id, and getInlineImageById returns the
// first match. A second element with the same id can therefore never be kept in step with its
// copies in the other languages, and nothing in the editor will repair it.
//
// It would be fine for the paste to produce no second picture, or a second picture with its own
// id. It is not fine for two elements to share one id.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import * as fs from "node:fs";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addInlineImage,
    changeInlineImagePicture,
    getInlineImages,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: { name: "inline-images-paste", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

/** Returns every inline image in the group, from all of its blocks, in one list. */
const allImages = async (page: import("@playwright/test").Page) =>
    (await getInlineImages(page, BLOCK)).flatMap((block) => block.images);

test("pasting text that contains a picture does not produce two pictures with one identity [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Paste");
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);

    const imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );
    const before = await allImages(page);
    expect(
        before.length,
        "The setup put no picture in the block, so there is nothing for the paste to copy.",
    ).toBeGreaterThan(0);

    // THE ACTION UNDER TEST: paste the block's own markup, including the wrapper, by dispatching
    // the paste event CKEditor listens for. Pressing Ctrl+C and Ctrl+V does not work here: the
    // key presses arrive, but CDP key events do not fill the OS clipboard, so nothing is pasted.
    // This test is about the code a paste goes through: Bloom's handler, which ignores a paste in
    // a bloom-editable, and then CKEditor's pasteFilter, which lets content from inside the editor
    // through unfiltered. Dispatching the event runs both of them.
    const block = page
        .frameLocator("#page")
        .locator(`${BLOCK} > .bloom-editable[lang="${LANG}"]`)
        .first();
    await block.click();
    const textBefore = ((await block.textContent()) ?? "").length;
    const pastedHtml = await block.evaluate((editable) => editable.innerHTML);
    expect(
        pastedHtml,
        "The markup about to be pasted holds no picture, so this test is measuring nothing.",
    ).toContain("bloom-inlineImage");
    await block.evaluate((editable, html) => {
        const transfer = new DataTransfer();
        transfer.setData("text/html", html);
        transfer.setData("text/plain", editable.textContent ?? "");
        // Paste at the end of the text, where a person pastes to repeat what they copied.
        const selection = editable.ownerDocument.getSelection()!;
        const range = editable.ownerDocument.createRange();
        range.selectNodeContents(editable);
        range.collapse(false);
        selection.removeAllRanges();
        selection.addRange(range);
        editable.dispatchEvent(
            new ClipboardEvent("paste", {
                bubbles: true,
                cancelable: true,
                clipboardData: transfer,
            }),
        );
    }, pastedHtml);
    // CKEditor handles the paste asynchronously, and so does anything the page does in response,
    // so wait until the block's text is longer. That shows the paste has happened.
    await expect
        .poll(async () => ((await block.textContent()) ?? "").length, {
            timeout: 30000,
            message:
                `The paste never changed the block's text, so nothing was pasted and this test ` +
                `is measuring nothing. Ctrl+A inside a contenteditable may not have selected ` +
                `what was expected.`,
        })
        .toBeGreaterThan(textBefore);

    const textAfter = ((await block.textContent()) ?? "").length;
    expect(
        textAfter,
        `The paste did not change the block's text (still ${textAfter} characters), so nothing ` +
            `was pasted and this test is measuring nothing. Ctrl+A inside a contenteditable may ` +
            `not have selected what was expected.`,
    ).toBeGreaterThan(textBefore);

    const after = await allImages(page);
    const byId = new Map<string, number>();
    for (const image of after)
        byId.set(image.id, (byId.get(image.id) ?? 0) + 1);
    // Each editable in the group is supposed to have one copy of the picture, because a float can
    // only wrap the text of the block it is in. So the test counts copies within each editable.
    const duplicatedWithinAnEditable = (
        await getInlineImages(page, BLOCK)
    ).flatMap((editable) => {
        const seen = new Map<string, number>();
        for (const image of editable.images)
            seen.set(image.id, (seen.get(image.id) ?? 0) + 1);
        return [...seen.entries()]
            .filter(([, count]) => count > 1)
            .map(([id, count]) => `${editable.languageTag}: ${count} of ${id}`);
    });

    expect(
        duplicatedWithinAnEditable,
        `Pasting text that contained the picture put more than one element with the same ` +
            `data-bloom-inline-image-id into one block: ${duplicatedWithinAnEditable.join("; ")}. ` +
            `That id is how the copies of a picture are matched across the group's languages, and ` +
            `getInlineImageById returns the first match, so the second element can never be kept ` +
            `in step with anything and nothing in the editor will repair it.`,
    ).toEqual([]);
});

test("pasting a picture into a front-matter field does not put markup in the data div [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    // The menu does not offer "Insert Image" in a data-book field. Such a field is stored as
    // markup in the data div and written back into every element with the same key, so a picture
    // in the title field would put the wrapper's markup into the book's title. A paste can still
    // put a picture there without using the menu, so this test checks the paste.
    const block = page
        .frameLocator("#page")
        .locator(`${BLOCK} > .bloom-editable[lang="${LANG}"]`)
        .first();
    const markupWithAPicture = await block.evaluate(
        (editable) => editable.innerHTML,
    );
    expect(
        markupWithAPicture,
        "The markup to paste holds no picture, so this test is measuring nothing.",
    ).toContain("bloom-inlineImage");

    const coverId = (await getPages(page)).find((p) => !p.isContentPage)!.id;
    await goToPage(page, coverId);
    // The test pastes into the cover credits instead of the title. The credits are a data-book
    // field on the same xmatter page, so they are stored the same way, and pasting into them
    // does not change the book's name, which the collection and the book's folder depend on.
    const title = page
        .frameLocator("#page")
        .locator(
            `.creditsRow .bloom-translationGroup > .bloom-editable[lang="${LANG}"]`,
        )
        .first();
    await title.click();
    const creditsBefore = await title.evaluate((e) => e.innerHTML);
    await title.evaluate((editable, html) => {
        const transfer = new DataTransfer();
        transfer.setData("text/html", html);
        transfer.setData("text/plain", "pasted");
        const selection = editable.ownerDocument.getSelection()!;
        const range = editable.ownerDocument.createRange();
        range.selectNodeContents(editable);
        range.collapse(false);
        selection.removeAllRanges();
        selection.addRange(range);
        editable.dispatchEvent(
            new ClipboardEvent("paste", {
                bubbles: true,
                cancelable: true,
                clipboardData: transfer,
            }),
        );
    }, markupWithAPicture);
    // As in the first test, wait for the paste to happen instead of waiting a fixed time. Any
    // change to the credits field's markup shows that the paste happened, whatever it produced.
    await expect
        .poll(async () => await title.evaluate((e) => e.innerHTML), {
            timeout: 30000,
            message:
                "The paste never changed the credits field, so nothing was pasted and this " +
                "test is measuring nothing.",
        })
        .not.toBe(creditsBefore);

    // Leaving the page makes Bloom save it.
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    const html = fs.readFileSync(
        bookHtmlPath(await findBookFolder(page, "Inline Images Paste")),
        "utf8",
    );
    const storedCredits = (html.match(
        /<div[^>]*data-book="smallCoverCredits"[^>]*lang="en"[^>]*>([\s\S]*?)<\/div>/,
    ) ?? [, ""])[1].trim();
    expect(
        storedCredits,
        `Pasting a picture into a front-matter field put its markup into the data div, which is ` +
            `where that field is stored and from which it is written back into every element ` +
            `with the same data-book key. It reads: ${storedCredits.slice(0, 300)}`,
    ).not.toContain("bloom-inlineImage");
    // The wrapper must not be anywhere else in the saved book either. The data div is written
    // back into every element with the same key, so one wrapper stored there would become
    // several on the pages. The two ids expected are the original picture's copies in the
    // content page's English and prototype blocks.
    expect(
        (html.match(/data-bloom-inline-image-id/g) ?? []).length,
        "Pasting into front matter left inline image wrappers on the xmatter pages.",
    ).toBe(2);
});
