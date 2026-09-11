// A probe, not a feature test. It exists to answer one open question in AUTOMATION-DEBT.md,
// "A title typed on the cover of a new book can fail to reach the collection": on the nightly
// runner a title typed on the cover of a brand-new book is sometimes dropped, and we do not yet
// know whether that is Bloom losing an edit or our own typing not being enough like a person's.
//
// So it makes the same book three times in one run, typing the title a different way each time,
// and reports which ways reached the collection:
//
//   1. insertText          — what helpers/bookMaking.ts typeInGroup does today. It raises `input`
//                            but no keydown/keypress/keyup at all (see the debt entry "Typing in a
//                            text box raises no key events").
//   2. keyPresses          — a real key event per character, which is what a person produces.
//   3. insertText + blur   — insertText, then focus explicitly leaves the box before Add Page, in
//                            case what commits the edit is the box losing the focus rather than
//                            the typing itself.
//
// Read it from a NIGHTLY run: on a developer machine all three pass (12 for 12 while this was
// written), and only the runner reproduces the loss. The failure message names the variants that
// lost the title, so one red row says which mechanism is at fault. Bloom logs what it actually
// received from the browser for each of these saves ("Cover-title investigation:" in
// EditingModel.UpdateBookDomFromBrowserPageContent), so the run's kept Log.txt then says whether
// the text was already gone before Bloom saw it or was lost after.
//
// The collection carries a branding, because every failure so far has been under one.
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { switchTab } from "../helpers/workspace";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";

test.use({
    collectionSpec: {
        name: "cover-title-save",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

interface IVariant {
    /** Used in the title, so a kept collection says which book came from which variant. */
    name: string;
    /** How the title gets into the box, and what happens to the focus afterwards. */
    typeIt: (
        page: import("@playwright/test").Page,
        title: string,
    ) => Promise<void>;
}

const VARIANTS: IVariant[] = [
    {
        name: "insertText",
        typeIt: (page, title) => typeInGroup(page, ".bookTitle", "en", title),
    },
    {
        name: "keyPresses",
        typeIt: (page, title) =>
            typeInGroup(page, ".bookTitle", "en", title, "keyPresses"),
    },
    {
        name: "insertText then blur",
        typeIt: async (page, title) => {
            await typeInGroup(page, ".bookTitle", "en", title);
            // Take the focus out of the box the way leaving it does, without clicking anything
            // else on the page (a stray click could select a different element and change what
            // Add Page then does).
            await titleBox(page).blur();
        },
    },
];

/**
 * The cover's English title box, WITHOUT touching it. Every read here has to leave the page as
 * the variant left it: clicking the box back into focus right before the save would undo the
 * blur variant, and would put a fresh click and focus into all three runs — the very kind of
 * event that could mask, or cause, the loss being investigated.
 */
function titleBox(page: import("@playwright/test").Page) {
    return editablePageFrame(page)
        .locator('.bookTitle .bloom-editable[lang="en"]')
        .first();
}

test("a title typed on a new book's cover reaches the collection, however it was typed", async ({
    page,
}) => {
    test.setTimeout(600000);

    const lost: string[] = [];
    for (const variant of VARIANTS) {
        const title = `Cover Title Probe ${variant.name}`;
        await switchTab(page, "collection");
        await makeBookFromTemplate(page, "Basic Book");
        await variant.typeIt(page, title);

        // What the browser holds right before the save. If this is already wrong, the text never
        // survived in the page at all and Bloom was never given a chance to save it.
        const inTheBoxBeforeSaving = await titleBox(page).innerText();

        // Adding a page is what saves the cover: OnInsertPage wraps the insert in SaveThen, which
        // asks the browser for the current page's content first.
        await addPage(page, "Just Text");

        let reachedTheCollection = true;
        try {
            await findBookFolder(page, title, 20000);
        } catch (error) {
            // findBookFolder throws both when the collection never learned the title and when it
            // could not ask at all (Bloom gone, browser closed). Only the first is the result this
            // probe is here to record; letting the second through as "the title was lost" would
            // point the whole investigation at the wrong thing, so it fails the run instead.
            if (!String(error).includes("has no book called")) throw error;
            reachedTheCollection = false;
        }

        // One line per variant in the run's output, so a nightly log carries the whole result even
        // when the assertion below stops at the first failure. Console.error, because that is what
        // survives at the reporter's default verbosity.
        console.error(
            `[cover-title probe] ${variant.name}: in the box before saving = ` +
                `"${inTheBoxBeforeSaving.trim()}", reached the collection = ${reachedTheCollection}`,
        );
        if (!reachedTheCollection)
            lost.push(
                `${variant.name} (the box held "${inTheBoxBeforeSaving.trim()}" when Add Page saved)`,
            );
    }

    expect(
        lost,
        "A title typed on the cover of a new book did not reach the collection. Which variants " +
            "lost it says where the fault is: only insertText means our typing is the problem and " +
            "typeInGroup should type the way a person does; every variant means Bloom is losing " +
            "the edit. See AUTOMATION-DEBT.md, 'A title typed on the cover of a new book can fail " +
            "to reach the collection'.",
    ).toEqual([]);
});
