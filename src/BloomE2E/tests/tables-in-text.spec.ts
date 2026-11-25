// Checks a table inserted into the text of a text box (BL-17178): Insert Table on the text box's
// right-click menu puts a table between two paragraphs of that language's text, at the caret.
//
// The table is part of the text, so everything that edits the text around it has to leave it
// alone: Backspace and Delete next to it, a selection across it that is deleted, typed over, cut
// or pasted over. Only the table's own menu deletes it. Its grip drags it elsewhere in the text,
// and it and what was typed in its cells survive leaving the page and coming back. Its cells have
// no formatting, and Ctrl+Z in a cell is the browser's own undo.
//
// Each test makes its own one-page book, so that none depends on what another did.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
} from "../helpers/bookMaking";
import { copyToClipboard } from "../helpers/clipboard";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { pressKeys } from "../helpers/keys";
import { waitForClickToSettle } from "../helpers/realClick";
import { getCellText, typeInCellKeyByKey } from "../helpers/tables";
import {
    cellTextIsBold,
    clickTopBarButton,
    countTablesInText,
    dragTableToAfterText,
    getTextBoxLayout,
    insertTableInTextAfter,
    putCaretBesideTable,
    readSavedTextBoxLayout,
    selectText,
} from "../helpers/tablesInText";

// Tables need a subscription and the experimental Tables feature, as for the other table tests.
test.use({
    collectionSpec: {
        name: "tables-in-text",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

const GROUP = ".bloom-translationGroup";
const LANG = "en";
const TEXT = "The kingfisher waits. The heron fishes. The otter swims.";
// How getTextBoxLayout lists the table and the paragraphs around it.
const TABLE = "[table]";
const FIRST_SENTENCE = "P:The kingfisher waits.";
const LAST_TWO_SENTENCES = "P:The heron fishes. The otter swims.";
// The text box after Insert Table just after the first sentence.
const LAYOUT_AFTER_INSERT = [FIRST_SENTENCE, TABLE, LAST_TWO_SENTENCES];

/**
 * Make a book with one Just Text page, type TEXT on it, and insert a table just after `afterText`
 * (by default its first sentence). Returns the book's folder.
 */
async function makePageWithTableInText(
    page: Page,
    afterText = "kingfisher waits.",
): Promise<string> {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    await goToPage(page, (await getContentPages(page))[0].id);
    await typeInGroup(page, GROUP, LANG, TEXT);
    await insertTableInTextAfter(page, GROUP, LANG, afterText);
    return bookFolder;
}

test("Insert Table puts a table between the halves of the paragraph, and it keeps what was typed in it", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a page with a table in its text", async () =>
        makePageWithTableInText(page),
    );
    expect(await getTextBoxLayout(page, GROUP, LANG)).toEqual(
        LAYOUT_AFTER_INSERT,
    );

    await step("Type in a cell, leave the page and come back", async () => {
        await typeInCellKeyByKey(page, 0, 0, LANG, "Bird");
        await reloadPageBeingEdited(page);
    });

    expect(await getTextBoxLayout(page, GROUP, LANG)).toEqual(
        LAYOUT_AFTER_INSERT,
    );
    expect(await getCellText(page, 0, 0, LANG)).toBe("Bird");
});

test("A table that ends the text is saved with no empty paragraph below it, and gets one back in the Edit tab", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let bookFolder = "";
    await step("Make a page with a table at the end of its text", async () => {
        bookFolder = await makePageWithTableInText(page, "otter swims.");
    });
    const withParagraphBelow = ["P:" + TEXT, TABLE, "P:"];
    expect(await getTextBoxLayout(page, GROUP, LANG)).toEqual(
        withParagraphBelow,
    );

    await step("Leave the page, which saves it, and come back", async () =>
        reloadPageBeingEdited(page),
    );

    const pageId = (await getContentPages(page))[0].id;
    expect(
        await readSavedTextBoxLayout(page, bookFolder, pageId, GROUP, LANG),
        "The saved page should have no empty paragraph below the table.",
    ).toEqual(["P:" + TEXT, TABLE]);
    expect(
        await getTextBoxLayout(page, GROUP, LANG),
        "The Edit tab should give the table a paragraph below it again, to type in.",
    ).toEqual(withParagraphBelow);
});

test("Backspace and Delete next to a table do not delete it", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a page with a table in its text", async () =>
        makePageWithTableInText(page),
    );

    await step(
        "Press Backspace at the start of the paragraph after the table",
        async () => {
            await putCaretBesideTable(
                page,
                GROUP,
                LANG,
                "startOfParagraphAfter",
            );
            await pressKeys(page, ["Backspace", "Backspace", "Backspace"]);
            // Typed after the presses, so it lands only once they have been handled.
            await page.keyboard.type("X");
        },
    );
    await expect
        .poll(async () => getTextBoxLayout(page, GROUP, LANG))
        .toEqual([
            FIRST_SENTENCE,
            TABLE,
            "P:XThe heron fishes. The otter swims.",
        ]);

    await step(
        "Press Delete at the end of the paragraph before the table",
        async () => {
            await putCaretBesideTable(
                page,
                GROUP,
                LANG,
                "endOfParagraphBefore",
            );
            await pressKeys(page, ["Delete", "Delete", "Delete"]);
            await page.keyboard.type("Y");
        },
    );
    await expect
        .poll(async () => getTextBoxLayout(page, GROUP, LANG))
        .toEqual([
            "P:The kingfisher waits.Y",
            TABLE,
            "P:XThe heron fishes. The otter swims.",
        ]);
});

test("A selection across a table can be deleted, but not typed over, cut or pasted over, and the table stays", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a page with a table in its text", async () =>
        makePageWithTableInText(page),
    );

    await step("Select across the table and press Delete", async () => {
        await selectText(page, GROUP, LANG, "The kingfisher", "The heron");
        await pressKeys(page, ["Delete"]);
    });
    const afterDelete = [
        "P:The kingfisher",
        TABLE,
        "P:fishes. The otter swims.",
    ];
    await expect
        .poll(async () => getTextBoxLayout(page, GROUP, LANG), {
            message:
                "Delete should remove the selected text and keep the table.",
        })
        .toEqual(afterDelete);

    await step("Select across the table and type a letter", async () => {
        await selectText(page, GROUP, LANG, "The", "fishes.");
        await page.keyboard.type("Z");
        await waitForClickToSettle(editablePageFrame(page));
    });
    expect(await getTextBoxLayout(page, GROUP, LANG)).toEqual(afterDelete);

    // Bloom does the top bar's commands in the order they are clicked, and Cut looks at the
    // selection as soon as it runs. So once a second Cut, of a word below the table, has taken
    // that word out, the first one has been handled too.
    await step(
        "Select across the table and click the top bar's Cut, then Cut a word below the table",
        async () => {
            await selectText(page, GROUP, LANG, "The", "fishes.");
            await clickTopBarButton(page, "cut");
            await selectText(page, GROUP, LANG, "fishes.", " The");
            await clickTopBarButton(page, "cut");
        },
    );
    const afterCut = ["P:The kingfisher", TABLE, "P:fishes. otter swims."];
    await expect
        .poll(async () => getTextBoxLayout(page, GROUP, LANG), {
            message:
                "Cut should take out only the word below the table, and leave the table.",
        })
        .toEqual(afterCut);

    // Paste reads the clipboard before it looks at the selection, so a later click says nothing
    // about when it looked. Leaving the page waits for a paste that is under way to finish.
    await step(
        "Select across the table, click the top bar's Paste, then leave the page and come back",
        async () => {
            await copyToClipboard(page, { text: "pasted words" });
            await selectText(page, GROUP, LANG, "The", "fishes.");
            await clickTopBarButton(page, "paste");
            await reloadPageBeingEdited(page);
        },
    );
    expect(await getTextBoxLayout(page, GROUP, LANG)).toEqual(afterCut);

    await step("Paste over a word below the table", async () => {
        await selectText(page, GROUP, LANG, "fishes.", " otter");
        await clickTopBarButton(page, "paste");
    });
    await expect
        .poll(async () => getTextBoxLayout(page, GROUP, LANG), {
            message:
                "Paste below the table should work, or the step before it proves nothing.",
        })
        .toEqual(["P:The kingfisher", TABLE, "P:fishes.pasted words swims."]);
});

test("Dragging a table by its grip moves it in the text, and it stays there", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a page with a table in its text", async () =>
        makePageWithTableInText(page),
    );
    const moved = [
        FIRST_SENTENCE,
        "P:The heron fishes.",
        TABLE,
        "P:The otter swims.",
    ];

    await step("Drag the table to just after the second sentence", async () =>
        dragTableToAfterText(page, GROUP, LANG, "heron fishes."),
    );
    expect(await getTextBoxLayout(page, GROUP, LANG)).toEqual(moved);

    await step("Leave the page and come back", async () =>
        reloadPageBeingEdited(page),
    );
    expect(await getTextBoxLayout(page, GROUP, LANG)).toEqual(moved);
});

test("Ctrl+B in a cell does not format its text, and Ctrl+Z undoes typing in the cell", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await step("Make a page with a table in its text", async () =>
        makePageWithTableInText(page),
    );

    await step("Type in a cell, press Ctrl+B, and type more", async () => {
        await typeInCellKeyByKey(page, 0, 0, LANG, "Bird");
        await pressKeys(page, ["Control+b"]);
        await page.keyboard.type("Owl");
    });
    await expect
        .poll(async () => getCellText(page, 0, 0, LANG))
        .toBe("BirdOwl");
    expect(
        await cellTextIsBold(page, 0, 0, LANG),
        "A cell's text has no formatting, so Ctrl+B must not make it bold.",
    ).toBe(false);

    await step("Press Ctrl+Z in the cell", async () =>
        pressKeys(page, ["Control+z"]),
    );
    await expect
        .poll(async () => getCellText(page, 0, 0, LANG), {
            message: "Ctrl+Z in the cell should undo typing in the cell.",
        })
        .not.toBe("BirdOwl");
    expect(await countTablesInText(page, GROUP, LANG)).toBe(1);
});
