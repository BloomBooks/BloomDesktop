// Which typing Ctrl+Z undoes at a time. Typing in a text box is undone in steps, and the steps
// end where the browser's own undo ends them, as measured in Edge (pageHistoryInput.ts, TypingRun,
// and docs/undo/TYPING-STEPS.md):
// - each line typed is a step, with the Enter or Shift+Enter that ends it; an Enter in the middle
//   of a line joins the typing around it;
// - a step has no limit on its length, but a pause of a second ends it (Bloom's rule; the
//   browser has none);
// - deleting right after typing starts a step, which typing after the deleting joins; a delete
//   that changes nothing does not count;
// - a step ends at a key that moves the caret, such as an arrow key, and at a click that moves
//   the caret, but not at a click that leaves it where it was;
// - an IME composition joins the step, and its end ends the step.
// The last two tests type with the Talking Book tool open, which puts each sentence into a marker
// span while the typing step is open; Ctrl+Z must still undo each step. After an undo, a click in
// the box makes the tool split the box again, which empties redo; the page history must say so.
//
// Every key is a real key press, so CKEditor's own handling of each key runs as it does for a
// person. Each test reads the text of every paragraph of the box after each Ctrl+Z and Ctrl+Y, and
// undoWithCtrlZAndExpect checks that each press ran exactly one undo.
//
// The tests are serial because they share the one book and page that the first one makes. Each
// empties the text box first.

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { checkUndoRoundTripAfterEachTest } from "../helpers/undoRoundTrip";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { deleteAllTextIn, pressKey, typeWithKeys } from "../helpers/keys";
import {
    openToolboxWithTalkingBook,
    setRecordingMode,
    waitForNarrationSentencesToCover,
} from "../helpers/talkingBook";
import {
    redoWithCtrlYAndExpect,
    undoWithCtrlZAndExpect,
    waitForPageHistory,
} from "../helpers/undo";

test.use({
    collectionSpec: { name: "undo-typing", languages: ["en"] },
});

checkUndoRoundTripAfterEachTest(test);

test.describe.configure({ mode: "serial" });

// The text box every test types into: the Just Text page's one translation group, in English.
const TEXT_BOX = ".bloom-translationGroup";

// The paragraphs of the box once a test has emptied it.
const EMPTY_BOX = [""];

// The text box of the page that the first test makes.
let box: Locator;

/**
 * The text of each paragraph of the box, without the zero width space that CKEditor leaves in an
 * empty paragraph or the zero width non-joiner that Bloom puts after a line break
 * (BloomField.InsertLineBreak).
 */
async function readParagraphs(): Promise<string[]> {
    return box
        .locator("p")
        .evaluateAll((paragraphs) =>
            paragraphs.map((p) => (p.textContent ?? "").replace(/[​‌]/g, "")),
        );
}

/** True when the page history has something to redo; the top bar enables Redo on this. */
async function canRedo(page: Page): Promise<boolean> {
    return editablePageFrame(page).evaluate(() =>
        (
            window as unknown as {
                editablePageBundle: { canRedo: () => boolean };
            }
        ).editablePageBundle.canRedo(),
    );
}

/**
 * The kind of every event in the record that the Show Page History window shows, oldest first,
 * such as "step", "undo" or "redoDropped".
 */
async function getPageHistoryEventKinds(page: Page): Promise<string[]> {
    return page.evaluate(() =>
        (
            window as unknown as {
                workspaceBundle: {
                    pageHistoryRecorder: {
                        getEvents: () => readonly { kind: string }[];
                    };
                };
            }
        ).workspaceBundle.pageHistoryRecorder
            .getEvents()
            .map((event) => event.kind),
    );
}

/** Empty the box with real keys, and check that it is down to one empty paragraph. */
async function emptyTheBox(page: Page): Promise<void> {
    box = await clickInGroup(page, TEXT_BOX, "en");
    await deleteAllTextIn(box, "the text box");
    await expect
        .poll(readParagraphs, {
            timeout: 15000,
            message:
                "test setup: emptying the text box should leave one empty paragraph",
        })
        .toEqual(EMPTY_BOX);
}

/**
 * Press Ctrl+Z once for each state in `statesBack`, the paragraphs that each press should leave,
 * then Ctrl+Y as many times, expecting the same states in the reverse order and finally `typed`,
 * the paragraphs from before the first Ctrl+Z. `typing` says what the test typed, for messages.
 */
async function expectUndoStepsAndRedo(
    page: Page,
    typing: string,
    typed: string[],
    statesBack: string[][],
): Promise<void> {
    await expect
        .poll(readParagraphs, {
            timeout: 15000,
            message: `test setup: ${typing} did not leave the paragraphs that the test expects`,
        })
        .toEqual(typed);
    for (const [i, state] of statesBack.entries()) {
        await undoWithCtrlZAndExpect(
            page,
            readParagraphs,
            state,
            `${typing} (Ctrl+Z number ${i + 1})`,
        );
    }
    const statesForward = [...statesBack.slice(0, -1).reverse(), typed];
    for (const [i, state] of statesForward.entries()) {
        await redoWithCtrlYAndExpect(
            page,
            readParagraphs,
            state,
            `${typing} (Ctrl+Y number ${i + 1})`,
        );
    }
}

test.describe("typing that Ctrl+Z undoes at a time", () => {
    test("three lines, each its own paragraph: each line with its Enter is a step", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await waitForPageHistory(page);
        await emptyTheBox(page);

        await typeWithKeys(page, "one");
        await pressKey(page, "Enter");
        await typeWithKeys(page, "two");
        await pressKey(page, "Enter");
        await typeWithKeys(page, "three");

        await expectUndoStepsAndRedo(
            page,
            "typing three lines",
            ["one", "two", "three"],
            [["one", "two", ""], ["one", ""], EMPTY_BOX],
        );
    });

    test("an Enter in the middle of a line joins the typing after it", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "abcd");
        await pressKey(page, "ArrowLeft");
        await pressKey(page, "ArrowLeft");
        await pressKey(page, "Enter");
        await typeWithKeys(page, "XY");

        await expectUndoStepsAndRedo(
            page,
            "splitting a line with Enter and typing",
            ["ab", "XYcd"],
            [["abcd"], EMPTY_BOX],
        );
    });

    test("a pause of over a second between key presses ends the step", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "ab");
        // The pause is what this test is about: kPauseThatEndsTypingStepMs is 1000 ms.
        await page.waitForTimeout(1500);
        await typeWithKeys(page, "cd");

        await expectUndoStepsAndRedo(
            page,
            "typing, pausing and typing again",
            ["abcd"],
            [["ab"], EMPTY_BOX],
        );
    });

    test("60 characters are one step", async ({ page }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "abcdefghij".repeat(6));

        await expectUndoStepsAndRedo(
            page,
            "typing 60 characters",
            ["abcdefghij".repeat(6)],
            [EMPTY_BOX],
        );
    });

    test("typing, Backspace twice, typing, Backspace: deleting after typing starts a step, which the typing after it joins", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "abc");
        await pressKey(page, "Backspace");
        await pressKey(page, "Backspace");
        await typeWithKeys(page, "xy");
        await pressKey(page, "Backspace");

        await expectUndoStepsAndRedo(
            page,
            "typing, deleting, typing and deleting again",
            ["ax"],
            [["axy"], ["abc"], EMPTY_BOX],
        );
    });

    test("a Delete at the end of the text, which changes nothing, leaves the typing one step", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "abc");
        await pressKey(page, "Delete");
        await typeWithKeys(page, "xy");

        await expectUndoStepsAndRedo(
            page,
            "typing on both sides of a Delete that changed nothing",
            ["abcxy"],
            [EMPTY_BOX],
        );
    });

    test("a click in the box that leaves the caret where it was keeps the step; one that moves it ends the step", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "abcd");
        // To the right of the text on its line: the caret stays at the end of the line.
        const line = box.locator("p").last();
        const lineBox = await line.boundingBox();
        expect(
            lineBox,
            "test setup: the line should be showing",
        ).not.toBeNull();
        await line.click({
            position: { x: lineBox!.width - 2, y: lineBox!.height / 2 },
        });
        await typeWithKeys(page, "e");
        // At the start of the line: the caret moves.
        await line.click({ position: { x: 1, y: lineBox!.height / 2 } });
        await typeWithKeys(page, "Z");

        await expectUndoStepsAndRedo(
            page,
            "typing, clicking where the caret was, typing, clicking elsewhere and typing",
            ["Zabcde"],
            [["abcde"], EMPTY_BOX],
        );
    });

    test("typing, then an arrow key, then typing: two steps", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "ab");
        await pressKey(page, "ArrowLeft");
        await typeWithKeys(page, "c");

        await expectUndoStepsAndRedo(
            page,
            "typing on both sides of an arrow key",
            ["acb"],
            [["ab"], EMPTY_BOX],
        );
    });

    test("a line break (Shift+Enter) at the end of a line ends the step with it", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "one");
        await pressKey(page, "Shift+Enter");
        await typeWithKeys(page, "two");
        await expect(
            box.locator("span.bloom-linebreak"),
            "test setup: Shift+Enter should have put a line break in the box",
        ).not.toHaveCount(0);

        await expectUndoStepsAndRedo(
            page,
            "typing two lines with a line break between them",
            ["onetwo"],
            [["one"], EMPTY_BOX],
        );
    });

    test("an IME composition joins the typing before it, and its end ends the step", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "ab");
        // What an input method sends while the user composes, and when they choose the text. It
        // sends no key presses here, so the second composition starts a step of its own.
        const cdp = await page.context().newCDPSession(page);
        for (const [keys, chosen] of [
            ["ka", "か"],
            ["sa", "さ"],
        ]) {
            for (let i = 1; i <= keys.length; i++)
                await cdp.send("Input.imeSetComposition", {
                    text: keys.slice(0, i),
                    selectionStart: i,
                    selectionEnd: i,
                });
            await cdp.send("Input.insertText", { text: chosen });
        }
        await typeWithKeys(page, "c");

        await expectUndoStepsAndRedo(
            page,
            "typing, composing two characters with an input method, and typing",
            ["abかさc"],
            [["abかさ"], ["abか"], EMPTY_BOX],
        );
    });

    test("with the Talking Book tool open, which marks each sentence as the user types, each line with its Enter is still a step", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // The tool marks nothing in an empty page, so give it a sentence before opening it.
        await emptyTheBox(page);
        await typeWithKeys(page, "Hi.");
        await openToolboxWithTalkingBook(page);
        await setRecordingMode(page, "By Sentence");
        await emptyTheBox(page);

        await typeWithKeys(page, "One.");
        await pressKey(page, "Enter");
        await typeWithKeys(page, "Two.");
        // Let the tool mark the sentences while the typing step is open, as it does for a person
        // who stops typing for a moment before pressing Ctrl+Z.
        await waitForNarrationSentencesToCover(page, "One.Two.");

        await expectUndoStepsAndRedo(
            page,
            "typing two lines with the Talking Book tool open",
            ["One.", "Two."],
            [["One.", ""], EMPTY_BOX],
        );
    });

    test("with the Talking Book tool open, a click in the box after Ctrl+Z empties redo, and the page history says so", async ({
        page,
    }) => {
        // The tool is open from the test before.
        await emptyTheBox(page);
        await typeWithKeys(page, "One.");
        await waitForNarrationSentencesToCover(page, "One.");
        await undoWithCtrlZAndExpect(
            page,
            readParagraphs,
            EMPTY_BOX,
            "typing with the Talking Book tool open",
        );
        expect(
            await canRedo(page),
            "test setup: after Ctrl+Z there should be something to redo",
        ).toBe(true);
        const eventsBefore = await getPageHistoryEventKinds(page);

        // At a click after an undo, the tool splits the box into sentences again. That change
        // joins the latest step, which empties redo; the page history records it.
        await box.click();
        await expect
            .poll(() => canRedo(page), {
                timeout: 15000,
                message:
                    "After the click, Talking Book's split of the box should have emptied redo.",
            })
            .toBe(false);
        const newEvents = (await getPageHistoryEventKinds(page)).slice(
            eventsBefore.length,
        );
        expect(
            newEvents,
            "The page history has no line saying that a change emptied redo.",
        ).toContain("redoDropped");
    });
});
