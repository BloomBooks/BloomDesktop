// The page history keeps the newest 50 undo steps (kDefaultMaxUndoSteps in
// bookEdit/pageHistory/pageHistory.ts) and drops older ones. These tests make more steps than that
// with real key presses, then check what Ctrl+Z, Ctrl+Y and the top bar's buttons do at the limit.
//
// Each step is one character typed, then End, which ends the typing step without moving the
// caret (docs/undo/TYPING-STEPS.md). The characters are all different, so the box's text says how
// many steps have been undone.
//
// The tests are serial because the second starts from where the first leaves the history.

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
import { deleteAllTextIn, pressKey } from "../helpers/keys";
import { canUndo, waitForPageHistory } from "../helpers/undo";

test.use({
    collectionSpec: { name: "undo-step-limit", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// How many undo steps the page history keeps.
const kMaxUndoSteps = 50;

// 52 different characters: two more steps than the history keeps.
const kCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

// The text box that the first test types into.
let box: Locator;

/** The box's text, without the zero width space that CKEditor may leave in it. */
async function readText(): Promise<string> {
    return ((await box.textContent()) ?? "").replace(/​/g, "");
}

/** True when the page history has something to redo. */
async function canRedo(page: Page): Promise<boolean> {
    return editablePageFrame(page).evaluate(() =>
        (
            window as unknown as {
                editablePageBundle: { canRedo: () => boolean };
            }
        ).editablePageBundle.canRedo(),
    );
}

/** Press key once and wait until the box holds expected; `what` names the press for the message. */
async function pressAndExpect(
    page: Page,
    key: string,
    expected: string,
    what: string,
): Promise<void> {
    await pressKey(page, key);
    await expect
        .poll(readText, {
            timeout: 15000,
            message: `After ${what}, the box should hold "${expected}".`,
        })
        .toBe(expected);
}

/** Make one undo step: type character, then End to end the typing step. */
async function typeOneStep(page: Page, character: string): Promise<void> {
    await page.keyboard.type(character);
    await pressKey(page, "End");
}

test.describe("the limit of 50 undo steps", () => {
    test("with 52 steps made, Ctrl+Z undoes the newest 50 and then nothing, and Ctrl+Y redoes all 50", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await waitForPageHistory(page);
        box = await clickInGroup(page, ".bloom-translationGroup", "en");
        await deleteAllTextIn(box, "the text box");

        for (const character of kCharacters) await typeOneStep(page, character);
        await expect.poll(readText).toBe(kCharacters);

        // Each Ctrl+Z takes back one character, down to the two whose steps the limit dropped
        // (with the step that emptied the box).
        for (let undone = 1; undone <= kMaxUndoSteps; undone++)
            await pressAndExpect(
                page,
                "Control+z",
                kCharacters.slice(0, kCharacters.length - undone),
                `Ctrl+Z number ${undone}`,
            );
        const kept = kCharacters.slice(0, kCharacters.length - kMaxUndoSteps);
        expect(kept, "test setup").toBe("ab");
        expect(
            await canUndo(page),
            "After 50 undos there should be nothing left to undo.",
        ).toBe(false);
        await expect(
            page.getByTestId("undo-button"),
            "The Undo button should turn off when there is nothing left to undo.",
        ).toBeDisabled({ timeout: 15000 });
        // One more Ctrl+Z changes nothing.
        await pressKey(page, "Control+z");
        await expect.poll(readText).toBe(kept);

        for (let redone = 1; redone <= kMaxUndoSteps; redone++)
            await pressAndExpect(
                page,
                "Control+y",
                kCharacters.slice(0, kept.length + redone),
                `Ctrl+Y number ${redone}`,
            );
        expect(await canRedo(page)).toBe(false);
        await expect(page.getByTestId("redo-button")).toBeDisabled({
            timeout: 15000,
        });
        await expect(page.getByTestId("undo-button")).toBeEnabled({
            timeout: 15000,
        });
    });

    test("at the limit, a new step after some undos drops the redo steps and is undone with the rest", async ({
        page,
    }) => {
        // The history holds the 50 newest steps from the test before, all done.
        for (let undone = 1; undone <= 10; undone++)
            await pressAndExpect(
                page,
                "Control+z",
                kCharacters.slice(0, kCharacters.length - undone),
                `Ctrl+Z number ${undone}`,
            );
        expect(await canRedo(page), "test setup").toBe(true);

        await typeOneStep(page, "1");
        const withNewStep = kCharacters.slice(0, kCharacters.length - 10) + "1";
        await expect.poll(readText).toBe(withNewStep);
        expect(
            await canRedo(page),
            "A new step should drop the redo steps.",
        ).toBe(false);

        // 40 steps were left, and the new one makes 41, all of which undo.
        for (let undone = 1; undone <= 41; undone++)
            await pressAndExpect(
                page,
                "Control+z",
                withNewStep.slice(0, withNewStep.length - undone),
                `Ctrl+Z number ${undone} after the new step`,
            );
        expect(await readText()).toBe("ab");
        expect(await canUndo(page)).toBe(false);
    });
});
