import { describe, it, expect } from "vitest";
import $ from "jquery";
import { getTheOneToolbox } from "./toolbox";

// jQuery 1.x's global cache of elements it has bound handlers to. It is not in the published
// typings. Each entry that has events holds the element as handle.elem.
type JQueryCacheEntry = { handle?: { elem?: Element } };
const jQueryCache = (): Record<string, JQueryCacheEntry> =>
    ($ as unknown as { cache: Record<string, JQueryCacheEntry> }).cache;

// How many of the toolbox jQuery's cache entries hold an element inside 'container'.
const cacheEntriesInside = (container: HTMLElement): number =>
    Object.values(jQueryCache()).filter((entry) => {
        const elem = entry?.handle?.elem;
        return !!elem && container.contains(elem);
    }).length;

// Makes a body in a document of its own, standing in for the page iframe's document: the
// toolbox binds its handlers with its own frame's jQuery onto elements that live there.
const makePageBody = (): HTMLElement => {
    const pageDoc = document.implementation.createHTMLDocument("page");
    pageDoc.body.innerHTML = `<div class="bloom-page">
            <div class="bloom-translationGroup">
                <div class="bloom-editable" lang="en"><p>one</p></div>
                <div class="bloom-editable" lang="fr"><p>deux</p></div>
            </div>
        </div>`;
    return pageDoc.body;
};

describe("ToolBox.releaseElementsForTools (BL-16840)", () => {
    // Switching pages navigates only the page iframe; the toolbox frame lives on. Unless the
    // toolbox's jQuery is told to let go of the elements it bound handlers to, its cache keeps
    // each page's elements, and through them that page's whole JS realm, for the rest of the
    // session.
    it("leaves no entry in the toolbox's jQuery cache for the page's elements", () => {
        const pageBody = makePageBody();
        const editables = pageBody.querySelectorAll(".bloom-editable");
        // sanity check on the test page itself
        expect(editables.length).toBe(2);
        expect(pageBody.ownerDocument).not.toBe(document);
        expect(cacheEntriesInside(pageBody)).toBe(0);

        getTheOneToolbox().configureElementsForTools(pageBody);
        // sanity check: configuring really did put the page's editables in the cache, or the
        // assertion below would pass whether or not the release works
        expect(cacheEntriesInside(pageBody)).toBe(editables.length);

        getTheOneToolbox().releaseElementsForTools(pageBody);

        expect(cacheEntriesInside(pageBody)).toBe(0);
    });

    it("does not touch elements outside the container it is given", () => {
        const leavingPage = makePageBody();
        const otherPage = makePageBody();
        getTheOneToolbox().configureElementsForTools(leavingPage);
        getTheOneToolbox().configureElementsForTools(otherPage);
        expect(cacheEntriesInside(otherPage)).toBe(2);

        getTheOneToolbox().releaseElementsForTools(leavingPage);

        expect(cacheEntriesInside(leavingPage)).toBe(0);
        expect(cacheEntriesInside(otherPage)).toBe(2);
        getTheOneToolbox().releaseElementsForTools(otherPage);
    });
});
