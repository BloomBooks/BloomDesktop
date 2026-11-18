import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

// Tests for the wait cursor over the page frame while a page clicked among the other pages opens.

import {
    isClickedPage,
    loadPageIntoFrame,
    pageClickWasRefused,
    pageFrameIsReadyToShow,
    showPageLoadingCover,
    storeClickedPageRecord,
    takeClickedPageRecord,
} from "./pageFrame";
import { kPageLoadNumberAttribute } from "./workspaceFrames";

// All Pages is showing, so a page change keeps the page being left on screen until the new one is
// ready.
vi.mock("./pageViewSettings", () => ({ isShowingOtherPages: () => true }));

function cover(): HTMLElement | null {
    return document.getElementById("page-loading-cover");
}

function pageBeingLeft(): HTMLElement | null {
    return document.getElementById("page-outgoing");
}

// Bloom changes to the clicked page: the new page loads behind the one being left, and returns the
// load number that the new page passes back when it is ready to show.
function startPageChange(): number {
    loadPageIntoFrame("about:blank", () => {});
    expect(
        pageBeingLeft(),
        "Sanity check: the page being left should stay on screen",
    ).not.toBeNull();
    const frame = document.getElementById("page") as HTMLIFrameElement;
    frame.dispatchEvent(new Event("load"));
    return Number(frame.getAttribute(kPageLoadNumberAttribute));
}

// The Edit tab's page frame, in its host, and a click on page "clicked" among the other pages.
function clickAnotherPage(): void {
    const host = document.createElement("div");
    const frame = document.createElement("iframe");
    frame.id = "page";
    host.appendChild(frame);
    document.body.appendChild(host);
    showPageLoadingCover();
    storeClickedPageRecord({
        pageId: "clicked",
        left: 0,
        top: 0,
        xFraction: 0.5,
        yFraction: 0.5,
    });
}

describe("the wait cursor after a click on another page", () => {
    beforeEach(() => {
        vi.useFakeTimers();
        clickAnotherPage();
        expect(
            cover(),
            "Sanity check: the click should put up the wait cursor",
        ).not.toBeNull();
        expect(isClickedPage("clicked")).toBe(true);
    });

    afterEach(() => {
        vi.useRealTimers();
        takeClickedPageRecord();
        document.body.innerHTML = "";
    });

    test("stays while Bloom has not answered, however long that takes", () => {
        vi.advanceTimersByTime(60000);

        expect(
            cover(),
            "Bloom can take the click late and open the page then",
        ).not.toBeNull();
        expect(isClickedPage("clicked")).toBe(true);
    });

    test("goes at once, with the record of the click, when Bloom does not open the page", () => {
        pageClickWasRefused("clicked");

        expect(cover()).toBeNull();
        expect(isClickedPage("clicked")).toBe(false);
    });

    test("stays when Bloom refuses the click while it is changing to another page", () => {
        const outgoing = document.createElement("iframe");
        outgoing.id = "page-outgoing";
        document.body.appendChild(outgoing);

        pageClickWasRefused("clicked");

        expect(
            cover(),
            "The page change under way takes the cover away when its page shows",
        ).not.toBeNull();
        expect(isClickedPage("clicked")).toBe(false);
    });

    test("keeps the record of a later click when Bloom refuses an earlier one", () => {
        storeClickedPageRecord({
            pageId: "later",
            left: 0,
            top: 0,
            xFraction: 0.5,
            yFraction: 0.5,
        });

        pageClickWasRefused("clicked");

        expect(isClickedPage("later")).toBe(true);
    });

    test("is taken down with the page being left when the new page never says it is ready", () => {
        startPageChange();

        vi.advanceTimersByTime(10000);

        expect(pageBeingLeft()).toBeNull();
        expect(cover()).toBeNull();
    });

    test("for a second click stays when the first page change's wait for its page runs out", () => {
        const loadNumber = startPageChange();
        pageFrameIsReadyToShow(loadNumber);
        expect(
            cover(),
            "Sanity check: the cover should go when the new page shows",
        ).toBeNull();

        // The user clicks another page before Bloom has started changing to it.
        showPageLoadingCover();
        vi.advanceTimersByTime(10000);

        expect(
            cover(),
            "The wait for the first page must not take down the second click's cover",
        ).not.toBeNull();
    });
});
