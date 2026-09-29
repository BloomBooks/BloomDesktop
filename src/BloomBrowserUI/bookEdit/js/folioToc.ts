/// <reference path="../../typings/jquery.qtip.d.ts" />
// The table of contents pages of a folio (see the Folio template). Each page's list is a
// translation group whose data-folio-book-ids names some of the books the folio publishes, in
// order, by bookInstanceId, and whose data-folio-book-titles is a JSON object giving the last title
// seen for each of those ids, so a book that is later deleted can still be named. Its language 1 box is an ordinary text box, so it can be formatted;
// choosing books rewrites its text as one line per title. When the folio is published, Bloom
// rewrites it again with each book's first page number (FolioPdfPartsMaker.FillTablesOfContents).
//
// A bubble beside the list, headed "Folio", holds the buttons for choosing the books and for the
// folio's settings in Book and Page Settings. Each time the page is shown, any book the list names
// that is no longer in the collection is removed from it, and the bubble says so.

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

const kListClass = "bloom-folio-toc-list";
const kBookIdsAttribute = "data-folio-book-ids";
const kBookTitlesAttribute = "data-folio-book-titles";

export function setupFolioTocPages(container: HTMLElement) {
    const lists = Array.from(
        container.getElementsByClassName(kListClass),
    ) as HTMLElement[];
    if (lists.length === 0) return;
    const bubbles = lists.map((list) => addFolioBubble(list));
    wrapWithRequestPageContentDelay(async () => {
        const response = await getAsync("collections/books?realTitle=true");
        const collectionBooks = response.data as IBookInfo[];
        lists.forEach((list, i) =>
            removeBooksNoLongerInCollection(list, bubbles[i], collectionBooks),
        );
    }, "folioTocRemoveMissingBooks");
}

function removeBooksNoLongerInCollection(
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
        writeListText(
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

function setLocalizedText(element: HTMLElement, id: string, english: string) {
    theOneLocalizationManager.asyncGetText(id, english, "").done((text) => {
        element.textContent = text;
    });
}

/** The last title seen for each book a table of contents list names, by id. */
function getBookTitles(list: HTMLElement): Record<string, string> {
    return JSON.parse(list.getAttribute(kBookTitlesAttribute) || "{}");
}

/** Rewrite a list's text as one line per title. */
function writeListText(list: HTMLElement, titles: string[]) {
    // The language 1 box: the one the page shows as its main text.
    const box = list.querySelector(
        ".bloom-editable.bloom-content1",
    ) as HTMLElement;
    box.innerHTML = "";
    for (const title of titles) {
        const line = document.createElement("p");
        line.textContent = title;
        box.appendChild(line);
    }
}

/** The books a table of contents list names, in order. */
function getBookIds(list: HTMLElement): string[] {
    return (list.getAttribute(kBookIdsAttribute) ?? "")
        .split(" ")
        .filter((id) => id);
}

function chooseBooks(list: HTMLElement) {
    const page = list.closest(".bloom-page") as HTMLElement;
    const links: Link[] = getBookIds(list).map((id) => ({
        book: {
            id,
            thumbnail: `${window.location.origin}/bloom/api/collections/book/coverImage?book-id=${id}`,
        },
    }));
    getWorkspaceBundleExports().showFolioBooksDialog(
        page.id,
        links,
        (chosen: Link[]) => {
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
            writeListText(list, chosen.map(titleOf));
        },
    );
}
