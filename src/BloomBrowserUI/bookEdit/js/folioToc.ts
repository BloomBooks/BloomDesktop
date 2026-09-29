/// <reference path="../../typings/jquery.qtip.d.ts" />
// The table of contents pages of a folio (see the Folio template). Each page's list is a
// translation group whose data-folio-book-ids names some of the books the folio publishes, in
// order, by bookInstanceId. Its language 1 box is an ordinary text box, so it can be formatted;
// choosing books rewrites its text as one line per title. When the folio is published, Bloom
// rewrites it again with each book's first page number (FolioPdfPartsMaker.FillTablesOfContents).
//
// A bubble beside the list, headed "Folio", holds the buttons for choosing the books and for the
// folio's settings in Book and Page Settings.

import $ from "jquery";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { getWorkspaceBundleExports } from "./workspaceFrames";
import { Link } from "../../react_components/BookGridSetup/BookLinkTypes";
import bloomQtipUtils from "./bloomQtipUtils";
import folioSvg from "../../images/folio.svg?raw";

const kListClass = "bloom-folio-toc-list";
const kBookIdsAttribute = "data-folio-book-ids";

export function setupFolioTocPages(container: HTMLElement) {
    const lists = Array.from(
        container.getElementsByClassName(kListClass),
    ) as HTMLElement[];
    for (const list of lists) addFolioBubble(list);
}

// The bubble is a qtip in the page-scaling container, like the hint bubbles, so it is never part
// of the page. It always shows.
function addFolioBubble(list: HTMLElement) {
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
}

function setLocalizedText(element: HTMLElement, id: string, english: string) {
    theOneLocalizationManager.asyncGetText(id, english, "").done((text) => {
        element.textContent = text;
    });
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
            list.setAttribute(
                kBookIdsAttribute,
                chosen.map((link) => link.book.id).join(" "),
            );
            // The language 1 box: the one the page shows as its main text.
            const box = list.querySelector(
                ".bloom-editable.bloom-content1",
            ) as HTMLElement;
            box.innerHTML = "";
            for (const link of chosen) {
                const line = document.createElement("p");
                line.textContent =
                    link.book.title || link.book.folderName || "";
                box.appendChild(line);
            }
        },
    );
}
