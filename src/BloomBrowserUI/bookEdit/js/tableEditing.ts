import {
    attachTable,
    defaultCellContentsForEachType,
    detachTable,
    registerCellContentType,
    setDefaultCellContentTypeId,
    unregisterCellContentType,
    kTableCellContentChangedEvent,
} from "bloom-table";
// Edit-only table styles (selection highlight, boundary hints). These must NOT
// reach published output, so they are loaded here in the editing context rather
// than via basePage.less. This injects into the page iframe (this module is part
// of editablePageBundle). The structural/read-time styles come from
// bloom-table.css, which basePage.less inlines so they ship everywhere.
import "bloom-table/bloom-table-edit.css";
import { SetupImagesInContainer } from "./bloomImages";
import BloomField from "../bloomField/BloomField";

let contentTypesRegistered = false;

/**
 * Replace one of the library's built-in cell content types with the Bloom
 * equivalent, keeping the library's own name and icon so the cell menu still
 * looks and reads the way the library intends.
 */
function replaceCellContentType(
    id: string,
    templateHtml: string,
    regexToIdentify: RegExp,
    makeDefault: boolean,
): void {
    const libraryType = defaultCellContentsForEachType.find((c) => c.id === id);
    if (!libraryType) {
        throw new Error(
            `bloom-table has no cell content type "${id}" to replace`,
        );
    }
    registerCellContentType(
        { ...libraryType, templateHtml, regexToIdentify },
        { makeDefault },
    );
}

/** Register Bloom-specific cell content types with the bloom-table library. */
function ensureContentTypesRegistered(): void {
    if (contentTypesRegistered) return;
    contentTypesRegistered = true;

    // Text cells hold a bloom-translationGroup rather than the library's bare
    // contenteditable, so text in a table participates in Bloom's multilingual
    // system and its styles. TranslationGroupManager (C#) adds the per-language
    // bloom-editable children on the first page load after a new table is made.
    replaceCellContentType(
        "text",
        "<div class='bloom-translationGroup bloom-trailingElement normal-style'></div>",
        /bloom-translationGroup/,
        true,
    );

    // Image cells hold a bloom-canvas, so Bloom's image tooling (choose image,
    // crop, image description, canvas elements) works inside a cell. The markup
    // matches what origami's Image link creates.
    replaceCellContentType(
        "image",
        "<div class='bloom-canvas bloom-has-canvas-element bloom-leadingElement'>" +
            "<div class='bloom-canvas-element bloom-backgroundImage' style='width:100%;height:100%;'>" +
            "<div class='bloom-imageContainer'><img src='placeHolder.png'/></div>" +
            "</div></div>",
        /bloom-canvas/,
        false,
    );

    // The library's video cell is a plain HTML5 <video> pointing at a sample on
    // the web. Bloom video needs a bloom-videoContainer and the Sign Language
    // tool, so offering the library's version would only produce a broken cell.
    // A cell can still hold a nested table, which the library's own template
    // builds out of cells of the default (text) type registered above.
    unregisterCellContentType("video");

    setDefaultCellContentTypeId("text");
}

/** Handle a cell's content being (re)initialised. Attached via SetupTableEditing. */
function onTableCellContentChanged(e: Event): void {
    const custom = e as CustomEvent<{
        cell: HTMLElement;
        contentType: string;
    }>;
    const { cell, contentType } = custom.detail;
    if (contentType === "text") {
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
