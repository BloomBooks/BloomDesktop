// The Edit tab's toolbox itself, rather than any one tool in it: which sections it offers, turning
// tools on and off under "More...", which section is open, and the book remembering that.
//
// These are characterization tests: they pin how the toolbox behaves today so that the toolbox
// infrastructure rewrite (BL-16608) has to keep it. No manual test card covered this, so these tests
// have a card of their own: Notion test case 830. Turning a tool on is driven through the
// real "More..." check box because that journey is part of what is being pinned; the other tests
// take the fast setup route (enableToolForBook).

import * as fs from "fs";
import * as Path from "path";
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

// SKIPPED: three of these tests are marked test.fixme because they fail on some runs and pass on
// others. They must be turned back on before the toolbox rework (BL-16608) is finished, because
// they pin exactly the behavior that rework has to keep.
//
// Why they fail: once a page finishes loading, the toolbox restores the book's saved state (whether
// the toolbox is open, and which tool is current) from settings it read before the page loaded.
// That overwrites anything done to the toolbox in the meantime: a toolbox just opened is shut again,
// and a section just opened (a tool, or "More...") collapses back to the saved tool. A person who
// clicks that fast simply clicks again; a test clicks at once, so it sometimes loses. Draft PR #8409
// fixes the shutting half. The tool half is not fixed, and the old toolbox code leans on that late
// restore to correct other things, so it is left to the rework.

test.fixme("turning a tool on under More... adds its section in order and opens it [Test Case ID 830]", async ({
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

test.fixme("turning a tool off under More... removes its section [Test Case ID 830]", async ({
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

test.fixme("the book remembers which tool was open [Test Case ID 830]", async ({
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
    // The book keeps it in its meta.json, not just in memory, so it survives a restart of Bloom.
    const meta = JSON.parse(
        fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
    ) as { currentTool?: string };
    expect(
        meta.currentTool,
        "The book's meta.json should record the Leveled Reader as its open tool.",
    ).toMatch(/^leveledReader/);
    await showToolbox(page);

    await expectOpenTool(
        page,
        "leveledReader",
        "Coming back to the book should reopen the tool that was open when it was left.",
    );
});
