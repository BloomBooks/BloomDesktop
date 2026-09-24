// The Edit tab's toolbox itself, rather than any one tool in it: which sections it offers, turning
// tools on and off under "More...", which section is open, and the book remembering that.
//
// These are characterization tests: they pin how the toolbox behaves today so that the toolbox
// infrastructure rewrite (BL-16608) has to keep it. No manual test card covered this, so these tests
// have a card of their own: Notion test case 830. Turning a tool on is driven through the
// real "More..." check box because that journey is part of what is being pinned; the other tests
// take the fast setup route (enableToolForBook).

import { expect, test } from "../fixtures/bloomTest";
import { editBook, makeBookFromTemplate } from "../helpers/bookMaking";
import { openReaderTool } from "../helpers/readerTools";
import {
    clickToolHeader,
    enableToolForBook,
    expectOpenTool,
    getOpenTool,
    getShownTools,
    setToolTurnedOn,
    showToolbox,
    waitForOpenTool,
} from "../helpers/toolbox";

test.use({
    collectionSpec: { name: "toolbox-sections", languages: ["en"] },
});

test("turning a tool on under More... adds its section in order and opens it [Test Case ID 830]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await showToolbox(page);
    // sanity check: a new Basic Book does not already have the Leveled Reader
    expect(
        await getShownTools(page),
        "A new Basic Book should not start with the Leveled Reader tool.",
    ).not.toContain("leveledReader");

    await setToolTurnedOn(page, "leveledReader", true);

    // Bloom opens the tool just turned on, after a moment that lets the person see the box tick
    // before "More..." closes (BL-16501).
    await expectOpenTool(
        page,
        "leveledReader",
        "Turning the Leveled Reader on did not open it.",
    );
    // Sections are in alphabetical order of their labels, with "More..." always last.
    const shown = await getShownTools(page);
    expect(
        shown.indexOf("leveledReader"),
        `The Leveled Reader should come before Talking Book. The toolbox shows: ${shown.join(", ")}.`,
    ).toBeLessThan(shown.indexOf("talkingBook"));
    expect(shown[shown.length - 1], "More... should be the last section.").toBe(
        "settings",
    );
});

test("turning a tool off under More... removes its section [Test Case ID 830]", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await enableToolForBook(page, bookFolder, "leveledReader");
    // sanity check: the tool is there to be turned off
    expect(await getShownTools(page)).toContain("leveledReader");

    await setToolTurnedOn(page, "leveledReader", false);

    const shown = await getShownTools(page);
    expect(
        shown,
        "Turning the Leveled Reader off should leave the other sections alone.",
    ).toEqual(expect.arrayContaining(["talkingBook", "settings"]));
});

test("clicking the header of the open tool leaves it open [Test Case ID 830]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await showToolbox(page);
    const open = await waitForOpenTool(page);

    await clickToolHeader(page, open);

    // The toolbox always shows one open tool; clicking its header does not close it.
    expect(await getOpenTool(page)).toBe(open);
});

test("the book remembers which tool was open [Test Case ID 830]", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await enableToolForBook(page, bookFolder, "leveledReader");
    await showToolbox(page);
    // sanity check: the tool we will look for is not the one the book opens anyway
    expect(
        await waitForOpenTool(page),
        "The book should not open the Leveled Reader before it has been opened once.",
    ).not.toBe("leveledReader");
    await openReaderTool(page, "leveledReader");
    expect(await getOpenTool(page)).toBe("leveledReader");

    await editBook(page, bookFolder);
    await showToolbox(page);

    await expectOpenTool(
        page,
        "leveledReader",
        "Coming back to the book should reopen the tool that was open when it was left.",
    );
});
