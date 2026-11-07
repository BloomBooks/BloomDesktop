// Typing with the Leveled Reader tool open leaves the caret where the person put it, even after they
// have clicked away from the text and back into it (BL-16916).
//
// While a reader tool is open, every pause in typing runs a markup pass (handlePageEditing in
// pageEditingMarkup.ts), which records the caret before it works on the text and puts it back
// afterwards. The bug: after a click on the dark area around the page, followed by a click in the
// middle of the text, that pass put the caret back where it had been BEFORE the click away (the
// end of the text, if that is where the person had been typing). So the character typed after
// the click landed in the right place, but half a second later the caret jumped, and everything
// typed after that went somewhere else.
//
// What went wrong: when the text box lost the focus, the reader tool's focusout handler marked up
// the text inside a CKEditor bookmark save-and-restore, and the restore put the focus straight back
// into the box, where CKEditor's own focus-out handler then locked its selection. The box never
// lost the focus again, so nothing unlocked it, and every later markup pass recorded the caret
// where the lock said it was. That handler now keeps the bookmarks only when the focus leaves Bloom's
// window. Clicking in the toolbox never had the bug (the restore did not pull the focus back across
// frames), and now skips the bookmarks too, so its test is here to keep it working.
//
// Each test types, then checks both where the caret is once the markup pass has run and where the
// next character lands. Knowing the pass has run: the Leveled Reader's "words on this page" count
// is updated by that same pass, so the text typed after the click adds a word, and the count
// changing says the pass is over.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForEditablePage,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    expandReaderTool,
    getReaderPhase,
    waitForReaderToolToRestore,
} from "../helpers/readerTools";
import { enableToolForBook, toolboxFrame } from "../helpers/toolbox";
import type { Locator, Page } from "@playwright/test";
import * as fs from "node:fs";

test.use({
    collectionSpec: { name: "reader-tool-keeps-caret", languages: ["en"] },
});

const GROUP = ".bloom-translationGroup";
const LANG = "en";

/** Wait until the Leveled Reader tool reports this many words on the page being edited. */
async function waitForWordsOnPage(page: Page, count: number, why: string) {
    await expect(
        toolboxFrame(page).getByTestId("leveled-reader-words-this-page"),
        `The Leveled Reader never counted ${count} words on the page ${why}.`,
    ).toHaveText(String(count), { timeout: 15000 });
}

/**
 * Wait until Bloom has written the Leveled Reader's level onto the book, and has finished loading
 * the page again afterwards.
 *
 * In a new book, the tool saves the level it restores, and the book's body does not have that
 * level yet, so Bloom saves the book with it and reloads the page (EditingModel.
 * EnsureLevelAttrCorrect). That happens about half a second after the tool shows the level, and
 * typing or clicking in the text while the page reloads is lost. Bloom writes the book before it
 * reloads the page, so once the saved book has the level, the reload has been asked for, and
 * waiting for the Edit tab to settle waits it out.
 */
async function waitForLevelToBeRecordedInTheBook(
    page: Page,
    bookFolder: string,
) {
    const level = await getReaderPhase(page, "leveledReader");
    await expect
        .poll(
            () =>
                /<body[^>]*\sdata-leveledreaderlevel="(\d+)"/.exec(
                    fs.readFileSync(bookHtmlPath(bookFolder), "utf8"),
                )?.[1],
            {
                timeout: 15000,
                message: `Bloom never saved the book with the Leveled Reader's level (${level}) on its body.`,
            },
        )
        .toBe(String(level));
    await waitForEditTabSettled(page);
    await waitForEditablePage(page);
}

/** How many characters of the box's text come before the caret; -1 if the caret is not in it. */
async function getCaretOffset(box: Locator): Promise<number> {
    return box.evaluate((element) => {
        const selection = element.ownerDocument.getSelection();
        if (
            !selection ||
            selection.rangeCount === 0 ||
            !element.contains(selection.focusNode)
        )
            return -1;
        const range = element.ownerDocument.createRange();
        range.selectNodeContents(element);
        range.setEnd(selection.focusNode!, selection.focusOffset);
        return range.toString().length;
    });
}

/**
 * Click in the box just after the `offset`th character of its text, the way a person clicks to put
 * the caret there.
 */
async function clickAfterCharacter(box: Locator, offset: number) {
    const position = await box.evaluate((element, characterOffset) => {
        const walker = element.ownerDocument.createTreeWalker(
            element,
            NodeFilter.SHOW_TEXT,
        );
        let remaining = characterOffset;
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const length = (node as Text).length;
            if (remaining <= length) {
                const range = element.ownerDocument.createRange();
                range.setStart(node, remaining - 1);
                range.setEnd(node, remaining);
                const character = range.getBoundingClientRect();
                const box = element.getBoundingClientRect();
                return {
                    // Just inside the right-hand edge of the character, so the caret goes after it.
                    x: character.right - 1 - box.left,
                    y: character.top + character.height / 2 - box.top,
                };
            }
            remaining -= length;
        }
        throw new Error(
            `The box has fewer than ${characterOffset} characters.`,
        );
    }, offset);
    await box.click({ position });
}

/**
 * Click the dark area to the right of the page, inside the page's frame, level with the text box:
 * somewhere that is not text, and not part of the page. (There is little room on the left, and at
 * the nightly's window size the page fills most of the frame and is scrolled, so the point is
 * worked out from what the frame is actually showing.)
 */
async function clickBesideThePage(page: Page, box: Locator) {
    const frameRect = await page.locator("iframe#page").boundingBox();
    if (!frameRect) throw new Error("The page frame has no bounding box.");
    const spot = await box.evaluate((element) => {
        const document = element.ownerDocument;
        const pageRect = element
            .closest(".bloom-page")!
            .getBoundingClientRect();
        const boxRect = element.getBoundingClientRect();
        // clientWidth leaves out the frame's scroll bar.
        const visibleRight = document.documentElement.clientWidth;
        const x = (pageRect.right + visibleRight) / 2;
        // Level with the part of the box that is scrolled into view.
        const y =
            (Math.max(boxRect.top, 0) +
                Math.min(
                    boxRect.bottom,
                    document.documentElement.clientHeight,
                )) /
            2;
        const hit = document.elementFromPoint(x, y);
        let lands: string;
        if (!hit) lands = "nothing";
        else if (hit.closest(".bloom-page")) lands = "the page";
        else lands = "beside the page";
        return { x, y, lands, room: visibleRight - pageRect.right };
    });
    expect(
        spot.lands,
        `test setup: where the click beside the page lands (with ${spot.room}px beside the page)`,
    ).toBe("beside the page");
    await page.mouse.click(frameRect.x + spot.x, frameRect.y + spot.y);
}

/**
 * Click the Leveled Reader's "words on this page" count, which does nothing: a click in the
 * toolbox that takes the focus out of the page altogether.
 */
async function clickInTheToolbox(page: Page) {
    await toolboxFrame(page)
        .getByTestId("leveled-reader-words-this-page")
        .click();
}

/**
 * Type in a text box with the Leveled Reader tool open, click away with `clickAway`, click back into
 * the middle of the text, type, and check that the caret stays where the click put it.
 */
async function typeClickAwayAndBack(
    page: Page,
    clickAway: (page: Page, box: Locator) => Promise<void>,
) {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Basic Text & Image");
    const [textPage] = await getContentPages(page);
    await enableToolForBook(page, bookFolder, "leveledReader");
    await goToPage(page, textPage.id);
    await expandReaderTool(page, "leveledReader");
    await waitForReaderToolToRestore(page, "leveledReader", bookFolder);
    await waitForLevelToBeRecordedInTheBook(page, bookFolder);

    const box = await clickInGroup(page, GROUP, LANG);
    // Real key presses, not one insertion: the markup pass is started by keyup.
    await page.keyboard.type("one two three four five");
    await expect(box).toHaveText("one two three four five");
    await waitForWordsOnPage(page, 5, "after the first typing");
    expect(
        await getCaretOffset(box),
        "test setup: the caret should be at the end of the typing",
    ).toBe(23);

    await clickAway(page, box);
    await expect(
        box,
        "Clicking away from the text should take the focus out of it.",
    ).not.toBeFocused();

    // Put the caret just after "two".
    await clickAfterCharacter(box, 7);
    await expect(box).toBeFocused();
    expect(
        await getCaretOffset(box),
        "test setup: the click should put the caret just after 'two'",
    ).toBe(7);

    await page.keyboard.type(" six");
    await expect(box).toHaveText("one two six three four five");
    await waitForWordsOnPage(page, 6, "after typing ' six' in the middle");

    expect(
        await getCaretOffset(box),
        "Once the Leveled Reader had marked up the text, the caret should still be just after " +
            "the ' six' that was typed, not back where it was before the click away.",
    ).toBe(11);
    await page.keyboard.type("Z");
    await expect(
        box,
        "The next character typed should go where the caret was put.",
    ).toHaveText("one two sixZ three four five");
}

test("clicking beside the page and back into the text does not make the caret jump (BL-16916)", async ({
    page,
}) => {
    await typeClickAwayAndBack(page, clickBesideThePage);
});

test("clicking in the toolbox and back into the text does not make the caret jump (BL-16916)", async ({
    page,
}) => {
    await typeClickAwayAndBack(page, clickInTheToolbox);
});
