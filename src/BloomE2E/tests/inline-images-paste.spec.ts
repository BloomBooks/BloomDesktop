// Copying text that contains a picture, and pasting it.
//
// WHAT THIS IS ABOUT. Bloom's own paste handler bails out whenever the focus is in a
// bloom-editable (bloomEditing.ts), so a paste inside a text block is CKEditor's. CKEditor's
// pasteFilter (lib/ckeditor/config.js) applies only to content from OUTSIDE the editor; an
// internal paste falls back to allowedContent = true. So the wrapper markup pastes through
// unfiltered, and what arrives is a second element carrying the FIRST one's
// data-bloom-inline-image-id.
//
// Identity is what the feature is built on. syncInlineImagesFromEditable matches the copies of a
// picture across the group's editables by that id, and getInlineImageById returns the first match,
// so a second element with the same id is a picture that can never be kept in step with its own
// copies in the other languages: it diverges from the moment it exists, and nothing in the editor
// will ever bring it back.
//
// Two pastes are worth asking about and they have different answers available: into the same
// block, and into a different block on the same page. Either "no second picture appears" or "the
// second picture gets its own id" would be a sound outcome; two elements sharing one id is not.

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

/** Every inline image in the group, flattened, with the block it is in. */
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

    // THE ACTION UNDER TEST: a paste of the block's own markup, wrapper included, delivered as
    // the paste event CKEditor listens for. Real Ctrl+C / Ctrl+V key presses do not do it here:
    // the keys arrive, but nothing lands, because the OS clipboard is not filled by CDP key
    // events. What matters for this question is not the clipboard but the paste PIPELINE --
    // Bloom's handler, which stands aside for a bloom-editable, and then CKEditor's pasteFilter,
    // which treats internal content as unfiltered -- and that is exactly what this reaches.
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
        // At the end of the text, which is where a person pastes when they mean "again".
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
    // The paste is asynchronous in CKEditor, and so is anything the page does about it, so wait
    // for the block's text to have grown: that is the paste having landed, and it is what the
    // next assertion is about anyway.
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
    // One copy of the picture per editable of the group is the design: that is how a float can
    // wrap the text of each language's block. So the count to watch is per editable.
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
    // The menu no longer offers "Add Image" in a data-book field, because that field is stored in
    // the data div as markup and written back into every element with the same key -- which made
    // the wrapper's markup the book's title. Hiding the command closes one door. A paste is the
    // other, and it does not go through the menu at all, so it has to be asked separately.
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
    // The cover credits rather than the title: it is a data-book field on the same xmatter page,
    // so it asks the same question, and pasting into it does not change the book's name, which
    // the collection and the book's folder are both keyed on.
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
    // As above: wait for the paste to have landed rather than for a length of time. The credits
    // field's markup changing is that, whatever the paste turned into.
    await expect
        .poll(async () => await title.evaluate((e) => e.innerHTML), {
            timeout: 30000,
            message:
                "The paste never changed the credits field, so nothing was pasted and this " +
                "test is measuring nothing.",
        })
        .not.toBe(creditsBefore);

    // Leaving the page is what makes Bloom save it, and the collection learns the title then.
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
    // And nowhere else in the saved book either: the data div is written back into every element
    // carrying the same key, so one wrapper stored there becomes several on the pages.
    expect(
        (html.match(/data-bloom-inline-image-id/g) ?? []).length,
        "Pasting into front matter left inline image wrappers on the xmatter pages.",
    ).toBe(2);
});
