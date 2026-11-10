// The booklet print instructions (BL-16179). Printing a booklet needs particular settings in the
// print dialog (landscape, flip on short edge, the paper size, actual size), and Bloom lists them
// for the book's page size. They used to be a banner at the foot of the PDF & Print screen, which
// the browser's print dialog covered completely, so nobody could read them while choosing the
// settings. Bloom now shows them in a small window of its own, above the print dialog.
//
// What stays manual: where the window sits and that it can be dragged and closed with its X. A test
// cannot see the print dialog in a screenshot, so whether the window covers any of the dialog's
// controls is checked by eye.
//
// The tests share one book, so they run in order.

import { expect, test } from "../fixtures/bloomTest";
import {
    makeBookFromTemplate,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    cancelPrintDialog,
    choosePdfMode,
    clickPrint,
    expectAllPrintInstructionsShowing,
    getPrintInstructions,
    isPrintDialogOpen,
    isPrintInstructionsWindowOpen,
} from "../helpers/pdfPrint";
import { setPageSize } from "../helpers/pageSize";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "booklet-print-instructions", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

test.describe("the booklet print instructions", () => {
    test("stay readable above the print dialog, and close when the user leaves PDF & Print [Test Case ID 856]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(240000);
        // A Basic Book is A5 portrait, which prints as a booklet on landscape A4.
        await makeBookFromTemplate(page, "Basic Book");
        await choosePdfMode(page, "Booklet Insides");
        expect(
            await isPrintInstructionsWindowOpen(page),
            "The print instructions window was open before Print was clicked.",
        ).toBe(false);

        await clickPrint(page, bloomApp.bloomPid);

        // The instructions are readable while the print dialog is open.
        expect(await getPrintInstructions(page)).toEqual([
            "Here are the settings you need for printing this booklet:",
            "1. Layout: Landscape",
            '2. Print on both sides: "Flip on short edge"',
            '3. More settings > Paper size: "A4"',
            '4. More settings > Scale: "Actual size"',
        ]);
        await expectAllPrintInstructionsShowing(page);
        expect(isPrintDialogOpen(bloomApp.bloomPid)).toBe(true);

        // Nothing tells Bloom when the print dialog closes, so the window stays after it does...
        await cancelPrintDialog(bloomApp.bloomPid);
        expect(
            await isPrintInstructionsWindowOpen(page),
            "The print instructions window closed with the print dialog, which Bloom cannot detect.",
        ).toBe(true);

        // ...and closes when the user leaves the PDF & Print screen.
        await switchTab(page, "collection");
        await expect
            .poll(() => isPrintInstructionsWindowOpen(page), {
                message:
                    "The print instructions window stayed open after the user left the Publish tab.",
            })
            .toBe(false);
    });

    test("do not appear when printing a simple PDF [Test Case ID 856]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(180000);
        await choosePdfMode(page, "Simple");

        await clickPrint(page, bloomApp.bloomPid);

        // Bloom opens the instructions window, when it does, before it asks for the print dialog,
        // so with the dialog showing the window would already be there.
        expect(
            await isPrintInstructionsWindowOpen(page),
            "A simple PDF needs no booklet settings, but the print instructions window opened.",
        ).toBe(false);
        await cancelPrintDialog(bloomApp.bloomPid);
    });

    test("show all their text when the page size adds a note [Test Case ID 856]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(240000);
        // A6 prints four pages to a sheet, so the settings come with the longest English note.
        // Leaving Publish for this also gives the next PDF screen a fresh start.
        await switchTab(page, "edit");
        await waitForEditablePage(page);
        await setPageSize(page, "A6Portrait");
        await choosePdfMode(page, "Booklet Insides");

        await clickPrint(page, bloomApp.bloomPid);

        expect(await getPrintInstructions(page)).toEqual([
            "Here are the settings you need for printing this booklet:",
            "1. Layout: Portrait",
            '2. Print on both sides: "Flip on long edge"',
            '3. More settings > Paper size: "A4"',
            '4. More settings > Scale: "Actual size"',
            "This will put four pages on each sheet of paper. Cut the stack, then fold to get two booklets.",
        ]);
        await expectAllPrintInstructionsShowing(page);
        await cancelPrintDialog(bloomApp.bloomPid);
    });
});
