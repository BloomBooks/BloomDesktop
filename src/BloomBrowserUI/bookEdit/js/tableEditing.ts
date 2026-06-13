import {
    attachTable,
    detachTable,
    registerCellContentType,
    setDefaultCellContentTypeId,
    kTableCellContentChangedEvent,
} from "bloom-table";
import { SetupImagesInContainer } from "./bloomImages";
import BloomField from "../bloomField/BloomField";

let contentTypesRegistered = false;

/** Register Bloom-specific cell content types with the bloom-table library. */
function ensureContentTypesRegistered(): void {
    if (contentTypesRegistered) return;
    contentTypesRegistered = true;

    // Default cell type: a bloom-translationGroup so text participates in
    // Bloom's multilingual system. TranslationGroupManager (C#) adds per-language
    // bloom-editable children on the first page load after a new table is created.
    registerCellContentType(
        {
            id: "translationGroup",
            englishName: "Text",
            templateHtml:
                "<div class='bloom-translationGroup bloom-trailingElement normal-style'></div>",
            regexToIdentify: /bloom-translationGroup/,
            icon: "",
        },
        { makeDefault: true },
    );

    // Image cell: a bloom-canvas so Bloom's image tooling works inside cells.
    registerCellContentType({
        id: "image",
        englishName: "Image",
        templateHtml:
            "<div class='bloom-canvas bloom-has-canvas-element bloom-leadingElement'>" +
            "<div class='bloom-canvas-element bloom-backgroundImage' style='width:100%;height:100%;'>" +
            "<div class='bloom-imageContainer'><img src='placeHolder.png'/></div>" +
            "</div></div>",
        regexToIdentify: /bloom-canvas/,
        icon: "",
    });

    setDefaultCellContentTypeId("translationGroup");
}

/** Handle a cell's content being (re)initialised. Attached via SetupTableEditing. */
function onTableCellContentChanged(e: Event): void {
    const custom = e as CustomEvent<{
        cell: HTMLElement;
        contentType: string;
    }>;
    const { cell, contentType } = custom.detail;
    if (contentType === "translationGroup") {
        // Wire any bloom-editable divs C# may have already populated.
        // If the translationGroup is empty, bloom-editables will appear on next page load.
        cell.querySelectorAll<HTMLElement>(".bloom-editable").forEach(
            (editable) => BloomField.ManageField(editable),
        );
    } else if (contentType === "image") {
        SetupImagesInContainer(cell);
    }
}

function attachSingleTable(tableDiv: HTMLElement): void {
    if (tableDiv.hasAttribute("data-table-attached")) return;
    tableDiv.setAttribute("data-table-attached", "1");
    attachTable(tableDiv);
}

/**
 * Wire table editing for the whole page. Called from SetupElements in
 * bloomEditing.ts on every page load. Attaches the cell-content-changed
 * event listener to the container and calls attachTable on every bloom-table
 * found inside it.
 */
export function SetupTableEditing(container: HTMLElement): void {
    ensureContentTypesRegistered();
    container.addEventListener(
        kTableCellContentChangedEvent,
        onTableCellContentChanged,
    );
    container
        .querySelectorAll<HTMLElement>(".bloom-table")
        .forEach((tableDiv) => attachSingleTable(tableDiv));
}

/**
 * Attach a single newly-created bloom-table element (called from
 * makeTableFieldClickHandler in origami.ts). The page-level event listener
 * installed by SetupTableEditing will already be on the page body, so no
 * new listener is needed here.
 */
export function AttachNewTable(tableDiv: HTMLElement): void {
    ensureContentTypesRegistered();
    attachSingleTable(tableDiv);
}

/**
 * Detach table editing from all bloom-table elements within `container`.
 * Called from removeEditingDebris in bloomEditing.ts before navigating away.
 */
export function TeardownTableEditing(container: HTMLElement): void {
    container.removeEventListener(
        kTableCellContentChangedEvent,
        onTableCellContentChanged,
    );
    container
        .querySelectorAll<HTMLElement>(".bloom-table[data-table-attached]")
        .forEach((tableDiv) => {
            tableDiv.removeAttribute("data-table-attached");
            detachTable(tableDiv);
        });
}
