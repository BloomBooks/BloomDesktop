// Which undo runs. Bloom has several undo mechanisms (Change Layout mode's, the reader tools', the
// picture undo, and CKEditor's), and the top bar's Undo button picks between them through Bloom's
// one undo stack. These tests pin that each gesture reaches the mechanism that owns the change, and
// only that one, by counting the calls each mechanism receives (helpers/undo.ts). They also pin that
// the stack's own Ctrl+Y binding stays out of the way of the Ctrl+Y handlers that already exist.
//
// This is the "nothing changed" claim of stage 1 of retiring CKEditor (BL-16900, under BL-6681).
// Later stages move these mechanisms onto the shared stack one by one, and these tests are what
// shows each one moved and nothing else did.
//
// Two known problems with the reader tools' keyboard undo are deliberately not asserted here, because
// later stages of the same project remove them: with a reader tool active, Ctrl+Z also runs
// CKEditor's undo, and the reader tools' undo restores a stale CKEditor bookmark span.
// docs/retire-ckeditor/PROGRESS.md records both.
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
import {
    chooseImageFile,
    getImagePlacement,
    selectImage,
} from "../helpers/images";
import { pressKey, typeWithKeys } from "../helpers/keys";
import {
    sections,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";
import { makeBasicBookWithReaderTool } from "../helpers/readerTools";
import {
    clickUndoButton,
    expectUndoMechanismCalls,
    waitForUndoAvailableFrom,
    watchUndoMechanisms,
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

// What every mechanism other than the one named should have received: nothing.
const NOTHING_REACHED = {
    changeLayout: 0,
    readerTools: 0,
    picture: 0,
    ckeditor: 0,
};

// The Basic Book page the first three tests share. Set by the first test.
let picturePage: IBookPage;

test.describe("undo reaches the mechanism that owns the change", () => {
    test("the Undo button undoes typing through CKEditor, and Ctrl+Z and Ctrl+Y stay CKEditor's", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        [picturePage] = await getContentPages(page);
        await goToPage(page, picturePage.id);

        // The Undo button.
        const box = await clickInGroup(page, TEXT_BOX, "en");
        await watchUndoMechanisms(page);
        await typeWithKeys(page, "dog");
        await expect(box).toHaveText("dog");
        await waitForUndoAvailableFrom(page, "ckeditor");
        await clickUndoButton(page);
        await expectUndoMechanismCalls(
            page,
            {
                ...NOTHING_REACHED,
                ckeditor: 1,
                // An undo that rewrites a text box repaints the reader tools' highlighting (BL-16558).
                readerMarkupRepaint: 1,
                ckeditorCommands: [],
            },
            "clicking Undo after typing",
        );
        await expect(box).not.toHaveText("dog");

        // Ctrl+Z and Ctrl+Y, pressed in the box, are CKEditor's own commands, and the one stack's
        // Ctrl+Y binding stands aside for them.
        await clickInGroup(page, TEXT_BOX, "en");
        await typeWithKeys(page, "cat");
        await expect(box).toHaveText("cat");
        await watchUndoMechanisms(page);
        await pressKey(page, "Control+z");
        await expectUndoMechanismCalls(
            page,
            { ...NOTHING_REACHED, ckeditorCommands: ["undo"] },
            "pressing Ctrl+Z after typing",
        );
        await pressKey(page, "Control+y");
        await expectUndoMechanismCalls(
            page,
            {
                ...NOTHING_REACHED,
                stackRedo: 0,
                ckeditorCommands: ["undo", "redo"],
            },
            "pressing Ctrl+Y after Ctrl+Z",
        );
        await expect(box).toHaveText("cat");
    });

    test("in Change Layout mode, Ctrl+Z and Ctrl+Y stay origami's, and the Undo button reaches it", async ({
        page,
    }) => {
        await goToPage(page, picturePage.id);
        await setChangeLayoutMode(page, true);
        // Two splits, so that a key handled twice would undo or redo both, and show.
        await splitSection(page, "bottom");
        const afterOneSplit = await sections(page).count();
        await splitSection(page, "bottom");
        const afterTwoSplits = await sections(page).count();
        await waitForUndoAvailableFrom(page, "changeLayout");
        await watchUndoMechanisms(page);

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
        await expectUndoMechanismCalls(
            page,
            { ...NOTHING_REACHED, stackRedo: 0, ckeditorCommands: [] },
            "pressing Ctrl+Z and then Ctrl+Y in Change Layout mode",
        );

        await clickUndoButton(page);
        await expect(
            sections(page),
            "The Undo button did not undo exactly the last split.",
        ).toHaveCount(afterOneSplit);
        await expectUndoMechanismCalls(
            page,
            { ...NOTHING_REACHED, changeLayout: 1 },
            "clicking Undo in Change Layout mode",
        );
        await setChangeLayoutMode(page, false);
    });

    test("the Undo button undoes a picture change through the picture undo", async ({
        page,
    }) => {
        await goToPage(page, picturePage.id);
        await chooseImageFile(page, IMAGE_FILE);
        // The picture undo answers only while its picture is selected.
        await selectImage(page);
        await waitForUndoAvailableFrom(page, "picture");
        await watchUndoMechanisms(page);

        await clickUndoButton(page);
        await expectUndoMechanismCalls(
            page,
            { ...NOTHING_REACHED, picture: 1 },
            "clicking Undo after choosing a picture",
        );
        await expect
            .poll(async () => (await getImagePlacement(page)).fileName, {
                timeout: 30000,
                message: "The Undo button did not put the placeholder back.",
            })
            .toBe("placeHolder.png");
    });

    test("with a reader tool active, the Undo button reaches the reader tools' undo, and Ctrl+Y stays theirs", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBasicBookWithReaderTool(page, "decodableReader");
        await addPage(page, "Just Text");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);

        // The Undo button.
        const box = await clickInGroup(page, TEXT_BOX, "en");
        await watchUndoMechanisms(page);
        await typeWithKeys(page, "sun");
        await expect(box).toHaveText("sun");
        await waitForUndoAvailableFrom(page, "readerTools");
        await clickUndoButton(page);
        await expectUndoMechanismCalls(
            page,
            {
                ...NOTHING_REACHED,
                readerTools: 1,
                readerMarkupRepaint: 1,
                ckeditorCommands: [],
            },
            "clicking Undo after typing with the Decodable Reader tool active",
        );

        // Ctrl+Y. The reader tools claim it, so the one stack's binding must not act as well.
        await clickInGroup(page, TEXT_BOX, "en");
        await typeWithKeys(page, "pot");
        await waitForUndoAvailableFrom(page, "readerTools");
        await watchUndoMechanisms(page);
        await pressKey(page, "Control+z");
        await expect(box, "Ctrl+Z did not undo the typing.").not.toHaveText(
            /pot$/,
        );
        await pressKey(page, "Control+y");
        await expectUndoMechanismCalls(
            page,
            { stackRedo: 0 },
            "pressing Ctrl+Y after Ctrl+Z with the Decodable Reader tool active",
        );
    });
});
