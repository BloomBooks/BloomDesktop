// The Play tab of a game page in the Edit tab (BL-13502). Play shows the page the way a reader of
// the published book gets it, so an author can try the game out: what the author does there must
// not reach the book, and nothing can be edited until they go back to Start.

import { expect, test } from "../fixtures/bloomTest";
import {
    isBookSettingsDialogShowing,
    cancelBookSettings,
    pressBookSettingsButton,
} from "../helpers/bookSettings";
import { goToPage } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    clickGameTab,
    dragItemOntoItsTarget,
    expectDraggableItem,
    getDraggablePositions,
    goToGamePage,
    makeBookWithGamePage,
    pressKeyOnDraggableItem,
    readSavedDraggablePositions,
    selectFirstDraggableItem,
} from "../helpers/games";
import {
    enableToolForBook,
    expectExpandedTool,
    getExpandedTool,
    isToolboxShowing,
    pressToolboxToggle,
    pressToolHeader,
} from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "game-play-tab",
        languages: ["en"],
        // Games need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test("dragging items in Play, then pressing Play again, does not move them in the saved book [Test Case ID 858]", async ({
    page,
}) => {
    const book = await makeBookWithGamePage(page, "Drag Images to Targets");
    const authored = await getDraggablePositions(page);
    expect(
        Object.keys(authored).length,
        "The game page should have draggable items to drag",
    ).toBeGreaterThan(0);

    await clickGameTab(page, "Play");
    const draggedId = await dragItemOntoItsTarget(page);
    expect(
        (await getDraggablePositions(page))[draggedId],
        "Sanity check: dragging the item should have moved it on the page",
    ).not.toEqual(authored[draggedId]);
    // Pressing Play while in Play used to take the dragged positions as the ones to put back
    // afterwards, so the author's layout was lost when the page was saved.
    await clickGameTab(page, "Play");

    // Leaving the page saves it: Bloom writes the book before it answers the jump to the other
    // page (EditingModel.JumpToPage), so the file read next is the one this visit saved.
    await goToPage(page, book.otherPageId);

    expect(
        await readSavedDraggablePositions(
            page,
            book.bookFolder,
            book.gamePageId,
        ),
        "The saved game page should have its items where the author put them",
    ).toEqual(authored);
});

test("Play disables the toolbox, the top bar's editing controls and editing keys until Start [Test Case ID 859]", async ({
    page,
}) => {
    const book = await makeBookWithGamePage(page, "Drag Images to Targets");
    await enableToolForBook(page, book.bookFolder, "talkingBook");
    await goToGamePage(page, book.gamePageId);
    const toolBefore = await getExpandedTool(page);
    expect(
        toolBefore,
        "Sanity check: some tool other than Talking Book should be expanded to begin with",
    ).not.toBe("talkingBook");
    // An item selected before Play, which Delete removes in Start. In Play, more than one thing
    // stops it (the page's key blocker, and canvas editing being suspended), so this checks what
    // the author sees rather than any one of them; playModeEditBlocker.spec.ts tests the blocker.
    const itemId = await selectFirstDraggableItem(page);

    await clickGameTab(page, "Play");
    // pressBookSettingsButton returns once a click that reached the button would have put the
    // dialog in the page; the same check in Start below shows that it does.
    await pressBookSettingsButton(page);
    expect(
        await isBookSettingsDialogShowing(page),
        "In Play, the Book and Page Settings button should not open its dialog",
    ).toBe(false);
    await pressToolHeader(page, "talkingBook");
    expect(
        await getExpandedTool(page),
        "In Play, clicking a tool's header should not expand it",
    ).toBe(toolBefore);
    // Showing a hidden toolbox would restart its tool, which might show image descriptions.
    await pressToolboxToggle(page);
    expect(
        await isToolboxShowing(page),
        "In Play, the toolbox toggle should not hide the toolbox",
    ).toBe(true);
    await pressKeyOnDraggableItem(page, itemId, "Delete");

    await clickGameTab(page, "Start");
    await expectDraggableItem(
        page,
        itemId,
        true,
        "Delete pressed in Play should not have removed the selected item",
    );

    // The same three in Start, where they work. This also shows the presses above reached
    // controls Bloom would have acted on.
    // Checked at once, with no waiting, so that the same check in Play had the same time to see a
    // dialog the click wrongly opened.
    await pressBookSettingsButton(page);
    expect(
        await isBookSettingsDialogShowing(page),
        "In Start, the Book and Page Settings button should have opened its dialog by the time it returns",
    ).toBe(true);
    await cancelBookSettings(page);
    await pressToolHeader(page, "talkingBook");
    await expectExpandedTool(
        page,
        "talkingBook",
        "In Start, clicking the Talking Book header should expand it",
    );
    await selectFirstDraggableItem(page);
    await pressKeyOnDraggableItem(page, itemId, "Delete");
    await expectDraggableItem(
        page,
        itemId,
        false,
        "In Start, Delete should remove the selected item",
    );
    await pressToolboxToggle(page);
    expect(
        await isToolboxShowing(page),
        "In Start, the toolbox toggle should have hidden the toolbox by the time it returns",
    ).toBe(false);
});
