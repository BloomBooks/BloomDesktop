// Press keys, for real, into whatever has the focus.
//
// This exists because typeInGroup and typeInCell do NOT press keys. They insert text, which raises
// input events and nothing else, so anything in Bloom that listens for a keydown is untouched by
// them. That is fine when the subject is the text; it is useless when the subject is the key.
//
// Canvas pages need the difference. The canvas element manager handles Delete, the arrow keys and
// Ctrl+C/Ctrl+V for whatever element is selected, and a text box can sit inside such an element.
// So the question "does Backspace in an empty text box delete the whole element?" is a question
// about who gets the keydown, and only a real key press asks it. (AUTOMATION-DEBT.md: "Typing in a
// text box raises no key events".)
//
// Every press goes through Playwright's keyboard, which sends the same CDP raw key events the
// browser would build from a physical key. What it cannot do is send a key that Bloom's WinForms
// shell claims as an accelerator: Ctrl+Z never reaches the page at all, which is why undo has its
// own helper in workspace.ts rather than a press here.

import { expect, type Locator, type Page } from "@playwright/test";

/**
 * A key, or a combination, in Playwright's own notation: "Enter", "Backspace", "ArrowDown",
 * "Control+c". Named as a type so a spec reads as a gesture rather than as a string.
 */
export type Key = string;

/**
 * Press a key into whatever has the focus, once. Waits for nothing, because what the press should
 * do is the caller's question; assert on that.
 */
export async function pressKey(page: Page, key: Key): Promise<void> {
    await page.keyboard.press(key);
}

/** Press several keys in turn into whatever has the focus. */
export async function pressKeys(page: Page, keys: Key[]): Promise<void> {
    for (const key of keys) await page.keyboard.press(key);
}

/**
 * Put the caret in a text box and then press a key, so the press goes somewhere known. Returns the
 * box, and throws if it never took the focus, which is the failure a test would otherwise see much
 * later as a key that seemed to do nothing.
 */
export async function pressKeyIn(
    box: Locator,
    key: Key,
    what: string,
): Promise<Locator> {
    await box.waitFor({ state: "visible", timeout: 30000 });
    await box.click();
    await expect(
        box,
        `Clicking ${what} did not give it the focus, so pressing "${key}" would go elsewhere.`,
    ).toBeFocused({ timeout: 15000 });
    await box.press(key);
    return box;
}

/**
 * Put the caret at the end of a text box's text. Use this before a Backspace that should act on the
 * text rather than on the box: a fresh click can leave the caret anywhere in the line.
 */
export async function moveCaretToEnd(box: Locator): Promise<void> {
    await box.click();
    await box.press("Control+End");
}

/**
 * Type text with real key presses, one key per character, into whatever has the focus.
 *
 * Slower than inserting the text and only worth it when the subject is the typing itself. For
 * ordinary text, use typeInCell or typeInGroup.
 */
export async function typeWithKeys(page: Page, text: string): Promise<void> {
    await page.keyboard.type(text);
}
