// Undo and redo of typing with the Decodable and Leveled Reader tools. The reader tools mark up the
// text by painting highlights over it; they do not rewrite a text box. So nothing they do after
// the typing (opening, changing the stage, marking up again when the focus leaves the box) makes
// an undo step, joins one or ends the page history: Ctrl+Z undoes the typing, Ctrl+Y redoes it,
// and the highlights follow the text that each puts back. Changing the level is the exception:
// C# reloads the page, which ends the history (see that test).
//
// Each test checks the box's text and its highlights together (getReaderMarkup), and that the
// page history ran exactly one undo for Ctrl+Z and one redo for Ctrl+Y.
//
// The tests are serial because they share the one book and page that the first one makes. Each
// empties the text box first.

import type { Locator } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { checkUndoRoundTripAfterEachTest } from "../helpers/undoRoundTrip";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { deleteAllTextIn, pressKey, typeWithKeys } from "../helpers/keys";
import {
    useKnownReaderLevels,
    useKnownReaderStages,
} from "../helpers/readerSetup";
import {
    clickReaderToolStepperText,
    expectReaderMarkup,
    expandReaderTool,
    setReaderPhase,
    undoAndRedoWithKeysAndExpectReaderMarkup,
    waitForReaderToolToRestore,
    type IReaderMarkup,
} from "../helpers/readerTools";
import {
    clickToolHeader,
    enableToolForBook,
    waitForExpandedTool,
} from "../helpers/toolbox";
import { canUndo, waitForPageHistory } from "../helpers/undo";

test.use({
    collectionSpec: { name: "undo-reader-tools", languages: ["en"] },
});

checkUndoRoundTripAfterEachTest(test);

test.describe.configure({ mode: "serial" });

// The text box every test types into: the Just Text page's one translation group, in English.
const TEXT_BOX = ".bloom-translationGroup";

// What the box shows once a test has emptied it.
const EMPTY_BOX: IReaderMarkup = { text: "", highlights: {} };

// At stage 1 (letters a, e and r; sight words "the", "of", "and" and "to", from
// useKnownReaderStages), "the" is a sight word, "era" is decodable and "dog" is not.
const DOG_TEXT = "the era dog";
const DOG_AT_STAGE_1: IReaderMarkup = {
    text: DOG_TEXT,
    highlights: { sightWord: ["the"], wordNotDecodable: ["dog"] },
};

// "air" needs the i of stage 2.
const AIR_TEXT = "the air";
const AIR_AT_STAGE_1: IReaderMarkup = {
    text: AIR_TEXT,
    highlights: { sightWord: ["the"], wordNotDecodable: ["air"] },
};
const AIR_AT_STAGE_2: IReaderMarkup = {
    text: AIR_TEXT,
    highlights: { sightWord: ["the"] },
};

// Level 1 allows three words in a sentence and level 2 allows eight (useKnownReaderLevels).
const SHORT_SENTENCE: IReaderMarkup = { text: "Dogs ran far", highlights: {} };
const LONG_SENTENCE_TEXT = "Dogs ran far away.";
const LONG_SENTENCE_AT_LEVEL_1: IReaderMarkup = {
    text: LONG_SENTENCE_TEXT,
    highlights: { sentenceTooLong: [LONG_SENTENCE_TEXT] },
};
const LONG_SENTENCE_AT_LEVEL_2: IReaderMarkup = {
    text: LONG_SENTENCE_TEXT,
    highlights: {},
};

// The text box of the page the first test makes.
let box: Locator;

test.describe("undo and redo of typing with a reader tool", () => {
    test("typing, then opening the Decodable Reader: Ctrl+Z undoes the typing and Ctrl+Y redoes it", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await useKnownReaderStages(page);
        await useKnownReaderLevels(page);
        await enableToolForBook(page, bookFolder, "decodableReader");
        await enableToolForBook(page, bookFolder, "leveledReader");
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        // The toolbox expands the book's tool once the page has loaded; let it, before expanding ours.
        await waitForExpandedTool(page);
        for (const tool of ["leveledReader", "decodableReader"] as const) {
            await expandReaderTool(page, tool);
            await waitForReaderToolToRestore(page, tool, bookFolder);
            await setReaderPhase(page, tool, 1);
        }
        await waitForPageHistory(page);

        // No reader tool marks up the typing: "More..." is the tool open.
        await clickToolHeader(page, "settings");
        box = await clickInGroup(page, TEXT_BOX, "en");
        await deleteAllTextIn(box, "the text box");
        await typeWithKeys(page, DOG_TEXT);
        await expectReaderMarkup(
            box,
            { text: DOG_TEXT, highlights: {} },
            "typing with no reader tool open",
        );

        await expandReaderTool(page, "decodableReader");
        await expectReaderMarkup(
            box,
            DOG_AT_STAGE_1,
            "opening the Decodable Reader tool",
        );
        await undoAndRedoWithKeysAndExpectReaderMarkup(
            page,
            box,
            EMPTY_BOX,
            DOG_AT_STAGE_1,
            `typing "${DOG_TEXT}" and then opening the Decodable Reader tool`,
        );
    });

    test("with the Leveled Reader open, a sentence made too long is marked, and Ctrl+Z and Ctrl+Y take the mark with the typing", async ({
        page,
    }) => {
        await setReaderPhase(page, "leveledReader", 1);
        await deleteAllTextIn(box, "the text box");
        await typeWithKeys(page, SHORT_SENTENCE.text);
        await expectReaderMarkup(
            box,
            SHORT_SENTENCE,
            "typing a sentence of three words at level 1",
        );
        // End leaves the caret at the end of the text, where it is, and ends the typing step, so
        // that the next typing is an undo step of its own.
        await pressKey(page, "End");
        await typeWithKeys(page, " away.");
        await expectReaderMarkup(
            box,
            LONG_SENTENCE_AT_LEVEL_1,
            "making the sentence four words long at level 1",
        );

        await undoAndRedoWithKeysAndExpectReaderMarkup(
            page,
            box,
            SHORT_SENTENCE,
            LONG_SENTENCE_AT_LEVEL_1,
            'typing " away." to make the sentence too long',
        );
    });

    // Changing the level is the one change here that ends the page history: C# writes the level
    // into the book's body and then saves and reloads the page (EditingModel.EnsureLevelAttrCorrect),
    // and a page load starts a new history.
    test("typing, then changing the Leveled Reader's level: the page reloads with the typing kept, and there is nothing to undo", async ({
        page,
    }) => {
        await setReaderPhase(page, "leveledReader", 1);
        await deleteAllTextIn(box, "the text box");
        await typeWithKeys(page, LONG_SENTENCE_TEXT);
        await expectReaderMarkup(
            box,
            LONG_SENTENCE_AT_LEVEL_1,
            "typing a sentence of four words at level 1",
        );
        expect(
            await canUndo(page),
            "Sanity check: the typing should be undoable before the level changes.",
        ).toBe(true);

        await setReaderPhase(page, "leveledReader", 2);
        await expect
            .poll(() => canUndo(page), {
                timeout: 30000,
                message:
                    "After the level changed, Bloom still offered an undo; the page should have reloaded with a new history.",
            })
            .toBe(false);
        await waitForPageHistory(page);
        await expectReaderMarkup(
            box,
            LONG_SENTENCE_AT_LEVEL_2,
            "changing to level 2, which allows eight words",
        );
    });

    test("typing, then changing the Decodable Reader's stage: the typing can still be undone and redone", async ({
        page,
    }) => {
        await setReaderPhase(page, "decodableReader", 1);
        await deleteAllTextIn(box, "the text box");
        await typeWithKeys(page, AIR_TEXT);
        await expectReaderMarkup(box, AIR_AT_STAGE_1, "typing at stage 1");

        await setReaderPhase(page, "decodableReader", 2);
        await expectReaderMarkup(
            box,
            AIR_AT_STAGE_2,
            "changing to stage 2, which adds the letter i",
        );
        await undoAndRedoWithKeysAndExpectReaderMarkup(
            page,
            box,
            EMPTY_BOX,
            AIR_AT_STAGE_2,
            `typing "${AIR_TEXT}" and then changing the stage`,
        );
    });

    test("typing, then clicking in the toolbox: the focus leaves the box and the typing can still be undone and redone", async ({
        page,
    }) => {
        await setReaderPhase(page, "decodableReader", 1);
        await deleteAllTextIn(box, "the text box");
        await typeWithKeys(page, DOG_TEXT);
        await expectReaderMarkup(box, DOG_AT_STAGE_1, "typing at stage 1");

        await clickReaderToolStepperText(page, "decodableReader");
        await expectReaderMarkup(
            box,
            DOG_AT_STAGE_1,
            "clicking in the toolbox, which takes the focus from the box",
        );
        await undoAndRedoWithKeysAndExpectReaderMarkup(
            page,
            box,
            EMPTY_BOX,
            DOG_AT_STAGE_1,
            `typing "${DOG_TEXT}" and then clicking in the toolbox`,
        );
    });
});
