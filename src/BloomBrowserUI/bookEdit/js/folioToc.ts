/// <reference path="../../typings/jquery.qtip.d.ts" />
// The table of contents pages of a folio (see the Folio template). Each page's list is a
// translation group whose data-folio-book-ids names some of the books the folio publishes, in
// order, by bookInstanceId, and whose data-folio-book-titles is a JSON object giving the last title
// seen for each of those ids, so a book that is later deleted can still be named. Its language 1
// box is a locked text box (lockedEditable.ts): Bloom writes it, as one entry per book, a title and
// "##" where the page number will go, and the person editing can format it but not type in it.
// When the folio is published, Bloom rewrites it again with each book's first page number
// (FolioPdfPartsMaker.FillTablesOfContents).
//
// A bubble beside the list, headed "Folio", holds the buttons for choosing the books and for the
// folio's settings in Book and Page Settings. Each time the page is shown, any book the list names
// that is no longer in the collection is removed from it, and the bubble says so. A second bubble,
// beside the heading, says that the page numbers are added when the PDF is made.
//
// A folio has one table of contents page. When its list is too long for it, the list flows on into
// pages that flow text adds, and takes away again when they are no longer needed: flow text offers
// to make pages for a box whose text does not fit and has nowhere to go, and on this page that offer
// is taken at once, without showing it. The boxes on those pages are locked too; they are known by
// the entries they hold.

import $ from "jquery";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { getWorkspaceBundleExports } from "./workspaceFrames";
import { Link } from "../../react_components/BookGridSetup/BookLinkTypes";
import bloomQtipUtils from "./bloomQtipUtils";
import folioSvg from "../../images/folio.svg?raw";
import { getAsync } from "../../utils/bloomApi";
import { IBookInfo } from "../../collectionsTab/BooksOfCollection";
import { wrapWithRequestPageContentDelay } from "./bloomEditing";
import { compareFolioListWithCollection } from "./folioTocBooks";
import {
    createPagesFor,
    updateCreatePagesButtons,
} from "../flowText/flowCreatePagesButton";
import { getFlowPassRunner } from "../flowText/flowContinueButton";
import {
    kCreatePagesButtonClass,
    kFlowChainAttr,
    kKeepWholeClass,
} from "../flowText/flowConstants";
import { getFlowGroupsOfPage } from "../flowText/flowChain";
import { emptyChainAfter, requestWalk } from "../flowText/flowBoundaryClient";
import { lockEditable, setUpLockedEditables } from "./lockedEditable";

const kListClass = "bloom-folio-toc-list";
const kBookIdsAttribute = "data-folio-book-ids";
const kBookTitlesAttribute = "data-folio-book-titles";
// These three match what FolioPdfPartsMaker.FillTablesOfContents writes for the PDF.
const kEntryClass = "bloom-folio-toc-entry";
const kEntryTitleClass = "bloom-folio-toc-title";
const kEntryPageNumberClass = "bloom-folio-toc-page-number";
// What an entry shows where its page number will be, until the PDF is made.
const kPageNumberStandIn = "##";

export function setupFolioTocPages(container: HTMLElement) {
    lockTableOfContentsBoxes(container);
    const lists = Array.from(
        container.getElementsByClassName(kListClass),
    ) as HTMLElement[];
    if (lists.length === 0) return;
    const bubbles = lists.map((list) => addFolioBubble(list));
    for (const list of lists) makePagesWhenFlowOffers(list);
    const heading = container.querySelector(".bloom-folio-toc-heading");
    if (heading) addPageNumbersBubble(heading as HTMLElement);
    wrapWithRequestPageContentDelay(async () => {
        // A box CkEditor is still setting up gets back, when it is ready, the text it had when
        // the page opened; a book removed from the list before then would come back.
        await whenCkEditorIsReady();
        const response = await getAsync("collections/books?realTitle=true");
        const collectionBooks = response.data as IBookInfo[];
        for (const [i, list] of lists.entries())
            await removeBooksNoLongerInCollection(
                list,
                bubbles[i],
                collectionBooks,
            );
    }, "folioTocRemoveMissingBooks");
}

/** Resolves once every CkEditor instance on the page is ready (at once when there is none). */
async function whenCkEditorIsReady(): Promise<void> {
    const ckeditor = (window as unknown as { CKEDITOR?: ICkEditorGlobal })
        .CKEDITOR;
    if (!ckeditor) return;
    for (;;) {
        const instances = Object.values(ckeditor.instances);
        if (instances.length === 0) {
            if (!document.querySelector(".bloom-editable")) return;
            await new Promise<void>((resolve) =>
                ckeditor.on("instanceReady", () => resolve()),
            );
            continue;
        }
        const notReady = instances.find((instance) => !instance.instanceReady);
        if (!notReady) return;
        await new Promise<void>((resolve) =>
            notReady.on("instanceReady", () => resolve()),
        );
    }
}

interface ICkEditorInstance {
    instanceReady?: boolean;
    on: (event: string, callback: () => void) => unknown;
}
interface ICkEditorGlobal {
    instances: Record<string, ICkEditorInstance>;
    on: (event: string, callback: () => void) => unknown;
}

/**
 * Lock the table of contents' language 1 box, and every box on the pages it flows on into, which
 * are the boxes holding its entries.
 */
function lockTableOfContentsBoxes(container: HTMLElement) {
    const boxes = new Set<HTMLElement>();
    for (const box of Array.from(
        container.querySelectorAll<HTMLElement>(
            `.${kListClass} > .bloom-editable.bloom-content1`,
        ),
    ))
        boxes.add(box);
    for (const entry of Array.from(
        container.querySelectorAll<HTMLElement>(
            `.bloom-editable p.${kEntryClass}`,
        ),
    ))
        boxes.add(entry.closest<HTMLElement>(".bloom-editable")!);
    if (boxes.size === 0) return;
    setUpLockedEditables(container.ownerDocument);
    boxes.forEach((box) => lockEditable(box));
}

async function removeBooksNoLongerInCollection(
    list: HTMLElement,
    bubble: HTMLElement,
    collectionBooks: IBookInfo[],
) {
    const collectionTitles = new Map(
        collectionBooks.map((book) => [book.id, book.title]),
    );
    const ids = getBookIds(list);
    const result = compareFolioListWithCollection(
        ids,
        getBookTitles(list),
        collectionTitles,
    );
    if (result.removed.length > 0) {
        list.setAttribute(kBookIdsAttribute, result.ids.join(" "));
        await writeListText(
            list,
            result.ids.map((id) => result.titles[id]),
        );
        for (const title of result.removed) {
            const notice = document.createElement("div");
            notice.className = "bloom-folio-toc-bubble-notice";
            bubble.appendChild(notice);
            theOneLocalizationManager
                .asyncGetText(
                    "Folio.BookNoLongerInCollection",
                    '"{0}" is no longer in this collection.',
                    "",
                    title,
                )
                .done((text) => {
                    notice.textContent = text;
                    $(list).qtip("reposition");
                });
        }
    }
    if (JSON.stringify(result.titles) !== JSON.stringify(getBookTitles(list)))
        list.setAttribute(kBookTitlesAttribute, JSON.stringify(result.titles));
}

// The bubble is a qtip in the page-scaling container, like the hint bubbles, so it is never part
// of the page. It always shows.
function addFolioBubble(list: HTMLElement): HTMLElement {
    // qtip sets an inline display on the element it is given, so the flex layout goes on a child.
    const content = document.createElement("div");
    const bubble = document.createElement("div");
    bubble.className = "bloom-folio-toc-bubble";
    content.appendChild(bubble);
    const heading = document.createElement("div");
    heading.className = "bloom-folio-toc-bubble-heading";
    // Inline, so the icon takes the heading's color (it draws in currentColor).
    const icon = document.createElement("span");
    icon.className = "bloom-folio-toc-bubble-icon";
    icon.innerHTML = folioSvg;
    heading.appendChild(icon);
    const headingText = document.createElement("span");
    heading.appendChild(headingText);
    bubble.appendChild(heading);
    const chooseBooksButton = document.createElement("button");
    chooseBooksButton.className = "bloom-folio-toc-choose-books";
    chooseBooksButton.onclick = () => chooseBooks(list);
    bubble.appendChild(chooseBooksButton);
    const settingsButton = document.createElement("button");
    settingsButton.className = "bloom-folio-toc-settings";
    settingsButton.onclick = () =>
        getWorkspaceBundleExports().showBookSettingsDialog("folio");
    bubble.appendChild(settingsButton);
    setLocalizedText(headingText, "EditTab.Folio.BubbleHeading", "Folio");
    setLocalizedText(
        chooseBooksButton,
        "EditTab.Folio.ChooseBooks",
        "Choose Books…",
    );
    setLocalizedText(settingsButton, "EditTab.Folio.Settings", "Settings…");

    $(list).qtip({
        content: $(content),
        position: {
            at: "right center",
            my: "left center",
            viewport: $(window),
            adjust: { method: "none" },
            container: bloomQtipUtils.qtipZoomContainer(),
        },
        show: { ready: true, event: false },
        hide: { event: false },
        style: {
            classes: "ui-tooltip-shadow ui-tooltip-plain bloom-folio-toc-qtip",
        },
    });
    return bubble;
}

/**
 * Take flow text's offer to make pages for the rest of the list, whenever it is made. The offer is
 * a button flow text adds to the list's translation group; Folio.less keeps it hidden here.
 */
function makePagesWhenFlowOffers(list: HTMLElement) {
    let making = false;
    const takeOffer = async () => {
        const offer = list.querySelector(
            `:scope > .${kCreatePagesButtonClass}`,
        );
        if (!offer || making) return;
        const box = list.querySelector(
            ".bloom-editable.bloom-content1",
        ) as HTMLElement;
        making = true;
        try {
            await createPagesFor(box, { showLastPage: false });
        } finally {
            making = false;
        }
    };
    new MutationObserver(() => void takeOffer()).observe(list, {
        childList: true,
    });
    void takeOffer();
}

// The Material "info outline" icon, drawn in currentColor.
const kInfoIconSvg =
    '<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor" aria-hidden="true"><path d="M11 7h2v2h-2zm0 4h2v6h-2zm1-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z"/></svg>';

// A bubble beside the heading saying that the list gets its page numbers when the PDF is made, so
// that nobody types them in. Like the Folio bubble, it is a qtip that is never part of the page.
function addPageNumbersBubble(heading: HTMLElement) {
    const content = document.createElement("div");
    const bubble = document.createElement("div");
    bubble.className = "bloom-folio-toc-info";
    content.appendChild(bubble);
    const icon = document.createElement("span");
    icon.className = "bloom-folio-toc-info-icon";
    icon.innerHTML = kInfoIconSvg;
    bubble.appendChild(icon);
    const text = document.createElement("span");
    bubble.appendChild(text);
    setLocalizedText(
        text,
        "EditTab.Folio.PageNumbersInfo",
        "Page numbers will be added when you make the PDF.",
    );
    $(heading).qtip({
        content: $(content),
        position: {
            at: "right center",
            my: "left center",
            viewport: $(window),
            adjust: { method: "none" },
            container: bloomQtipUtils.qtipZoomContainer(),
        },
        show: { ready: true, event: false },
        hide: { event: false },
        style: {
            classes:
                "ui-tooltip-shadow ui-tooltip-plain bloom-folio-toc-qtip bloom-folio-toc-info-qtip",
        },
    });
}

function setLocalizedText(element: HTMLElement, id: string, english: string) {
    theOneLocalizationManager.asyncGetText(id, english, "").done((text) => {
        element.textContent = text;
    });
}

/** The last title seen for each book a table of contents list names, by id. */
function getBookTitles(list: HTMLElement): Record<string, string> {
    return JSON.parse(list.getAttribute(kBookTitlesAttribute) || "{}");
}

/**
 * Rewrite the table of contents as one entry per title: the title, then the stand-in for its page
 * number. When the list already runs on into later pages, those boxes are emptied first and the
 * chain is refitted, so that no title is left behind there as well.
 */
async function writeListText(list: HTMLElement, titles: string[]) {
    // The language 1 box: the one the page shows as its main text.
    const box = list.querySelector(
        ".bloom-editable.bloom-content1",
    ) as HTMLElement;
    const chainId = list.getAttribute(kFlowChainAttr);
    const page = list.closest(".bloom-page") as HTMLElement;
    if (chainId)
        await emptyChainAfter(
            chainId,
            page.id,
            getFlowGroupsOfPage(page).indexOf(list),
        );
    box.innerHTML = "";
    for (const title of titles) {
        const entry = document.createElement("p");
        entry.className = `${kEntryClass} ${kKeepWholeClass}`;
        const titleSpan = document.createElement("span");
        titleSpan.className = kEntryTitleClass;
        titleSpan.textContent = title;
        const numberSpan = document.createElement("span");
        numberSpan.className = kEntryPageNumberClass;
        numberSpan.textContent = kPageNumberStandIn;
        entry.append(titleSpan, numberSpan);
        box.appendChild(entry);
    }
    // Bloom settles a box as it is typed in, not when a script rewrites it, so ask. In a chain,
    // the pass carries what does not fit on into the emptied boxes after this one, and the refit
    // of the chain that follows takes away pages left with nothing on them; otherwise a box that no
    // longer fits gets flow text's offer of pages, which is taken at once.
    const runner = getFlowPassRunner();
    if (chainId) runner?.requestPassFor([box], "folioTocRewritten");
    runner?.markOverflow(box);
    runner?.updatePageOverflow(page);
    updateCreatePagesButtons(page);
    if (chainId)
        await requestWalk(chainId, page.id, box.getAttribute("lang") ?? "");
}

/** The books a table of contents list names, in order. */
function getBookIds(list: HTMLElement): string[] {
    return (list.getAttribute(kBookIdsAttribute) ?? "")
        .split(" ")
        .filter((id) => id);
}

function chooseBooks(list: HTMLElement) {
    const links: Link[] = getBookIds(list).map((id) => ({
        book: {
            id,
            thumbnail: `${window.location.origin}/bloom/api/collections/book/coverImage?book-id=${id}`,
        },
    }));
    getWorkspaceBundleExports().showFolioBooksDialog(links, (chosen: Link[]) =>
        wrapWithRequestPageContentDelay(async () => {
            const titleOf = (link: Link) =>
                link.book.title || link.book.folderName || "";
            list.setAttribute(
                kBookIdsAttribute,
                chosen.map((link) => link.book.id).join(" "),
            );
            list.setAttribute(
                kBookTitlesAttribute,
                JSON.stringify(
                    Object.fromEntries(
                        chosen.map((link) => [link.book.id, titleOf(link)]),
                    ),
                ),
            );
            await writeListText(list, chosen.map(titleOf));
        }, "folioTocChooseBooks"),
    );
}
