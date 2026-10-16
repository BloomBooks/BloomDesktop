// Which undo runs. The page open in the Edit tab has one undo history, the page history, and the
// top bar's Undo button, Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z all go to it; CKEditor's own undo and the
// reader tools' undo never run. Change Layout mode keeps its own undo, which the button and the keys
// reach while the mode is on. These tests pin that each gesture reaches the right one, by counting
// the undos and redos the page history runs and the commands CKEditor runs (helpers/undo.ts).
//
// docs/undo/MINIMUM.md items 9 and 10 describe the routing.
//
// The tests are serial because each one starts from the book the one before it left behind.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import { pressKey, typeWithKeys } from "../helpers/keys";
import {
    sections,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";
import { makeBasicBookWithReaderTool } from "../helpers/readerTools";
import {
    clickUndoButton,
    expectUndoAndRedoCalls,
    waitForPageHistory,
    watchUndoAndRedo,
} from "../helpers/undo";

test.use({
    collectionSpec: { name: "undo-routing", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The picture for the picture test. A copy of src/BloomTests/ImageProcessing/images/bird.png.
const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// The text box every test types into: the page's one translation group, in English.
const TEXT_BOX = ".bloom-translationGroup";

// The Basic Book page the first three tests share. Set by the first test.
let picturePage: IBookPage;

test.describe("undo reaches the page history, or Change Layout mode's own undo", () => {
    test("the Undo button and Ctrl+Z undo typing, and Ctrl+Y redoes it, all through the page history", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        [picturePage] = await getContentPages(page);
        await goToPage(page, picturePage.id);
        await waitForPageHistory(page);

        // The Undo button.
        const box = await clickInGroup(page, TEXT_BOX, "en");
        await watchUndoAndRedo(page);
        await typeWithKeys(page, "dog");
        await expect(box).toHaveText("dog");
        await clickUndoButton(page);
        await expectUndoAndRedoCalls(
            page,
            { historyUndo: 1, historyRedo: 0, ckeditorCommands: [] },
            "clicking Undo after typing",
        );
        await expect(box).not.toHaveText("dog");

        // Ctrl+Z and Ctrl+Y, pressed in the box. The page history takes them before CKEditor can.
        await clickInGroup(page, TEXT_BOX, "en");
        await typeWithKeys(page, "cat");
        await expect(box).toHaveText("cat");
        await watchUndoAndRedo(page);
        await pressKey(page, "Control+z");
        await expectUndoAndRedoCalls(
            page,
            { historyUndo: 1, historyRedo: 0, ckeditorCommands: [] },
            "pressing Ctrl+Z after typing",
        );
        await expect(box).not.toHaveText("cat");
        await pressKey(page, "Control+y");
        await expectUndoAndRedoCalls(
            page,
            { historyUndo: 1, historyRedo: 1, ckeditorCommands: [] },
            "pressing Ctrl+Y after Ctrl+Z",
        );
        await expect(box).toHaveText("cat");
        await pressKey(page, "Control+z");
        await pressKey(page, "Control+Shift+z");
        await expectUndoAndRedoCalls(
            page,
            { historyUndo: 2, historyRedo: 2, ckeditorCommands: [] },
            "pressing Ctrl+Z and then Ctrl+Shift+Z",
        );
        await expect(box).toHaveText("cat");
    });

    test("in Change Layout mode, Ctrl+Z, Ctrl+Y and the Undo button reach origami's own undo", async ({
        page,
    }) => {
        await goToPage(page, picturePage.id);
        await setChangeLayoutMode(page, true);
        // Two splits, so that a key handled twice would undo or redo both, and show.
        await splitSection(page, "bottom");
        const afterOneSplit = await sections(page).count();
        await splitSection(page, "bottom");
        const afterTwoSplits = await sections(page).count();
        await watchUndoAndRedo(page);

        await pressKey(page, "Control+z");
        await expect(
            sections(page),
            "Ctrl+Z did not undo exactly the last split.",
        ).toHaveCount(afterOneSplit);
        await pressKey(page, "Control+y");
        await expect(
            sections(page),
            "Ctrl+Y did not redo exactly the last split.",
        ).toHaveCount(afterTwoSplits);

        await clickUndoButton(page);
        await expect(
            sections(page),
            "The Undo button did not undo exactly the last split.",
        ).toHaveCount(afterOneSplit);
        await expectUndoAndRedoCalls(
            page,
            { historyUndo: 0, historyRedo: 0, ckeditorCommands: [] },
            "pressing Ctrl+Z and Ctrl+Y, and clicking Undo, in Change Layout mode",
        );
        await setChangeLayoutMode(page, false);
    });

    test("the Undo button undoes a picture change through the page history", async ({
        page,
    }) => {
        await goToPage(page, picturePage.id);
        await waitForPageHistory(page);
        await chooseImageFile(page, IMAGE_FILE);
        await watchUndoAndRedo(page);

        await clickUndoButton(page);
        await expectUndoAndRedoCalls(
            page,
            { historyUndo: 1 },
            "clicking Undo after choosing a picture",
        );
        await expect
            .poll(async () => (await getImagePlacement(page)).fileName, {
                timeout: 30000,
                message: "The Undo button did not put the placeholder back.",
            })
            .toBe("placeHolder.png");
    });

    test("with a reader tool active, Ctrl+Z and Ctrl+Y undo and redo the typing through the page history", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBasicBookWithReaderTool(page, "decodableReader");
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        await waitForPageHistory(page);

        const box = await clickInGroup(page, TEXT_BOX, "en");
        await typeWithKeys(page, "sun");
        await expect(box).toHaveText("sun");
        await watchUndoAndRedo(page);
        await pressKey(page, "Control+z");
        await expect(box, "Ctrl+Z did not undo the typing.").not.toHaveText(
            /sun$/,
        );
        await pressKey(page, "Control+y");
        await expect(box, "Ctrl+Y did not redo the typing.").toHaveText("sun");
        await expectUndoAndRedoCalls(
            page,
            { historyUndo: 1, historyRedo: 1, ckeditorCommands: [] },
            "pressing Ctrl+Z and then Ctrl+Y with the Decodable Reader tool active",
        );
    });
});
