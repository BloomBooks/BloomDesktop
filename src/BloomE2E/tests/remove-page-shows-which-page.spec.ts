// Before Bloom asks "Really Remove Page?", it brings the page it will remove into view in the Edit
// tab and draws a red X across it, so the user can see which page they are about to lose. Cancel
// takes the X away and removes nothing.
//
// Automates "Remove Page Shows Which Page It Will Remove" (Test Case ID 836).

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    cancelRemovePageDialog,
    getRemovalMark,
    openRemovePageDialog,
} from "../helpers/pageList";
import {
    choosePageView,
    getPagePlacement,
    isWhollyInView,
    scrollPageOutOfView,
} from "../helpers/pageView";

test.use({
    collectionSpec: { name: "remove-page-mark", languages: ["en"] },
});

// Each line of the X stops this far short of the page's corners.
const kInsetFromCorners = 30;

test("the page to be removed scrolls into view with a red X across it, and Cancel takes the X away [Test Case ID 836]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    // Enough pages that, with all of them showing, the first content page can be scrolled out of
    // view: four rows of spreads at two to a row, where the view is about one and a half pages tall.
    await addPage(page, "Just Text", 10);
    const pageCount = (await getPages(page)).length;
    const toRemove = (await getContentPages(page))[0];
    await goToPage(page, toRemove.id);
    await choosePageView(page, "all");
    await scrollPageOutOfView(page, toRemove.id);
    expect(await getRemovalMark(page)).toBeUndefined();

    // THE ACTION UNDER TEST: the Remove button under the page list.
    await openRemovePageDialog(page);

    const placement = await getPagePlacement(page, toRemove.id);
    expect(
        isWhollyInView(placement),
        `The page to be removed should have scrolled into view, but it runs from ` +
            `(${placement.page.left}, ${placement.page.top}) to ` +
            `(${placement.page.right}, ${placement.page.bottom}), and the view from ` +
            `(${placement.visible.left}, ${placement.visible.top}) to ` +
            `(${placement.visible.right}, ${placement.visible.bottom}).`,
    ).toBe(true);
    const mark = await getRemovalMark(page);
    expect(mark, "There should be a red X across the page").toBeDefined();
    expect(mark!.left).toBeCloseTo(placement.page.left, 0);
    expect(mark!.top).toBeCloseTo(placement.page.top, 0);
    expect(mark!.width).toBeCloseTo(
        placement.page.right - placement.page.left,
        0,
    );
    expect(mark!.height).toBeCloseTo(
        placement.page.bottom - placement.page.top,
        0,
    );
    const near = kInsetFromCorners;
    const farX = mark!.width - kInsetFromCorners;
    const farY = mark!.height - kInsetFromCorners;
    expect(mark!.lines.length).toBe(2);
    expect(mark!.lines[0][0]).toBeCloseTo(near, 0);
    expect(mark!.lines[0][1]).toBeCloseTo(near, 0);
    expect(mark!.lines[0][2]).toBeCloseTo(farX, 0);
    expect(mark!.lines[0][3]).toBeCloseTo(farY, 0);
    expect(mark!.lines[1][0]).toBeCloseTo(farX, 0);
    expect(mark!.lines[1][1]).toBeCloseTo(near, 0);
    expect(mark!.lines[1][2]).toBeCloseTo(near, 0);
    expect(mark!.lines[1][3]).toBeCloseTo(farY, 0);
    expect(mark!.lineColors).toEqual(["#ff0000", "#ff0000"]);
    expect(mark!.lineWidths).toEqual([4, 4]);

    await cancelRemovePageDialog(page);

    expect(await getRemovalMark(page)).toBeUndefined();
    expect((await getPages(page)).length).toBe(pageCount);
    expect(await getShownPageId(page)).toBe(toRemove.id);
});
