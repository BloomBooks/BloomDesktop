// Drive and read a table in the text of a text box (BL-17178): the table that the text box's
// right-click menu's Insert Table puts between two paragraphs of one language's text.
//
// Such a table is a child of the language's text box (`.bloom-editable`), between its paragraphs,
// with the class `bloom-tableInText`. Its cells are ordinary table cells, so the cell helpers in
// tables.ts (cell, typeInCellKeyByKey, getCellText) work on it as on any other table. What this
// file adds is what only a table in text has: the command that inserts it, the layout of the text
// around it, the caret and selections in that text, and the grip that drags it.
//
// Coordinates measured inside the page are relative to the page's frame; mouse coordinates are
// relative to Bloom's window. pointInWindow adds the frame's offset, in one place.

import { expect, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import {
    clickContextMenuCommand,
    rightClickAt,
    waitForContextMenu,
} from "./inlineImages";
import { realClick } from "./realClick";
import { cellTextBox, pageFrameOffset } from "./tables";

/** The localization id of Insert Table on a text box's right-click menu. */
const kInsertTableCommand = "EditTab.TableInText.InsertTable";
const kTableInTextClass = "bloom-tableInText";
const kGripClass = "bloom-tableInTextGrip";
const kWaitMs = 30000;

/** One language's text box in a translation group, e.g. ".bloom-translationGroup" and "en". */
function textBox(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Locator {
    return editablePageFrame(page)
        .locator(groupSelector)
        .first()
        .locator(`.bloom-editable[lang="${languageTag}"]`)
        .first();
}

/** Turn a point measured inside the page's frame into one in Bloom's window, for the mouse. */
async function pointInWindow(
    page: Page,
    point: { x: number; y: number },
): Promise<{ x: number; y: number }> {
    const offset = await pageFrameOffset(page);
    return { x: offset.x + point.x, y: offset.y + point.y };
}

/**
 * The text box's children as a list a test can compare: "[table]" for a table in the text, and
 * for a paragraph (or heading) its tag and text, e.g. "P:Some words." The tables' cells are not
 * listed; read them with getCellText in tables.ts.
 */
export async function getTextBoxLayout(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<string[]> {
    return textBox(page, groupSelector, languageTag).evaluate(
        (box, tableClass) =>
            Array.from(box.children)
                .filter((child) => !child.classList.contains("bloom-ui"))
                .map((child) =>
                    child.classList.contains(tableClass)
                        ? "[table]"
                        : `${child.tagName}:${(child.textContent ?? "").trim()}`,
                ),
        kTableInTextClass,
    );
}

/** How many tables are in the text of one language's text box. */
export async function countTablesInText(
    page: Page,
    groupSelector: string,
    languageTag: string,
): Promise<number> {
    return textBox(page, groupSelector, languageTag)
        .locator(`> .${kTableInTextClass}`)
        .count();
}

/**
 * Where in the frame the text just after the first `text` in the box's own text is (outside its
 * tables): the middle of the right half of its last letter, where a click puts the caret after it.
 */
async function pointAfterText(
    box: Locator,
    text: string,
): Promise<{ x: number; y: number }> {
    await box.waitFor({ state: "visible", timeout: kWaitMs });
    const found = await box.evaluate(
        (element, args) => {
            const walker = document.createTreeWalker(
                element,
                NodeFilter.SHOW_TEXT,
            );
            while (walker.nextNode()) {
                const node = walker.currentNode as Text;
                if (node.parentElement?.closest("." + args.tableClass))
                    continue;
                const at = node.data.indexOf(args.text);
                if (at < 0) continue;
                // To the middle of the view, not just into it: in a short window, text scrolled
                // only into view sits at the top, under the bar that holds the Change Layout switch.
                node.parentElement!.scrollIntoView({ block: "center" });
                const range = document.createRange();
                const end = at + args.text.length;
                range.setStart(node, end - 1);
                range.setEnd(node, end);
                const rect = range.getBoundingClientRect();
                return {
                    x: rect.right - rect.width / 4,
                    y: rect.top + rect.height / 2,
                };
            }
            return undefined;
        },
        { text, tableClass: kTableInTextClass },
    );
    if (!found)
        throw new Error(
            `The text box's own text (outside its tables) has no "${text}" to point after.`,
        );
    return found;
}

/**
 * Right-click just after `afterText` in one language's text box and choose Insert Table, the way a
 * person does. The right-click puts the caret there, and the table goes in at the caret. Returns
 * once the box holds one more table than before.
 */
export async function insertTableInTextAfter(
    page: Page,
    groupSelector: string,
    languageTag: string,
    afterText: string,
): Promise<void> {
    const before = await countTablesInText(page, groupSelector, languageTag);
    const box = textBox(page, groupSelector, languageTag);
    const at = await pointInWindow(page, await pointAfterText(box, afterText));
    // A left click first, as a person's first click puts the caret in the text; then the right
    // click at the same place opens the menu without moving it.
    await page.mouse.click(at.x, at.y);
    await rightClickAt(page, at.x, at.y);
    await waitForContextMenu(page, `after "${afterText}"`);
    await clickContextMenuCommand(page, kInsertTableCommand);
    await expect
        .poll(async () => countTablesInText(page, groupSelector, languageTag), {
            timeout: kWaitMs,
            message: `Insert Table did not add a table to the "${languageTag}" text box.`,
        })
        .toBe(before + 1);
}

/**
 * Put the caret at the start of the paragraph just after the table, or at the end of the one just
 * before it, and return once it is there. The caret is set directly: it is the start state for the
 * key the test presses next, not what it measures. A click would not do for the paragraph after:
 * a click just below a table can land on the table's own add-row button (a known open item on
 * BL-17178).
 */
export async function putCaretBesideTable(
    page: Page,
    groupSelector: string,
    languageTag: string,
    where: "startOfParagraphAfter" | "endOfParagraphBefore",
): Promise<void> {
    const box = textBox(page, groupSelector, languageTag);
    await box.focus();
    await box.evaluate(
        (element, args) => {
            const table = element.querySelector(
                ":scope > ." + args.tableClass,
            )!;
            const paragraph =
                args.where === "startOfParagraphAfter"
                    ? table.nextElementSibling!
                    : table.previousElementSibling!;
            const range = element.ownerDocument.createRange();
            range.selectNodeContents(paragraph);
            range.collapse(args.where === "startOfParagraphAfter");
            const selection = element.ownerDocument.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);
        },
        { where, tableClass: kTableInTextClass },
    );
    const caretIsThere = async () =>
        box.evaluate(
            (element, args) => {
                const selection = element.ownerDocument.getSelection();
                if (!selection || selection.rangeCount === 0)
                    return "there is no selection";
                const caret = selection.getRangeAt(0);
                const where = `${caret.startContainer.nodeName} "${(caret.startContainer.textContent ?? "").slice(0, 30)}" at ${caret.startOffset}`;
                if (!selection.isCollapsed)
                    return `the selection is not collapsed (${where})`;
                const table = element.querySelector(
                    ":scope > ." + args.tableClass,
                )!;
                const paragraph =
                    args.where === "startOfParagraphAfter"
                        ? table.nextElementSibling!
                        : table.previousElementSibling!;
                if (!paragraph.contains(caret.startContainer))
                    return `the caret is outside the paragraph (${where})`;
                const between = element.ownerDocument.createRange();
                between.selectNodeContents(paragraph);
                if (args.where === "startOfParagraphAfter")
                    between.setEnd(caret.startContainer, caret.startOffset);
                else between.setStart(caret.startContainer, caret.startOffset);
                return between.toString().trim() === ""
                    ? "there"
                    : `the caret is inside the paragraph's text (${where})`;
            },
            { where, tableClass: kTableInTextClass },
        );
    await expect
        .poll(caretIsThere, {
            timeout: kWaitMs,
            message: `Setting the caret did not leave it at the ${where} of the table.`,
        })
        .toBe("there");
}

/**
 * Select the text box's text from just after `fromText` (above a table) to just after `toText`
 * (below it), so that the selection takes in the table, as a person's drag across it would. The
 * selection is set directly: it is the start state for what the test presses next, not what it
 * measures.
 */
export async function selectAcrossTable(
    page: Page,
    groupSelector: string,
    languageTag: string,
    fromText: string,
    toText: string,
): Promise<void> {
    const box = textBox(page, groupSelector, languageTag);
    await box.focus();
    const selected = await box.evaluate(
        (element, args) => {
            const find = (text: string) => {
                const walker = document.createTreeWalker(
                    element,
                    NodeFilter.SHOW_TEXT,
                );
                while (walker.nextNode()) {
                    const node = walker.currentNode as Text;
                    if (node.parentElement?.closest("." + args.tableClass))
                        continue;
                    const at = node.data.indexOf(text);
                    if (at >= 0) return { node, offset: at + text.length };
                }
                throw new Error(
                    `The text box has no "${text}" outside its tables.`,
                );
            };
            const from = find(args.fromText);
            const to = find(args.toText);
            const range = document.createRange();
            range.setStart(from.node, from.offset);
            range.setEnd(to.node, to.offset);
            const selection = element.ownerDocument.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);
            return selection.toString();
        },
        { fromText, toText, tableClass: kTableInTextClass },
    );
    if (!selected)
        throw new Error("Selecting across the table selected nothing.");
}

/**
 * Click the top bar's Cut or Paste button, the way a person does, once Bloom has enabled it.
 * Returns as soon as the click is made; what it should have done is the caller's question.
 */
export async function clickTopBarButton(
    page: Page,
    which: "cut" | "paste",
): Promise<void> {
    const button = page.getByTestId(`${which}-button`);
    await expect(
        button,
        `The top bar's ${which} button never became enabled.`,
    ).toBeEnabled({ timeout: kWaitMs });
    await realClick(button);
}

/**
 * Drag the table by its grip (the six dots left of its first row) and drop it just after
 * `afterText` in the same text box, the way a person does. A drop inside a paragraph splits it,
 * with the table between the halves. The grip shows only while the pointer is over the page, so
 * the pointer goes over the table first. Returns once the table's place in the text has changed.
 */
export async function dragTableToAfterText(
    page: Page,
    groupSelector: string,
    languageTag: string,
    afterText: string,
): Promise<void> {
    const before = await getTextBoxLayout(page, groupSelector, languageTag);
    const box = textBox(page, groupSelector, languageTag);
    const table = box.locator(`> .${kTableInTextClass}`).first();
    await table.scrollIntoViewIfNeeded({ timeout: kWaitMs });
    const tableBox = await table.boundingBox();
    if (!tableBox)
        throw new Error("The table in the text has no on-screen box.");
    await page.mouse.move(
        tableBox.x + tableBox.width / 2,
        tableBox.y + tableBox.height / 2,
    );
    const grip = editablePageFrame(page).locator(`.${kGripClass}`).first();
    await expect(
        grip,
        "The table's grip did not show with the pointer over the page.",
    ).toBeVisible({
        timeout: kWaitMs,
    });
    const gripBox = await grip.boundingBox();
    if (!gripBox) throw new Error("The table's grip has no on-screen box.");
    const drop = await pointInWindow(
        page,
        await pointAfterText(box, afterText),
    );
    await page.mouse.move(
        gripBox.x + gripBox.width / 2,
        gripBox.y + gripBox.height / 2,
    );
    await page.mouse.down();
    // Several steps, as a hand moves, so the drag sees the pointer travel and shows where it lands.
    await page.mouse.move(drop.x, drop.y, { steps: 12 });
    await page.mouse.up();
    await expect
        .poll(async () => getTextBoxLayout(page, groupSelector, languageTag), {
            timeout: kWaitMs,
            message: `Dragging the table to just after "${afterText}" did not move it.`,
        })
        .not.toEqual(before);
}

/**
 * Whether any of the text in one language's text box of a cell is bold, the way Ctrl+B would make
 * it in an ordinary text box. Rows and columns count from 0, as in tables.ts.
 */
export async function cellTextIsBold(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
): Promise<boolean> {
    return (await cellTextBox(page, row, column, languageTag)).evaluate(
        (element) => !!element.querySelector("b, strong"),
    );
}
