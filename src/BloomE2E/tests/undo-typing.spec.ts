// Which typing Ctrl+Z undoes at a time. Typing in a text box is undone in steps, and the steps
// end where they ended in Bloom before the page history, when CKEditor 4.5.1's undo plugin made
// them (pageHistoryInput.ts, TypingRun):
// - Enter, a new paragraph, is a step of its own, so the typing before it and after it are
//   separate steps too;
// - a step ends at the 25th character typed;
// - a step ends when the user goes from typing to deleting, or back;
// - a step ends at a key that moves the caret, such as an arrow key;
// - Shift+Enter, a line break, is typing like any other key.
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
    test("three lines, each its own paragraph: the typing of each line and each Enter are separate steps", async ({
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
            [
                ["one", "two", ""],
                ["one", "two"],
                ["one", ""],
                ["one"],
                EMPTY_BOX,
            ],
        );
    });

    test("30 characters: the first 25 are one step and the last 5 another", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "abcdefghijklmnopqrstuvwxyz1234");

        await expectUndoStepsAndRedo(
            page,
            "typing 30 characters",
            ["abcdefghijklmnopqrstuvwxyz1234"],
            [["abcdefghijklmnopqrstuvwxy"], EMPTY_BOX],
        );
    });

    test("typing, then Backspace twice, then typing: three steps", async ({
        page,
    }) => {
        await emptyTheBox(page);
        await typeWithKeys(page, "abc");
        await pressKey(page, "Backspace");
        await pressKey(page, "Backspace");
        await typeWithKeys(page, "xy");

        await expectUndoStepsAndRedo(
            page,
            "typing, deleting and typing again",
            ["axy"],
            [["a"], ["abc"], EMPTY_BOX],
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

    test("a line break (Shift+Enter) is typing, in the same step as the typing around it", async ({
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
            [EMPTY_BOX],
        );
    });

    test("with the Talking Book tool open, which marks each sentence as the user types, the typing of each line and each Enter are still separate steps", async ({
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
            [["One.", ""], ["One."], EMPTY_BOX],
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
