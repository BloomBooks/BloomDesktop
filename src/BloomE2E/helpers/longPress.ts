// The long-press panel: hold a letter down in a text box and Bloom offers its special characters
// in a strip along the bottom of the page (src/BloomBrowserUI/lib/long-press/jquery.longpress.js).
//
// The panel exists only while the key is down, so every helper here that chooses a character
// expects holdKeyUntilLongPressPanelShows to have been called, and releases the key itself.
//
// Holding a key is a keydown followed by repeats of it, and that is what the panel listens for:
// the first keydown types the letter, and a second one for the same key opens the panel. Playwright
// marks a keydown for a key that is already down as a repeat, as the keyboard does.
//
// Bloom turns long-press off when a keyboarding program such as Keyman is the active input method
// (keyboarding/useLongpress). On such a machine long-press is never attached to a text box, and
// holdKeyUntilLongPressPanelShows says so.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";

/** The panel of special characters, in the frame of the page being edited. */
function longPressPanel(frame: Frame): Locator {
    return frame.locator(".long-press-popup");
}

/** The button for one special character in the panel. */
function longPressButton(frame: Frame, character: string): Locator {
    return longPressPanel(frame).locator(
        `li.long-press-letter[data-value="${character}"]`,
    );
}

/** The special characters the panel offers, in the order it shows them. */
async function getOfferedCharacters(frame: Frame): Promise<string[]> {
    return longPressPanel(frame)
        .locator("li.long-press-letter")
        .evaluateAll((buttons) =>
            buttons.map((button) => button.getAttribute("data-value") ?? ""),
        );
}

/**
 * Where a character is in the panel, counting from 0. Throws, naming what the panel offers, when
 * the character is not there.
 */
async function indexOfOfferedCharacter(
    frame: Frame,
    character: string,
): Promise<number> {
    const offered = await getOfferedCharacters(frame);
    const index = offered.indexOf(character);
    if (index < 0)
        throw new Error(
            `The long-press panel does not offer "${character}". It offers: ${offered.join(" ")}.`,
        );
    return index;
}

/** Wait until the panel shows `character` as the selected one. */
async function expectSelectedCharacter(
    frame: Frame,
    character: string,
    how: string,
): Promise<void> {
    await expect(
        longPressPanel(frame).locator("li.long-press-letter.selected"),
        `${how} did not select "${character}" in the long-press panel.`,
    ).toHaveAttribute("data-value", character, { timeout: 15000 });
}

/** Wait until the panel has gone. `after` says what should have closed it. */
async function expectPanelClosed(page: Page, after: string): Promise<void> {
    await expect(
        longPressPanel(editablePageFrame(page)),
        `The long-press panel stayed up after ${after}.`,
    ).toHaveCount(0, { timeout: 15000 });
}

/** Let go of the held key, and wait until the panel has gone, as it does when a person lets go. */
async function releaseHeldKey(page: Page, key: string): Promise<void> {
    await page.keyboard.up(key);
    await expectPanelClosed(page, `the "${key}" key was released`);
}

/**
 * Wait until Bloom has attached long-press to the text box that has the focus.
 *
 * Bloom attaches it a moment after the page loads: it first asks whether long-press is to be used
 * at all, and fetches the panel's instructions (activateLongPressFor in bloomEditing.ts). A key
 * held down before that types its letter twice and shows no panel, for a person as for a test.
 * Nothing on the page shows when it has happened, so this asks the page's own script
 * (isLongPressAttachedTo, which is there for this).
 */
async function waitForLongPressAttached(frame: Frame): Promise<void> {
    await expect
        .poll(
            () =>
                frame.evaluate(() => {
                    const focused = document.activeElement;
                    return (
                        !!focused &&
                        (
                            window as unknown as {
                                editablePageBundle: {
                                    isLongPressAttachedTo: (
                                        element: Element,
                                    ) => boolean;
                                };
                            }
                        ).editablePageBundle.isLongPressAttachedTo(focused)
                    );
                }),
            {
                timeout: 15000,
                message:
                    "Bloom never attached long-press to the text box that has the focus. Bloom " +
                    "turns long-press off when a keyboarding program such as Keyman is the " +
                    "active input method.",
            },
        )
        .toBe(true);
}

/**
 * Hold a letter key down in the text box that has the focus, until the long-press panel shows.
 * The letter itself is typed at the caret, as it is for a person. Returns the special characters
 * the panel offers. The key stays down: choose a character with one of the helpers below, which
 * release it.
 */
export async function holdKeyUntilLongPressPanelShows(
    page: Page,
    key: string,
): Promise<string[]> {
    const frame = editablePageFrame(page);
    await waitForLongPressAttached(frame);
    await page.keyboard.down(key);
    // The key repeating, which is what tells Bloom that it is being held.
    await page.keyboard.down(key);
    await expect(
        longPressPanel(frame),
        `Holding "${key}" down never showed the long-press panel.`,
    ).toBeVisible({ timeout: 15000 });
    return getOfferedCharacters(frame);
}

/**
 * With the long-press panel showing, click a special character in it, then let go of the held key.
 * Moving the mouse onto the character already puts it in the text; the click closes the panel.
 */
export async function chooseLongPressCharacterWithClick(
    page: Page,
    heldKey: string,
    character: string,
): Promise<void> {
    const frame = editablePageFrame(page);
    await indexOfOfferedCharacter(frame, character);
    await longPressButton(frame, character).click();
    // The click closes the panel, so letting go of the key afterwards has nothing left to close.
    await expectPanelClosed(page, `"${character}" was clicked`);
    await page.keyboard.up(heldKey);
}

/**
 * With the long-press panel showing, turn the mouse wheel until a special character is selected,
 * one notch per character from the first, then let go of the held key. The mouse is put over
 * `over`, the text box being typed in, first, so that it is not over the panel: resting on a
 * character there selects it without the wheel.
 */
export async function chooseLongPressCharacterWithMouseWheel(
    page: Page,
    heldKey: string,
    character: string,
    over: Locator,
): Promise<void> {
    const frame = editablePageFrame(page);
    const index = await indexOfOfferedCharacter(frame, character);
    const offered = await getOfferedCharacters(frame);
    await over.hover();
    for (let notch = 0; notch <= index; notch++) {
        await page.mouse.wheel(0, 100);
        // Each notch moves one character along, so wait for it before the next.
        await expectSelectedCharacter(
            frame,
            offered[notch],
            `Notch ${notch + 1} of the mouse wheel`,
        );
    }
    await releaseHeldKey(page, heldKey);
}

/**
 * With the long-press panel showing, press the shortcut key the panel shows beside a special
 * character, then let go of the held key. Letting go of the shortcut key is what closes the panel:
 * Bloom closes it when any key comes up.
 */
export async function chooseLongPressCharacterWithShortcut(
    page: Page,
    heldKey: string,
    character: string,
): Promise<void> {
    const frame = editablePageFrame(page);
    await indexOfOfferedCharacter(frame, character);
    const shortcut = await longPressButton(frame, character).getAttribute(
        "data-shortcut",
    );
    if (!shortcut)
        throw new Error(
            `The long-press panel shows no shortcut key for "${character}".`,
        );
    await page.keyboard.down(shortcut.toLowerCase());
    await expectSelectedCharacter(
        frame,
        character,
        `The shortcut key "${shortcut}"`,
    );
    await page.keyboard.up(shortcut.toLowerCase());
    await expectPanelClosed(
        page,
        `the shortcut key "${shortcut}" was released`,
    );
    await page.keyboard.up(heldKey);
}

/** Where the caret is in a text box: which paragraph, and how many of its characters come before. */
export interface ICaretPlace {
    paragraph: number;
    offset: number;
}

/**
 * Where the caret is in a text box, or undefined when the box does not hold a collapsed caret
 * inside one of its paragraphs. Zero-width spaces, which the editor leaves in the text for its own
 * use, are not counted.
 */
export async function getCaretPlace(
    box: Locator,
): Promise<ICaretPlace | undefined> {
    return box.evaluate((element) => {
        const selection = element.ownerDocument.getSelection();
        if (!selection || selection.rangeCount === 0 || !selection.isCollapsed)
            return undefined;
        const paragraphs = Array.from(element.querySelectorAll("p"));
        const paragraph = paragraphs.findIndex((p) =>
            p.contains(selection.focusNode),
        );
        if (paragraph < 0) return undefined;
        const range = element.ownerDocument.createRange();
        range.selectNodeContents(paragraphs[paragraph]);
        range.setEnd(selection.focusNode!, selection.focusOffset);
        return {
            paragraph,
            offset: range.toString().replace(/​/g, "").length,
        };
    });
}

/** The text of each paragraph of a text box, without the editor's zero-width spaces. */
export async function getParagraphTexts(box: Locator): Promise<string[]> {
    return box.evaluate((element) =>
        Array.from(element.querySelectorAll("p")).map((p) =>
            (p.textContent ?? "").replace(/​/g, ""),
        ),
    );
}

/**
 * Put the caret in one paragraph of a text box, after `offset` of its characters, with the keys a
 * person uses: Ctrl+Home, then Down to the paragraph, Home, and Right along it. Each paragraph
 * must fit on one line. Returns once the caret is there.
 */
export async function putCaretInParagraph(
    box: Locator,
    paragraph: number,
    offset: number,
): Promise<void> {
    await box.click();
    await box.press("Control+Home");
    for (let line = 0; line < paragraph; line++) await box.press("ArrowDown");
    await box.press("Home");
    for (let character = 0; character < offset; character++)
        await box.press("ArrowRight");
    await expect
        .poll(() => getCaretPlace(box), {
            timeout: 15000,
            message:
                `The keys did not put the caret after ${offset} characters of paragraph ` +
                `${paragraph}.`,
        })
        .toEqual({ paragraph, offset });
}
