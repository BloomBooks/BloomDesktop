// Shows the rest of the book around the page being edited, laid out in spreads the way the
// finished book will be read: usually the front cover alone on the right, then each left page
// beside its facing right page, as many spreads per row as fit, with further rows above and below.
// Calendars and books sized for a screen are grouped differently; see bookGridLayout.ts.
//
// Only the page being edited is editable. Every other page is a read-only rendering in its own
// iframe, so none of the editing code (which assumes this document holds exactly one .bloom-page)
// can see it. Clicking one of those pages makes it the page being edited.
//
// A book can have hundreds of pages, so a page is rendered only while it is on or near the
// screen; the rest are empty boxes of the right size.

import $ from "jquery";
import { get } from "../../utils/bloomApi";
import {
    getPageListBundleExports,
    getWorkspaceBundleExports,
} from "./workspaceFrames";
import type { IPageListFrameExports } from "../pageThumbnailList/pageThumbnailList";
import { getSpreadShape, makeGridLayout } from "./bookGridLayout";
import { kBloomPurple } from "../../bloomMaterialUITheme";

interface IGridPage {
    key: string;
    caption: string;
}

// Where the clicked page was on screen, so the next page document can put it back there.
const kClickedPageKey = "bloom-edit-clickedGridPage";

// On this document's root once replayTheClickThatOpenedThisPage() has finished.
const kOpeningClickDoneClass = "bloom-book-grid-opening-click-done";

// Horizontal space between one spread and the next.
const kSpreadGap = 40;
// Vertical space between rows.
const kRowGap = 40;
// Width on screen, in pixels, of the outline around the page being edited.
const kEditedPageOutlineWidth = 3;

interface IClickedPageRecord {
    pageId: string;
    // The page's position in the page frame's viewport when it was clicked.
    left: number;
    top: number;
    // Where in the page the click landed, as a fraction of its width and height.
    xFraction: number;
    yFraction: number;
}

const kGridStyles = `
#page-scaling-container.bloom-book-grid-on {
    position: relative;
    box-sizing: border-box;
}
#bloom-book-grid {
    position: absolute;
    pointer-events: none;
    z-index: 0;
}
.bloom-book-grid-cell {
    position: absolute;
    box-sizing: border-box;
    background-color: rgba(255, 255, 255, 0.08);
    pointer-events: auto;
}
.bloom-book-grid-cell.bloom-book-grid-rendered {
    background-color: white;
}
/* The page being edited sits in this cell itself; the cell must neither hide it nor take its clicks.
   The outline marks it among the other pages. The z-index puts the outline over the facing page's
   cell, so it shows along the edge the two pages share. */
.bloom-book-grid-cell.bloom-book-grid-edited {
    background-color: transparent;
    pointer-events: none;
    outline: var(--bloom-edited-page-outline-width) solid ${kBloomPurple};
    z-index: 1;
}
.bloom-book-grid-frame {
    display: block;
    width: 100%;
    height: 100%;
    border: 0 none;
    pointer-events: none;
}
.bloom-book-grid-veil {
    position: absolute;
    inset: 0;
}
`;

// The controls Bloom shows above the page live in a bar fixed across the top of the page frame, so
// they are in the same place whichever page is being edited, and whether or not the other pages are
// showing. The bar covers the space Bloom already leaves above the page (#labelAndLayoutPane), so
// the page is no further down than it would be without it.
const kControlsBarStyles = `
body > .above-page-control-container.bloom-controls-bar {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    height: var(--bloom-controls-bar-height);
    max-width: none !important;
    padding: 0 16px;
    box-sizing: border-box;
    z-index: 1000;
    background-color: var(--bloom-controls-bar-background);
}
`;

let gridLayer: HTMLElement | undefined;
// Where the user clicked the page that is now being edited, back when it was one of the other
// pages, as a fraction of its width and height. See replayTheClickThatOpenedThisPage().
let openingClick: { xFraction: number; yFraction: number } | undefined;
// Where the page being edited is in this page frame's viewport, as the user last saw it, and the
// zoom it was seen at. Laying the pages out again (a zoom change, a narrower window) scrolls to keep
// the page there; see keepEditedPageInPlace().
let editedPagePlace: { left: number; top: number; zoom: string } | undefined;
let gridStyle: HTMLStyleElement | undefined;
let observers: { disconnect(): void }[] = [];

/**
 * Turn the view of the other pages on or off, remembering the choice for the next page. The page
 * view chooser in the shell's top bar calls this through the page frame's exports.
 */
export function setShowingOtherPages(show: boolean): void {
    if (show) {
        setupBookGridView();
    } else {
        removeBookGridView();
    }
}

/**
 * Build the grid of other pages, if the user has turned it on. Only the live Edit tab gets one:
 * the same editing code also loads pages off-screen to process them, and there must be nothing
 * extra in the document then. The promise resolves once the grid is laid out, scrolled into place,
 * and every page on screen is drawn (at once, if there is no grid to build).
 */
export function setupBookGridView(): Promise<void> {
    if (!isShowingOtherPagesHere()) {
        return Promise.resolve();
    }
    if (gridLayer) {
        return Promise.resolve();
    }
    return new Promise((resolve) => {
        get(
            "pageList/pages",
            (response) => {
                const pages: IGridPage[] = response.data.pages;
                if (
                    !getWorkspaceBundleExports().isShowingOtherPages() ||
                    gridLayer
                ) {
                    resolve();
                    return;
                }
                buildGrid(pages).then(resolve);
            },
            // get() also sends here anything buildGrid throws. Either way the page must still be
            // shown, but only a failed request is expected: anything else is a bug to report.
            (error) => {
                resolve();
                if (!error.isAxiosError) throw error;
            },
        );
    });
}

/**
 * True when this document is the live Edit tab's page and the user has chosen to see the other
 * pages of the book around it. The page is then placed where the user clicked it, so code setting
 * up the page must not scroll it away (for example, by focusing a text box without preventScroll).
 */
export function isShowingOtherPagesHere(): boolean {
    return (
        window.frameElement?.id === "page" &&
        getWorkspaceBundleExports().isShowingOtherPages()
    );
}

/** Remove the grid of other pages and everything it changed in this document. */
export function removeBookGridView(): void {
    observers.forEach((o) => o.disconnect());
    observers = [];
    editedPagePlace = undefined;
    gridLayer?.remove();
    gridLayer = undefined;
    gridStyle?.remove();
    gridStyle = undefined;
    const container = getScalingContainer();
    if (container) {
        container.classList.remove("bloom-book-grid-on");
        container.style.removeProperty("padding-left");
        container.style.removeProperty("padding-top");
    }
    // The page is back at the top left.
    window.scrollTo(0, 0);
    repositionBubbles();
}

// Bloom's hint and source bubbles are qTip popups, placed where their box was when they were
// shown. Whenever the grid moves the page they must move with it, or they are left behind, and
// hold the document open so it scrolls to empty space. Move them in one step: by default qTip
// slides a visible bubble to its new place, which trails behind the page.
function repositionBubbles(): void {
    (
        $("[data-hasqtip]") as unknown as {
            qtip(command: string, event: undefined, effect: boolean): void;
        }
    ).qtip("reposition", undefined, false);
}

function getControlsContainer(): HTMLElement | undefined {
    return document.getElementsByClassName(
        "above-page-control-container",
    )[0] as HTMLElement | undefined;
}

/**
 * Put the controls Bloom shows above the page into a bar fixed across the top of the page frame
 * (see kControlsBarStyles). Only the live Edit tab gets it.
 */
export function setupControlsBar(): void {
    if (window.frameElement?.id !== "page") return;
    const style = document.createElement("style");
    style.textContent = kControlsBarStyles;
    document.head.appendChild(style);
    document.body.style.setProperty(
        "--bloom-controls-bar-background",
        getComputedStyle(document.body).backgroundColor,
    );
    document.body.style.setProperty(
        "--bloom-controls-bar-height",
        `${document.getElementById("labelAndLayoutPane")!.offsetHeight}px`,
    );
    moveControlsToBar();
    // The controls are rendered inside the scaling container, and may be (re)created there.
    new MutationObserver(() => moveControlsToBar()).observe(
        getScalingContainer()!,
        { childList: true },
    );
}

function moveControlsToBar(): void {
    const controls = getControlsContainer();
    if (!controls || controls.parentElement === document.body) return;
    controls.classList.add("bloom-controls-bar");
    document.body.appendChild(controls);
}

function getScalingContainer(): HTMLElement | null {
    return document.getElementById("page-scaling-container");
}

function getEditedPage(): HTMLElement {
    return document.querySelector(".bloom-page") as HTMLElement;
}

function buildGrid(pages: IGridPage[]): Promise<void> {
    const container = getScalingContainer()!;
    const editedPage = getEditedPage();
    const editedIndex = pages.findIndex((p) => p.key === editedPage.id);
    if (editedIndex < 0) {
        throw new Error(
            `bookGridView: the page being edited (${editedPage.id}) is not in the page list`,
        );
    }

    container.classList.add("bloom-book-grid-on");
    gridLayer = document.createElement("div");
    gridLayer.id = "bloom-book-grid";
    // bloom-ui marks it as editing furniture, never part of the page.
    gridLayer.classList.add("bloom-ui");
    container.prepend(gridLayer);

    // Collected before adding our own styles, which the page frames do not need.
    const headContent = getStylesheetsForPageFrames();
    const bodyAttributes = getBodyAttributesForPageFrames();
    gridStyle = document.createElement("style");
    gridStyle.textContent = kGridStyles;
    document.head.appendChild(gridStyle);

    const cells = new Map<string, HTMLElement>();
    pages.forEach((page, index) => {
        const cell = document.createElement("div");
        cell.classList.add("bloom-book-grid-cell");
        cell.setAttribute("data-page-id", page.key);
        cell.setAttribute("data-page-index", index.toString());
        if (index === editedIndex) {
            cell.classList.add("bloom-book-grid-edited");
        } else {
            const veil = document.createElement("div");
            veil.classList.add("bloom-book-grid-veil");
            veil.addEventListener("click", (e) =>
                onClickOtherPage(e, cell, page),
            );
            cell.appendChild(veil);
        }
        gridLayer!.appendChild(cell);
        cells.set(page.key, cell);
    });

    const layout = () => layoutGrid(pages.length, editedIndex, cells);
    layout();
    restoreScrollPosition(editedPage);
    rememberWhereEditedPageIs();
    const onScroll = () => rememberWhereEditedPageIs();
    window.addEventListener("scroll", onScroll);
    observers.push({
        disconnect: () => window.removeEventListener("scroll", onScroll),
    });

    // Draw the pages that are on screen now, rather than waiting for the observer below, so the
    // caller can know when the view is complete.
    const onScreen = Array.from(cells.values()).filter(
        (cell) =>
            !cell.classList.contains("bloom-book-grid-edited") &&
            isOnScreen(cell),
    );
    const drawn = Promise.all(
        onScreen.map((cell) =>
            renderPageInCell(cell, headContent, bodyAttributes),
        ),
    );

    // Anything that moves or resizes the page being edited (the controls rendered above it, a
    // zoom change, a narrower window) changes where the other pages belong.
    const resizeObserver = new ResizeObserver(() => layout());
    resizeObserver.observe(container);
    resizeObserver.observe(editedPage);
    observers.push(resizeObserver);
    const mutationObserver = new MutationObserver(() => layout());
    mutationObserver.observe(container, { childList: true });
    observers.push(mutationObserver);

    // Render a page's iframe only while it is on or near the screen, and drop it when it goes
    // far away, so a 200-page book costs no more than a 10-page one.
    const intersectionObserver = new IntersectionObserver(
        (entries) => {
            entries.forEach((entry) => {
                const cell = entry.target as HTMLElement;
                if (entry.isIntersecting) {
                    renderPageInCell(cell, headContent, bodyAttributes);
                } else {
                    cell.querySelector("iframe")?.remove();
                    cell.classList.remove("bloom-book-grid-rendered");
                }
            });
        },
        { rootMargin: "100% 50%" },
    );
    cells.forEach((cell) => {
        if (!cell.classList.contains("bloom-book-grid-edited")) {
            intersectionObserver.observe(cell);
        }
    });
    observers.push(intersectionObserver);
    return drawn.then(() => undefined);
}

function isOnScreen(element: HTMLElement): boolean {
    const rect = element.getBoundingClientRect();
    return (
        rect.right > 0 &&
        rect.bottom > 0 &&
        rect.left < window.innerWidth &&
        rect.top < window.innerHeight
    );
}

// Position every cell, and pad the scaling container so that the page being edited lands in its
// own cell. Cells are laid out in the page's own (unzoomed) pixels; the scaling container zooms
// them along with the page.
function layoutGrid(
    pageCount: number,
    editedIndex: number,
    cells: Map<string, HTMLElement>,
): void {
    if (!gridLayer) return;
    const container = getScalingContainer()!;
    const editedPage = getEditedPage();
    const pageWidth = editedPage.offsetWidth;
    const pageHeight = editedPage.offsetHeight;

    // The scaling container zooms everything in it, the outline around the page being edited
    // included; keep that outline the same width on screen at any zoom.
    const zoom = editedPage.getBoundingClientRect().width / pageWidth;
    const outlineWidth = kEditedPageOutlineWidth / zoom;
    gridLayer.style.setProperty(
        "--bloom-edited-page-outline-width",
        `${outlineWidth}px`,
    );
    // Room around the grid, so the outline of a page at its edge is not cut off by the edge of
    // the page frame. Nothing counts an outline when working out how far the frame can scroll.
    const edgeMargin = Math.max(kSpreadGap / 2, outlineWidth);

    // The container is border-box (see kGridStyles), so its clientWidth is the whole width,
    // whatever padding this sets below. How many spreads fit across must not depend on that
    // padding: the padding depends on the answer, and the two would chase each other for ever.
    const grid = makeGridLayout(
        getSpreadShape(editedPage),
        pageWidth,
        pageHeight,
        container.clientWidth - 2 * edgeMargin,
        kSpreadGap,
        kRowGap,
    );

    const edited = grid.positionOfPage(editedIndex);
    const wantedPaddingLeft = `${edited.x + edgeMargin}px`;
    const wantedPaddingTop = `${edited.y + kRowGap}px`;
    if (container.style.paddingLeft !== wantedPaddingLeft) {
        container.style.paddingLeft = wantedPaddingLeft;
    }
    if (container.style.paddingTop !== wantedPaddingTop) {
        container.style.paddingTop = wantedPaddingTop;
    }

    // The grid's origin is wherever that puts the page being edited, less its own offset in the grid.
    const originX = editedPage.offsetLeft - edited.x;
    const originY = editedPage.offsetTop - edited.y;
    gridLayer.style.left = `${originX}px`;
    gridLayer.style.top = `${originY}px`;

    let index = 0;
    cells.forEach((cell) => {
        const position = grid.positionOfPage(index);
        cell.style.left = `${position.x}px`;
        cell.style.top = `${position.y}px`;
        cell.style.width = `${pageWidth}px`;
        cell.style.height = `${pageHeight}px`;
        index++;
    });
    const last = grid.positionOfPage(pageCount - 1);
    gridLayer.style.width = `${grid.spreadsPerRow * (grid.spreadWidth + kSpreadGap)}px`;
    gridLayer.style.height = `${last.y + pageHeight + edgeMargin}px`;
    keepEditedPageInPlace();
    repositionBubbles();
}

function getZoomOfScalingContainer(): string {
    return getScalingContainer()!.style.transform;
}

// Note where the user sees the page being edited. A scroll the browser makes on its own while the
// zoom changes (it clamps the scroll position when zooming out shrinks the document) is not the
// user moving the page, so it is ignored until keepEditedPageInPlace() has caught up with the zoom.
function rememberWhereEditedPageIs(): void {
    const zoom = getZoomOfScalingContainer();
    if (editedPagePlace && editedPagePlace.zoom !== zoom) return;
    const rect = getEditedPage().getBoundingClientRect();
    editedPagePlace = { left: rect.left, top: rect.top, zoom };
}

// Scroll so the page being edited is where the user last saw it. Without this, a zoom change keeps
// the scroll position while everything grows or shrinks around it, and the page ends up thousands
// of pixels out of view.
function keepEditedPageInPlace(): void {
    if (!editedPagePlace) return;
    const rect = getEditedPage().getBoundingClientRect();
    const dx = rect.left - editedPagePlace.left;
    const dy = rect.top - editedPagePlace.top;
    editedPagePlace.zoom = getZoomOfScalingContainer();
    if (Math.abs(dx) >= 1 || Math.abs(dy) >= 1) window.scrollBy(dx, dy);
}

// Draw one page in its cell. The promise resolves when the page and its pictures have loaded, or
// when the cell has scrolled away and been dropped before that.
function renderPageInCell(
    cell: HTMLElement,
    headContent: string,
    bodyAttributes: string,
): Promise<void> {
    if (cell.querySelector("iframe")) return Promise.resolve();
    const pageId = cell.getAttribute("data-page-id")!;
    const frame = document.createElement("iframe");
    frame.classList.add("bloom-book-grid-frame");
    frame.setAttribute("tabindex", "-1");
    // No scripts or event handlers in a book's page may run here, where the page is only shown.
    // allow-same-origin still lets this document write the page into the frame.
    frame.setAttribute("sandbox", "allow-same-origin");
    // Insert before the veil, so the veil stays on top to take the clicks.
    cell.insertBefore(frame, cell.querySelector(".bloom-book-grid-veil"));
    return getPageContent(pageId, () => frame.isConnected).then((content) => {
        // It may have scrolled away, and been dropped, while we waited.
        if (!frame.isConnected) return;
        if (content === undefined) {
            // The request failed. Without the frame, the page is tried again when it next comes
            // near the screen.
            frame.remove();
            return;
        }
        return new Promise<void>((resolve) => {
            frame.addEventListener("load", () => resolve(), { once: true });
            // Written into the frame rather than put in srcdoc, so that saving the page being
            // edited, which serializes this whole document, does not carry every page's HTML
            // along in an attribute.
            const frameDocument = frame.contentDocument!;
            frameDocument.open();
            frameDocument.write(
                `<!DOCTYPE html><html><head><base href="${
                    document.baseURI
                }">${headContent}<style>
                html, body { margin: 0 !important; padding: 0 !important; overflow: hidden; background: transparent; }
                .bloom-page { margin: 0 !important; }
            </style></head><body ${bodyAttributes}>${fullSizePictures(
                content,
            )}</body></html>`,
            );
            frameDocument.close();
            cell.classList.add("bloom-book-grid-rendered");
        });
    });
}

// No more than this many page requests at once, as in the page list (see PageThumbnail.tsx): more
// would starve the page being edited and the toolbox, which are often loading at the same time.
const kMaxPageRequests = 4;
let activePageRequests = 0;
const queuedPageRequests: (() => void)[] = [];

// Get a page's content, as made for the page list, once a request slot is free. Resolves to
// undefined if the request fails, or if stillWanted() says no by the time a slot is free.
function getPageContent(
    pageId: string,
    stillWanted: () => boolean,
): Promise<string | undefined> {
    return new Promise((resolve) => {
        const start = () => {
            if (!stillWanted()) {
                resolve(undefined);
                startNextPageRequest();
                return;
            }
            activePageRequests++;
            const finish = (content: string | undefined) => {
                activePageRequests--;
                resolve(content);
                startNextPageRequest();
            };
            get(
                `pageList/pageContent?page-id=${encodeURIComponent(pageId)}`,
                (response) => finish(response.data.content),
                () => finish(undefined),
            );
        };
        if (activePageRequests < kMaxPageRequests) start();
        else queuedPageRequests.push(start);
    });
}

function startNextPageRequest(): void {
    if (activePageRequests < kMaxPageRequests) queuedPageRequests.shift()?.();
}

// The page content comes in the form made for the page list, whose pictures ask the server for small
// versions by adding "thumbnail=1" to the query (see MarkImageNodesForThumbnail in PageListApi.cs),
// sometimes followed by "&transparent=...". These pages are shown at full size, so ask for the
// pictures themselves, keeping any other parameters: "x.png?thumbnail=1&transparent=yes" becomes
// "x.png?transparent=yes", and "x.png?optional=true&thumbnail=1" becomes "x.png?optional=true".
function fullSizePictures(pageHtml: string): string {
    return pageHtml
        .replace(/\?thumbnail=1(&amp;|&)/g, "?")
        .replace(/(\?|&amp;|&)thumbnail=1/g, "");
}

// The page frames show a page with the same stylesheets as the page being edited, but none of
// its scripts.
function getStylesheetsForPageFrames(): string {
    return Array.from(
        document.head.querySelectorAll("link[rel='stylesheet'], style"),
    )
        .map((element) => element.outerHTML)
        .join("");
}

// The body's classes and data attributes affect how a page displays (e.g. which languages show).
function getBodyAttributesForPageFrames(): string {
    return Array.from(document.body.attributes)
        .filter((a) => a.name !== "style")
        .map((a) => `${a.name}="${a.value.replace(/"/g, "&quot;")}"`)
        .join(" ");
}

function onClickOtherPage(
    e: MouseEvent,
    cell: HTMLElement,
    page: IGridPage,
): void {
    e.preventDefault();
    e.stopPropagation();
    getWorkspaceBundleExports().showPageLoadingCover();
    const rect = cell.getBoundingClientRect();
    const record: IClickedPageRecord = {
        pageId: page.key,
        left: rect.left,
        top: rect.top,
        xFraction: (e.clientX - rect.left) / rect.width,
        yFraction: (e.clientY - rect.top) / rect.height,
    };
    try {
        sessionStorage.setItem(kClickedPageKey, JSON.stringify(record));
    } catch {
        // Without storage the new page just scrolls into view.
    }
    // Exactly as if the page had been clicked in the page list: queued behind any page-list request
    // still on its way, and sending the page being left so C# saves the freshest copy.
    whenPageListIsReady((pageList) =>
        pageList.postPageClicked(page.key, page.caption),
    );
}

// The page list's frame can still be loading (Bloom reloads it along with a page change) after this
// page is showing. Nothing can be queued in a page list that has not loaded, so waiting for it keeps
// the click in order; the loading cover shows the wait cursor meanwhile. A page list that has not
// loaded in kMaxWaitForPageListMs is broken, which is reported rather than waited on for ever.
const kMaxWaitForPageListMs = 10000;
function whenPageListIsReady(
    task: (pageList: IPageListFrameExports) => void,
    giveUpAt = Date.now() + kMaxWaitForPageListMs,
): void {
    const pageList = getPageListBundleExports();
    if (pageList) {
        task(pageList);
        return;
    }
    if (Date.now() > giveUpAt) {
        throw new Error(
            "The page list never finished loading, so a click on another page could not be sent.",
        );
    }
    window.setTimeout(() => whenPageListIsReady(task, giveUpAt), 50);
}

// When the user clicked a page in the grid, put the page, now being edited, back where it was on
// screen, so it looks as if the page simply became editable. Otherwise bring it into view.
function restoreScrollPosition(editedPage: HTMLElement): void {
    let record: IClickedPageRecord | undefined;
    try {
        const saved = sessionStorage.getItem(kClickedPageKey);
        sessionStorage.removeItem(kClickedPageKey);
        record = saved ? JSON.parse(saved) : undefined;
    } catch {
        record = undefined;
    }
    const rect = editedPage.getBoundingClientRect();
    // Scroll this document only, with scrollBy/scrollTo. scrollIntoView would also scroll the Edit
    // tab's own containers, outside this frame, and push the page list off the side of the window.
    if (record && record.pageId === editedPage.id) {
        window.scrollBy(rect.left - record.left, rect.top - record.top);
        openingClick = {
            xFraction: record.xFraction,
            yFraction: record.yFraction,
        };
    } else {
        window.scrollBy(
            rect.left - (window.innerWidth - rect.width) / 2,
            rect.top - (window.innerHeight - rect.height) / 2,
        );
    }
}

/**
 * If the user opened this page by clicking it while it was one of the other pages, do what that
 * click would have done had the page been editable all along: put the text cursor where they
 * clicked, or select the picture or other canvas element they clicked. Call it once the page is
 * set up.
 */
export async function replayTheClickThatOpenedThisPage(): Promise<void> {
    try {
        await replayOpeningClick();
    } finally {
        // Marks, for tests, that the page has done all it will do about the click that opened it.
        document.documentElement.classList.add(kOpeningClickDoneClass);
    }
}

async function replayOpeningClick(): Promise<void> {
    if (!openingClick) return;
    const editedPage = getEditedPage();
    // Where the page is now, which is not necessarily where it was put when it loaded.
    const rect = editedPage.getBoundingClientRect();
    const x = rect.left + openingClick.xFraction * rect.width;
    const y = rect.top + openingClick.yFraction * rect.height;
    openingClick = undefined;
    const target = document.elementFromPoint(x, y) as HTMLElement | null;
    // Only ever something on the page being edited. Whatever else is at that point (another page,
    // the bar of controls) is not what the user clicked, and a click on another page would open
    // that page, whose own replay could open the next one, and so on through the book.
    if (!target || !editedPage.contains(target)) return;
    const editable = target.closest(".bloom-editable") as HTMLElement | null;
    // CKEditor puts back the text it took over when it finishes starting, which would wipe out a
    // cursor placed before then.
    if (editable) await whenCkEditorIsReady(editable);
    // A real click's events, so the canvas element code selects what was clicked.
    const init = {
        bubbles: true,
        cancelable: true,
        composed: true,
        view: window,
        clientX: x,
        clientY: y,
        button: 0,
    };
    target.dispatchEvent(new MouseEvent("mousedown", { ...init, buttons: 1 }));
    target.dispatchEvent(new MouseEvent("mouseup", { ...init, buttons: 0 }));
    target.dispatchEvent(new MouseEvent("click", { ...init, buttons: 0 }));
    if (editable) placeCaretAt(editable, x, y);
}

function whenCkEditorIsReady(editable: HTMLElement): Promise<void> {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const editor = (editable as any).bloomCkEditor;
    if (!editor || editor.status === "ready") return Promise.resolve();
    return new Promise((resolve) =>
        editor.once("instanceReady", () => resolve()),
    );
}

// Put the text cursor in this text box at this point in the viewport.
function placeCaretAt(editable: HTMLElement, x: number, y: number): void {
    // Focusing must not scroll: the page is exactly where it was when clicked.
    editable.focus({ preventScroll: true });
    const range = document.caretRangeFromPoint(x, y);
    if (range && editable.contains(range.startContainer)) {
        const selection = window.getSelection();
        selection?.removeAllRanges();
        selection?.addRange(range);
    }
}
