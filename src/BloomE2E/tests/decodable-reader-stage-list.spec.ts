// Removing a stage and dragging one to a new position: the two ways the stage list itself can be
// edited, as opposed to editing what a stage contains.
//
// Both go through the dialog's clone-change-publish helper, and neither had any test. Neither
// test saves, deliberately: what is being checked is that the list the user is looking at comes
// out right, and keeping both in one file needs the file to save at most once (see
// AUTOMATION-DEBT.md, "Saving the reader settings costs the next test in the file its shell
// document"). What reaches disk is covered by the other specs. (BL-16607)

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    cancelReaderSetup,
    dragStage,
    enableDecodableReaderTool,
    getStageRowTexts,
    openDecodableStagesSetup,
    removeSelectedStage,
    selectStage,
    useKnownReaderStages,
} from "../helpers/readerSetup";

test.use({ collectionSpec: { name: "reader-stage-list", languages: ["en"] } });

test("builds a book with the Decodable Reader tool turned on, and known stages", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);
    await useKnownReaderStages(page);
});

test("removing the selected stage takes that stage out of the list", async ({
    page,
}) => {
    await openDecodableStagesSetup(page);

    const before = await getStageRowTexts(page);
    // sanity check: there are stages to remove one from
    expect(before.length).toBeGreaterThan(2);

    // Pick one in the middle, so a wrong index shows up as the wrong row going.
    await selectStage(page, 1);
    const removed = before[1];
    expect(await removeSelectedStage(page)).toBe(before.length - 1);

    const after = await getStageRowTexts(page);
    expect(after).not.toContain(removed);
    // The others survive, in their original order.
    expect(after).toEqual([before[0], ...before.slice(2)]);

    await cancelReaderSetup(page);
});

test("dragging a stage down the list moves it", async ({ page }) => {
    await openDecodableStagesSetup(page);

    const before = await getStageRowTexts(page);
    expect(before.length).toBeGreaterThan(2);

    await dragStage(page, 0, 2);

    const after = await getStageRowTexts(page);
    // Same stages, none lost or duplicated...
    expect(after.length).toBe(before.length);
    expect([...after].sort()).toEqual([...before].sort());
    // ...but the one that was first is no longer first.
    expect(after[0]).not.toBe(before[0]);

    await cancelReaderSetup(page);
});
