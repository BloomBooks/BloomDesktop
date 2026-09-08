// The Edit tab's direct-formatting toolbar and its keyboard shortcuts.
//
// Every text box on a page is a CKEditor surface, and CKEditor floats a small toolbar above the box
// while text in it is selected (see attachToCkEditor in bookEdit/js/bloomEditing.ts, which shows
// and hides it on CKEditor's selectionCheck). Bloom's toolbar has Bold, Italic, Underline,
// Superscript, a text-color palette, Remove Formatting, and a hyperlink button. Ctrl+B, Ctrl+I and
// Ctrl+U are CKEditor's own shortcuts for the first three; Ctrl+Space is Bloom's "clear formatting",
// which runs the same removeFormat command as the button (AddEditKeyHandlers in bloomEditing.ts).
//
// What the toolbar writes is CKEditor's core styles: <strong>, <em>, <u>, <sup>, and a bare
// <span style="color: ..."> for text color. getFormattedRuns reads the box back in those terms, so a
// test asserts "this word is bold and underlined" rather than on markup.
//
// Undo is NOT here: it belongs to the top bar, so helpers/workspace.ts has it. `clickUndoButton`
// clicks the Undo button; `undo` runs the production undo path for the Ctrl+Z step, because Ctrl+Z
// is a WinForms accelerator no test can press (AUTOMATION-DEBT.md: "WinForms surfaces are
// invisible to CDP").

import { expect, type Locator, type Page } from "@playwright/test";
import { editablePageFrame, clickInGroup } from "./bookMaking";
import { pressKey } from "./keys";

/** A command of the formatting toolbar. Text color is separate (pickTextColorFromToolbar). */
export type FormatCommand =
    | "bold"
    | "italic"
    | "underline"
    | "superscript"
    | "removeFormat";

/** The character formatting of one run of text, as the toolbar can apply it. */
export interface IFormatting {
    bold: boolean;
    italic: boolean;
    underline: boolean;
    superscript: boolean;
    /** The text color as "#rrggbb" lower case, or absent when the run has the style's color. */
    color?: string;
}

/** A stretch of text within one paragraph of a box whose formatting is the same throughout. */
export interface IFormattedRun {
    /** Which paragraph of the box the run is in, counting from 0. */
    paragraph: number;
    text: string;
    formatting: IFormatting;
}

/** No character formatting at all: what freshly typed text has. */
export const PLAIN: IFormatting = {
    bold: false,
    italic: false,
    underline: false,
    superscript: false,
};

/** The class CKEditor gives each toolbar button's anchor, e.g. cke_button__bold. */
const BUTTON_CLASS: Record<FormatCommand | "textColor", string> = {
    bold: "cke_button__bold",
    italic: "cke_button__italic",
    underline: "cke_button__underline",
    superscript: "cke_button__superscript",
    removeFormat: "cke_button__removeformat",
    textColor: "cke_button__textcolor",
};

/** The keyboard shortcut for each command that has one. Superscript has none. */
const SHORTCUT: Partial<Record<FormatCommand, string>> = {
    bold: "Control+b",
    italic: "Control+i",
    underline: "Control+u",
    removeFormat: "Control+Space",
};

/** One language's box of one translation group on the page being shown. */
function boxOf(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Locator {
    return editablePageFrame(page)
        .locator(`${groupSelector} .bloom-editable[lang="${languageTag}"]`)
        .first();
}

/**
 * Select `text`, the first place it occurs in one language's box of one translation group, the way
 * a person does with the keyboard: click just before its first character, then Shift+ArrowRight
 * once per character. The text may run across formatted and unformatted stretches. Returns once the
 * browser reports exactly that text selected, which is also when CKEditor has shown the formatting
 * toolbar for it.
 *
 * Throws when the text is not in the box, or when the selection came out as something else.
 */
export async function selectTextInGroup(
    page: Page,
    groupSelector: string,
    languageTag: string,
    text: string,
): Promise<Locator> {
    if (!text) throw new Error("selectTextInGroup needs some text to select.");
    const box = boxOf(page, groupSelector, languageTag);
    await box.waitFor({ state: "visible", timeout: 30000 });

    // Where the first character of the text is, relative to the box, so the click below lands
    // just before it. The text can start in one text node and end in another, so this searches
    // the box's text as a whole and maps the hit back to its node.
    const start = await box.evaluate((element, wanted) => {
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        const nodes: Text[] = [];
        let whole = "";
        let node: Node | null;
        while ((node = walker.nextNode())) {
            nodes.push(node as Text);
            whole += node.textContent ?? "";
        }
        const index = whole.indexOf(wanted);
        if (index < 0) return { found: false as const, whole };
        let passed = 0;
        for (const textNode of nodes) {
            const length = textNode.textContent?.length ?? 0;
            if (index < passed + length) {
                const range = document.createRange();
                range.setStart(textNode, index - passed);
                range.setEnd(textNode, index - passed + 1);
                const rect = range.getBoundingClientRect();
                const boxRect = element.getBoundingClientRect();
                return {
                    found: true as const,
                    x: rect.left - boxRect.left + Math.min(2, rect.width / 3),
                    y: rect.top - boxRect.top + rect.height / 2,
                };
            }
            passed += length;
        }
        return { found: false as const, whole };
    }, text);
    if (!start.found)
        throw new Error(
            `The "${languageTag}" box of "${groupSelector}" does not contain "${text}". ` +
                `Its text is: "${start.whole}".`,
        );

    await box.click({ position: { x: start.x, y: start.y } });
    await expect(
        box,
        `Clicking in the "${languageTag}" box of "${groupSelector}" did not give it the focus.`,
    ).toBeFocused({ timeout: 15000 });
    // One press per character as the caret counts them: an emoji, or a letter with its combining
    // accents, is one caret step but several UTF-16 code units, so text.length would overrun.
    const graphemes = [
        ...new Intl.Segmenter(undefined, { granularity: "grapheme" }).segment(
            text,
        ),
    ].length;
    for (let i = 0; i < graphemes; i++) {
        await pressKey(page, "Shift+ArrowRight");
    }

    const selected = await editablePageFrame(page).evaluate(
        () => document.getSelection()?.toString() ?? "",
    );
    if (selected !== text)
        throw new Error(
            `Meant to select "${text}" but the browser reports "${selected}" selected.`,
        );
    return box;
}

/**
 * Select everything in one language's box of one translation group, the way a person does: click
 * in it and press Ctrl+A. Returns the box.
 */
export async function selectAllInGroup(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<Locator> {
    const box = await clickInGroup(page, groupSelector, languageTag);
    await box.press("Control+a");
    // The selection's text has a line break between paragraphs and textContent has none, so the
    // two are compared with the whitespace taken out.
    const withoutSpaces = (text: string) => text.replace(/\s/g, "");
    const expected = withoutSpaces((await box.textContent()) ?? "");
    await expect
        .poll(
            async () =>
                withoutSpaces(
                    await editablePageFrame(page).evaluate(
                        () => document.getSelection()?.toString() ?? "",
                    ),
                ),
            {
                timeout: 15000,
                message: `Ctrl+A did not select the whole text of the "${languageTag}" box of "${groupSelector}".`,
            },
        )
        .toBe(expected);
    return box;
}

/**
 * The formatting toolbar's button for a command. The toolbar exists once per box and shows only
 * while text in that box is selected, so this is the one that is visible right now.
 */
function formatButton(
    page: Page,
    command: FormatCommand | "textColor",
): Locator {
    return editablePageFrame(page).locator(
        `a.${BUTTON_CLASS[command]}:visible`,
    );
}

/**
 * The formatting toolbar's button for a command, waited for. Throws, saying why, when the toolbar
 * is not showing, which is what happens when no text is selected.
 */
async function visibleFormatButton(
    page: Page,
    command: FormatCommand | "textColor",
): Promise<Locator> {
    const button = formatButton(page, command);
    try {
        await button.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        throw new Error(
            `The formatting toolbar is not showing its ${command} button. The toolbar appears ` +
                `only while text in a box is selected; select some first.`,
        );
    }
    return button;
}

/**
 * Whether the toolbar shows a style button as "on", which CKEditor does when the selected text
 * already has that style. Only the four style buttons have such a state.
 */
async function isFormatButtonOn(
    page: Page,
    command: FormatCommand,
): Promise<boolean> {
    const classes =
        (await formatButton(page, command).getAttribute("class")) ?? "";
    return classes.split(/\s+/).includes("cke_button_on");
}

/**
 * Whether the current selection has none of the formatting the toolbar can put on text: nothing
 * formatted inside it, and no formatting element around it either.
 */
async function isSelectionPlain(page: Page): Promise<boolean> {
    return editablePageFrame(page).evaluate(() => {
        const selection = document.getSelection();
        if (!selection || selection.rangeCount === 0) return false;
        const range = selection.getRangeAt(0);
        const formatted = "strong,b,em,i,u,sup,span[style]";
        if (range.cloneContents().querySelector(formatted)) return false;
        const around =
            range.commonAncestorContainer instanceof Element
                ? range.commonAncestorContainer
                : range.commonAncestorContainer.parentElement;
        return !around?.closest(formatted);
    });
}

/**
 * Wait for a formatting command to have taken effect on the selection. A style command toggles
 * its toolbar button, so this waits for the button to leave the state it was in before; Remove
 * Formatting has no button state, so this waits for the selection to be plain.
 */
async function waitForFormatCommand(
    page: Page,
    command: FormatCommand,
    buttonWasOn: boolean,
): Promise<void> {
    if (command === "removeFormat") {
        await expect
            .poll(() => isSelectionPlain(page), {
                timeout: 15000,
                message:
                    "Remove Formatting left formatting on the selected text.",
            })
            .toBe(true);
        return;
    }
    await expect
        .poll(() => isFormatButtonOn(page, command), {
            timeout: 15000,
            message: `The ${command} button never changed state, so the command did not land.`,
        })
        .toBe(!buttonWasOn);
}

/**
 * Click a button of the formatting toolbar that floats above the box with selected text: Bold,
 * Italic, Underline, Superscript, or Remove Formatting. Select the text first
 * (selectTextInGroup); the toolbar is not there otherwise, and this says so.
 *
 * Returns once the command has taken effect on the selection (see waitForFormatCommand). The
 * Text Color button only opens the palette; pickTextColorFromToolbar drives that.
 */
export async function clickFormatButton(
    page: Page,
    command: FormatCommand | "textColor",
): Promise<void> {
    const button = await visibleFormatButton(page, command);
    if (command === "textColor") {
        await button.click();
        return;
    }
    const wasOn = await isFormatButtonOn(page, command);
    await button.click();
    await waitForFormatCommand(page, command, wasOn);
}

/**
 * Press the keyboard shortcut for a formatting command into the box that has the focus: Ctrl+B,
 * Ctrl+I, Ctrl+U, or Ctrl+Space for Remove Formatting. Superscript has no shortcut, and asking for
 * one throws. Returns once the command has taken effect on the selection, read from the same
 * toolbar the buttons live on, so the text must be selected here too.
 */
export async function pressFormatShortcut(
    page: Page,
    command: FormatCommand,
): Promise<void> {
    const key = SHORTCUT[command];
    if (!key) throw new Error(`There is no keyboard shortcut for ${command}.`);
    await visibleFormatButton(page, command);
    const wasOn = await isFormatButtonOn(page, command);
    await pressKey(page, key);
    await waitForFormatCommand(page, command, wasOn);
}

/**
 * Give the selected text a color from the toolbar's palette, the way a person does: click the text
 * color button, then click the swatch for `hex` ("#rrggbb", any case) in the palette that drops
 * down. The swatches are the collection's text palette (TextColorPalette in
 * react_components/color-picking/bloomPalette.ts, unless the collection has customized it).
 *
 * Throws, listing the swatches on offer, when the palette has no such color.
 */
export async function pickTextColorFromToolbar(
    page: Page,
    hex: string,
): Promise<void> {
    const code = hex.replace(/^#/, "").toLowerCase();
    if (!/^[0-9a-f]{6}$/.test(code))
        throw new Error(`Expected a color like "#ff1616", not "${hex}".`);

    // CKEditor renders the palette inside an iframe of its own within a drop-down panel. The
    // panel does not reliably stay open after the click: Bloom hides every CKEditor panel whenever
    // CKEditor checks the selection (attachToCkEditor in bloomEditing.ts), and that check runs on a
    // timer a moment after the click, so the panel can vanish right after it opened, or under the
    // swatch click, leaving the button "on" over a hidden panel. A person clicks the button again,
    // and so does this: the next click closes the panel CKEditor thinks is open, the one after
    // opens it. (AUTOMATION-DEBT.md: "The text color palette sometimes does not open on the first
    // click".)
    const frame = editablePageFrame(page);
    const panel = frame.locator(".cke_panel:visible");
    const swatches = panel.frameLocator("iframe").locator("a.cke_colorbox");
    const swatch = swatches.filter({
        has: panel
            .frameLocator("iframe")
            .locator(`span.cke_colorbox[style*="${code}" i]`),
    });
    const selectedText = () =>
        editablePageFrame(page).evaluate(
            () => document.getSelection()?.toString() ?? "",
        );
    const selectedBefore = await selectedText();
    for (let attempt = 1; attempt <= 6; attempt++) {
        await clickFormatButton(page, "textColor");
        const opened = await panel
            .waitFor({ state: "visible", timeout: 2000 })
            .then(() => true)
            .catch(() => false);
        if (!opened) continue;
        // CKEditor checks the selection at most 200ms after the last mouse or key event (its
        // checkSelectionChange throttle), and that is the check in which Bloom may hide the panel
        // that has just opened. A swatch click delivered into a panel that closes at that moment
        // lands on the text beneath it and moves the selection, which is worse than a retry. So
        // wait out that one check, and click only a panel that is still showing afterwards.
        await page.waitForTimeout(kSelectionCheckMs);
        if (!(await panel.isVisible())) continue;
        if ((await swatch.count()) !== 1) {
            const offered = await panel
                .frameLocator("iframe")
                .locator("span.cke_colorbox")
                .evaluateAll((spans) =>
                    spans
                        .map((s) => s.getAttribute("style") ?? "")
                        .filter((style) => style.includes("background-color"))
                        .map((style) =>
                            style.replace(/.*#/, "#").replace(/[;\s]/g, ""),
                        ),
                );
            throw new Error(
                `The text color palette has no swatch for #${code}. It offers: ${offered.join(", ")}.`,
            );
        }
        await swatch.click();
        await panel.waitFor({ state: "hidden", timeout: 15000 });
        const selectedAfter = await selectedText();
        if (selectedAfter !== selectedBefore)
            throw new Error(
                `Picking a text color changed the selection from "${selectedBefore}" to ` +
                    `"${selectedAfter}", so the click did not land on the palette.`,
            );
        return;
    }
    throw new Error(
        "The text color palette never stayed open long enough to pick a color, though the Text " +
            "Color button was clicked six times.",
    );
}

/**
 * How long CKEditor can take to run its selection check after a mouse or key event: it throttles
 * checkSelectionChange to one per 200ms. pickTextColorFromToolbar waits this long after opening
 * the palette, because the check that follows the click is the one in which Bloom may close it.
 */
const kSelectionCheckMs = 250;

/**
 * Read back the character formatting of one language's box of one translation group, paragraph by
 * paragraph, as runs of text that share one formatting. Adjacent text with the same formatting is
 * one run, so a word that is bold throughout is in one run whether CKEditor wrote it as one
 * <strong> or two.
 */
export async function getFormattedRuns(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<IFormattedRun[]> {
    const box = boxOf(page, groupSelector, languageTag);
    await box.waitFor({ state: "visible", timeout: 30000 });
    return box.evaluate((element) => {
        // The browser reports an inline color as rgb(); the palette and the tests speak hex.
        const toHex = (css: string): string => {
            const match = /^rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(css);
            if (!match) return css.toLowerCase();
            return (
                "#" +
                [match[1], match[2], match[3]]
                    .map((n) => Number(n).toString(16).padStart(2, "0"))
                    .join("")
            );
        };
        const same = (a: IFormatting, b: IFormatting) =>
            a.bold === b.bold &&
            a.italic === b.italic &&
            a.underline === b.underline &&
            a.superscript === b.superscript &&
            a.color === b.color;

        const paragraphs = Array.from(element.querySelectorAll(":scope > p"));
        const blocks: Element[] = paragraphs.length ? paragraphs : [element];
        const runs: IFormattedRun[] = [];
        blocks.forEach((block, paragraph) => {
            const walker = document.createTreeWalker(
                block,
                NodeFilter.SHOW_TEXT,
            );
            let node: Node | null;
            while ((node = walker.nextNode())) {
                const text = node.textContent ?? "";
                if (!text) continue;
                const formatting: IFormatting = {
                    bold: false,
                    italic: false,
                    underline: false,
                    superscript: false,
                };
                for (
                    let ancestor = node.parentElement;
                    ancestor && ancestor !== block;
                    ancestor = ancestor.parentElement
                ) {
                    const tag = ancestor.tagName;
                    if (tag === "STRONG" || tag === "B") formatting.bold = true;
                    else if (tag === "EM" || tag === "I")
                        formatting.italic = true;
                    else if (tag === "U") formatting.underline = true;
                    else if (tag === "SUP") formatting.superscript = true;
                    if (
                        tag === "SPAN" &&
                        (ancestor as HTMLElement).style.color &&
                        formatting.color === undefined
                    )
                        formatting.color = toHex(
                            (ancestor as HTMLElement).style.color,
                        );
                }
                const last = runs[runs.length - 1];
                if (
                    last &&
                    last.paragraph === paragraph &&
                    same(last.formatting, formatting)
                )
                    last.text += text;
                else runs.push({ paragraph, text, formatting });
            }
        });
        return runs;
    });
}

/** The runs, one line each, for a failure message. */
export function describeRuns(runs: IFormattedRun[]): string {
    if (runs.length === 0) return "(the box has no text)";
    return runs
        .map((run) => {
            const on: string[] = (
                ["bold", "italic", "underline", "superscript"] as const
            ).filter((k) => run.formatting[k]);
            if (run.formatting.color) on.push(run.formatting.color);
            return `  ¶${run.paragraph} "${run.text}" [${on.join(", ") || "plain"}]`;
        })
        .join("\n");
}

/** The text of each paragraph of the box, with the formatting left out. */
export function paragraphTextsOf(runs: IFormattedRun[]): string[] {
    const texts: string[] = [];
    for (const run of runs) {
        texts[run.paragraph] = (texts[run.paragraph] ?? "") + run.text;
    }
    return texts;
}

/**
 * The formatting of `text` in the box: the formatting of the one run that contains it. When no
 * single run does, because the text is not there or is formatted differently along its length, the
 * answer is a description of the runs instead, so that an assertion on it fails naming what the box
 * holds.
 */
export function formattingOf(
    runs: IFormattedRun[],
    text: string,
): IFormatting | string {
    const run = runs.find((r) => r.text.includes(text));
    if (!run)
        return `no single run holds "${text}"; the runs are:\n${describeRuns(runs)}`;
    return run.formatting;
}

/**
 * Assert, polling, that `text` in the box has exactly this formatting. Polled because a toolbar
 * click or a shortcut is applied a moment after the event is delivered.
 */
export async function expectFormatting(
    page: Page,
    groupSelector: string,
    languageTag: string,
    text: string,
    expected: IFormatting,
): Promise<void> {
    await expect
        .poll(
            async () =>
                formattingOf(
                    await getFormattedRuns(page, groupSelector, languageTag),
                    text,
                ),
            {
                timeout: 15000,
                message: `"${text}" does not have the expected formatting.`,
            },
        )
        .toEqual(expected);
}
