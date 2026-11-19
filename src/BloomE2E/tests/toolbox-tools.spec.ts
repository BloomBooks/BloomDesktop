// The Edit tab's toolbox itself, rather than any one tool in it: which tools it offers, turning
// tools on and off under "More...", which tool is expanded, and the book remembering that.
//
// These are characterization tests: they pin how the toolbox behaves today so that the toolbox
// infrastructure rewrite (BL-16608) has to keep it. No manual test card covered this, so these tests
// have a card of their own: Notion test case 830. Turning a tool on is driven through the
// real "More..." check box because that journey is part of what is being pinned; the other tests
// take the fast setup route (enableToolForBook).

import * as fs from "fs";
import * as Path from "path";
import { expect, test } from "../fixtures/bloomTest";
import { bookHtmlPath } from "../helpers/bookHtml";
import { editBook, makeBookFromTemplate } from "../helpers/bookMaking";
import { expandReaderTool } from "../helpers/readerTools";
import {
    clickToolHeader,
    enableToolForBook,
    expectExpandedTool,
    getExpandedTool,
    getShownTools,
    setToolTurnedOn,
    showToolbox,
    waitForExpandedTool,
} from "../helpers/toolbox";

test.use({
    collectionSpec: { name: "toolbox-tools", languages: ["en"] },
});

// These tests were skipped (test.fixme) because they raced the toolbox's late settings
// restore: it applied settings read before the page was ready, so a toolbox just expanded was
// shut again and a tool just expanded closed back to the tool saved in the book. A person who
// clicks that fast simply clicks again; a test clicks at once, so it sometimes lost. The
// restore now imposes a saved fact only if that fact has not changed since it was read.

test("turning a tool on under More... adds it in alphabetical order and expands it [Test Case ID 830]", async ({
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

    // Bloom expands the tool just turned on, after a moment that lets the person see the box tick
    // before "More..." closes (BL-16501).
    await expectExpandedTool(
        page,
        "leveledReader",
        "Turning the Leveled Reader on did not expand it.",
    );
    // Tools are in alphabetical order of their labels, with "More..." always last.
    const shown = await getShownTools(page);
    expect(
        shown.indexOf("leveledReader"),
        `The Leveled Reader should come before Talking Book. The toolbox shows: ${shown.join(", ")}.`,
    ).toBeLessThan(shown.indexOf("talkingBook"));
    expect(shown[shown.length - 1], "More... should be the last tool.").toBe(
        "settings",
    );
});

test("turning a tool off under More... removes it [Test Case ID 830]", async ({
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
        "Turning the Leveled Reader off should leave the other tools alone.",
    ).toEqual(expect.arrayContaining(["talkingBook", "settings"]));
});

test("clicking the header of the expanded tool leaves it expanded [Test Case ID 830]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await showToolbox(page);
    const open = await waitForExpandedTool(page);

    await clickToolHeader(page, open);

    // The toolbox always shows one expanded tool; clicking its header does not close it.
    expect(await getExpandedTool(page)).toBe(open);
});

test("the book remembers which tool was expanded [Test Case ID 830]", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await enableToolForBook(page, bookFolder, "leveledReader");
    await showToolbox(page);
    // sanity check: the tool we will look for is not the one the book opens anyway
    expect(
        await waitForExpandedTool(page),
        "The book should not open the Leveled Reader before it has been expanded once.",
    ).not.toBe("leveledReader");
    await expandReaderTool(page, "leveledReader");
    expect(await getExpandedTool(page)).toBe("leveledReader");

    await editBook(page, bookFolder);
    // The book keeps it in its meta.json, not just in memory, so it survives a restart of Bloom.
    const meta = JSON.parse(
        fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
    ) as { currentTool?: string };
    expect(
        meta.currentTool,
        "The book's meta.json should record the Leveled Reader as its expanded tool.",
    ).toMatch(/^leveledReader/);
    await showToolbox(page);

    await expectExpandedTool(
        page,
        "leveledReader",
        "Coming back to the book should reopen the tool that was expanded when it was left.",
    );
});

test("turning a reader tool on reaches the book file without typing anything [Test Case ID 830]", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await enableToolForBook(page, bookFolder, "leveledReader");
    // The switch records its choice as a class on the book's body, outside every page, so the
    // save that follows sees no change on the page.
    const bodyClasses = () => {
        const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
        const body = /<body[^>]*\bclass="([^"]*)"/.exec(html);
        return (body ? body[1] : "").split(/\s+/);
    };
    // sanity check: the book is not already a leveled reader
    expect(
        bodyClasses(),
        "A new Basic Book should not already be a leveled reader.",
    ).not.toContain("leveled-reader");
    await showToolbox(page);
    // The switch tells Bloom without waiting for an answer; wait for the answer here, as a person
    // is never fast enough to leave the book before it comes.
    const bloomHasTheSwitch = page.waitForResponse((response) =>
        response.url().includes("/toolbox/leveled"),
    );
    await expandReaderTool(page, "leveledReader");
    await bloomHasTheSwitch;

    await editBook(page, bookFolder);

    expect(
        bodyClasses(),
        "Turning on the Leveled Reader's switch should make the saved book a leveled reader, though no page was edited.",
    ).toContain("leveled-reader");
});
