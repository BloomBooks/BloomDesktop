// Read a book's saved .htm from disk and describe what each of its pages contains.
//
// Bloom writes the book to disk as it edits, so the file is the product's own record of what a
// page holds — a better subject for "did the copy preserve everything?" than the editing DOM,
// which shows only the one page on screen and decorates it with editing-only markup.
//
// Parsing happens INSIDE Bloom's own page, with DOMParser, rather than in Node: this package
// has no HTML parser among its dependencies, and adding one to read a file we already have is
// not worth it. Nothing is written back; the page is only borrowed as a parser.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Page } from "@playwright/test";

/**
 * What one page of a book holds, reduced to the things the tests measure: what a copy should have
 * preserved, and what a table should look like once it is on disk.
 */
export interface IPageContents {
    /** The page div's own id. Bloom gives a pasted page a fresh one. */
    id: string;
    /** The template the page came from, e.g. the Custom layout's id. */
    lineage: string;
    /** Every user-defined style class (`Foo-style`) on the page's editable text. */
    styleClasses: string[];
    /** The `src` of every image on the page, relative to the book folder. */
    imageSources: string[];
    /** The id of every Talking Book recorded span; each names a file in `audio/`. */
    audioSentenceIds: string[];
    /** The `src` of every video source on the page, `#t=` trim fragment included. */
    videoSources: string[];
    /**
     * The text of every editable box on the page, in document order, joined with " | ". Enough
     * to ask whether something typed reached the saved page.
     */
    text: string;
    /**
     * The page's origami layout, as one string per split: the orientation and the two
     * component sizes. Comparing these says whether a custom layout survived the copy.
     */
    layout: string[];
    /** Every table on the page, in document order, nested tables included. */
    tables: ITableDescription[];
    /**
     * Any editing-only markup the page still carries. This should always be empty: the table
     * library appends its chrome to the body and marks its own state on the cells, and Bloom
     * calls removeTableEditingArtifacts before it saves. Anything listed here is markup that
     * has been written into the book, where it will confuse a later load and reach the reader.
     */
    editingArtifacts: string[];
}

/**
 * One table as the saved file describes it. Everything here is read from the markup rather than
 * measured, because the file is what a later load, a publish, or a spreadsheet export will read.
 */
export interface ITableDescription {
    /** 0 for a table on the page, 1 for a table inside a cell of one, and so on. */
    depth: number;
    rows: number;
    columns: number;
    /** `data-column-widths` verbatim, e.g. "fill,fill" or "150px,fill". */
    columnWidths: string;
    /** `data-row-heights` verbatim. */
    rowHeights: string;
    /** What each of the table's own cells holds, in row-major order. */
    cellContentTypes: string[];
    /** The `src` of every image in the table's own cells, book-relative. */
    imageSources: string[];
    /** The `src` of every video in the table's own cells, trim fragment included. */
    videoSources: string[];
    /** How many of the table's cells are covered by a merge, so are not shown. */
    mergedAwayCellCount: number;
}

/** A book as read from disk: its pages, plus the user-defined styles its head carries. */
export interface IBookContents {
    pages: IPageContents[];
    /** The text of the book's `userModifiedStyles` block, where Bloom keeps custom styles. */
    userModifiedStyles: string;
}

/** The path of a book folder's own .htm file, which Bloom names after the folder. */
export function bookHtmlPath(bookFolder: string): string {
    return Path.join(bookFolder, `${Path.basename(bookFolder)}.htm`);
}

/**
 * The editing-only markup the table library and Bloom put on a table while it is being edited.
 * None of it should survive a save.
 */
export const kTableEditingMarkupSelectors = [
    "[data-table-attached]",
    ".cell--selected",
    ".table--selected",
    ".bloom-current-table",
    ".bloom-pointer-near",
    "[data-table-overlay]",
    ".bloom-sel-overlay",
    "[data-btable-anchor-name]",
    "[data-ui-active-row-index]",
    ".bloom-pulse-fill",
    ".bloom-pulse-border",
];

/**
 * Read the book at `bookFolder` and describe its numbered (non-front/back-matter) pages, in
 * order. `page` is used only as a DOM parser.
 */
export async function readBook(
    page: Page,
    bookFolder: string,
): Promise<IBookContents> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    const args = {
        source: html,
        artifactSelectors: kTableEditingMarkupSelectors,
    };
    return page.evaluate(({ source, artifactSelectors }) => {
        const document = new DOMParser().parseFromString(source, "text/html");
        const styleElement = document.querySelector(
            'style[title="userModifiedStyles"]',
        );
        const describeTables = (pageDiv: Element) =>
            [...pageDiv.querySelectorAll(".bloom-table")].map((tableDiv) => {
                // The table's OWN cells: a nested table's cells belong to that table, and are
                // described by its own entry in this list.
                const cells = [
                    ...tableDiv.querySelectorAll(":scope > .bloom-cell"),
                ];
                const sizes = (name: string) =>
                    tableDiv.getAttribute(name) ?? "";
                const countOf = (name: string) => {
                    const value = sizes(name);
                    return value === "" ? 0 : value.split(",").length;
                };
                const inOwnCells = (selector: string) =>
                    cells.flatMap((cellDiv) => [
                        ...cellDiv.querySelectorAll(selector),
                    ]);
                let depth = 0;
                for (
                    let ancestor = tableDiv.parentElement;
                    ancestor;
                    ancestor = ancestor.parentElement
                )
                    if (ancestor.classList.contains("bloom-table")) depth++;
                return {
                    depth,
                    rows: countOf("data-row-heights"),
                    columns: countOf("data-column-widths"),
                    columnWidths: sizes("data-column-widths"),
                    rowHeights: sizes("data-row-heights"),
                    cellContentTypes: cells.map(
                        (cellDiv) =>
                            cellDiv.getAttribute("data-content-type") ?? "",
                    ),
                    imageSources: inOwnCells("img").map(
                        (img) => img.getAttribute("src") ?? "",
                    ),
                    videoSources: inOwnCells("video source").map(
                        (source) => source.getAttribute("src") ?? "",
                    ),
                    mergedAwayCellCount: cells.filter((cellDiv) =>
                        cellDiv.classList.contains("bloom-skip"),
                    ).length,
                };
            });
        const pages = [
            ...document.querySelectorAll("div.bloom-page.numberedPage"),
        ].map((pageDiv) => ({
            id: pageDiv.id,
            text: [...pageDiv.querySelectorAll(".bloom-editable")]
                .map((editable) => (editable.textContent ?? "").trim())
                .join(" | "),
            lineage: pageDiv.getAttribute("data-pagelineage") ?? "",
            styleClasses: [
                ...new Set(
                    [...pageDiv.querySelectorAll(".bloom-editable")].flatMap(
                        (editable) =>
                            [...editable.classList].filter((c) =>
                                c.endsWith("-style"),
                            ),
                    ),
                ),
            ].sort(),
            imageSources: [...pageDiv.querySelectorAll("img")].map(
                (img) => img.getAttribute("src") ?? "",
            ),
            audioSentenceIds: [
                ...pageDiv.querySelectorAll(".audio-sentence"),
            ].map((span) => span.id),
            videoSources: [...pageDiv.querySelectorAll("video source")].map(
                (source) => source.getAttribute("src") ?? "",
            ),
            layout: [...pageDiv.querySelectorAll(".split-pane")].map(
                (split) => {
                    const orientation = split.classList.contains(
                        "horizontal-percent",
                    )
                        ? "horizontal"
                        : "vertical";
                    // The inline style is where origami records the split percentage.
                    const sizeOf = (position: string) =>
                        split
                            .querySelector(
                                `:scope > .split-pane-component.position-${position}`,
                            )
                            ?.getAttribute("style") ?? "";
                    const [first, second] =
                        orientation === "horizontal"
                            ? ["top", "bottom"]
                            : ["left", "right"];
                    return `${orientation} ${sizeOf(first)} | ${sizeOf(second)}`;
                },
            ),
            tables: describeTables(pageDiv),
            editingArtifacts: artifactSelectors.filter(
                (selector) => pageDiv.querySelector(selector) !== null,
            ),
        }));
        return {
            pages,
            userModifiedStyles: styleElement?.textContent ?? "",
        };
    }, args);
}

/**
 * Wait until the book on disk has `count` numbered pages, then return it. Bloom saves after the
 * edit, not with it, so a test that reads the file the moment a click returns can read the old
 * one. This polls the file rather than sleeping.
 */
export async function waitForBookWithPageCount(
    page: Page,
    bookFolder: string,
    count: number,
    timeoutMs = 60000,
): Promise<IBookContents> {
    // Return the very read that satisfied the check. A second read after the poll could catch
    // Bloom mid-write and hand back a different, half-written file.
    let book: IBookContents | undefined;
    await expect
        .poll(
            async () => {
                book = await readBook(page, bookFolder);
                return book.pages.length;
            },
            {
                timeout: timeoutMs,
                message:
                    `${bookHtmlPath(bookFolder)} never came to have ${count} numbered pages. ` +
                    `Bloom may not have saved the change.`,
            },
        )
        .toBe(count);
    return book!;
}

/** True if `relativePath` (as a page's markup names it) exists inside the book folder. */
export function bookFileExists(
    bookFolder: string,
    relativePath: string,
): boolean {
    // A video source carries a trim fragment, e.g. "video/x.mp4#t=0.0,2.0"; the file is the
    // part before it.
    const file = relativePath.split("#")[0];
    return fs.existsSync(Path.join(bookFolder, file));
}

/**
 * The front/back matter pack a book's HTML carries: the key before "-XMatter" in the one pack
 * stylesheet its head links, e.g. "Factory" or "Traditional". Bloom links exactly one such
 * stylesheet per book (XMatterHelper.GetStyleSheetFileName) and swaps it when it brings the book up
 * to date under a collection whose pack changed, so this says which pack the book was last brought
 * up to date with. It is the same answer for a book on disk and for the copy an upload sent to
 * Bloom Library, which is why it takes the HTML rather than a path. Throws, naming the stylesheets
 * it did find, when the HTML links no pack or more than one.
 */
export function xmatterPackInBookHtml(html: string): string {
    const hrefs = [...html.matchAll(/<link[^>]*\shref="([^"]+)"/g)].map(
        (match) => match[1],
    );
    const packs = hrefs.flatMap((href) => {
        const match = /^(.+)-XMatter\.css$/.exec(href);
        return match ? [match[1]] : [];
    });
    if (packs.length !== 1)
        throw new Error(
            `Expected the book's HTML to link exactly one <Pack>-XMatter.css, found ${packs.length}. ` +
                `Stylesheets linked: ${hrefs.join(", ")}`,
        );
    return packs[0];
}

/** The front/back matter pack of the book saved in `bookFolder`; see xmatterPackInBookHtml. */
export function readXmatterPackOfBook(bookFolder: string): string {
    return xmatterPackInBookHtml(
        fs.readFileSync(bookHtmlPath(bookFolder), "utf8"),
    );
}

/**
 * The book's .htm, with what carries no meaning taken out, so that two reads can be compared to
 * ask "did the book change?": each element's attributes in name order (HTML attribute order means
 * nothing, and Bloom's comparison ignores it too; see SafeXmlNode.GetXmlIgnoringAttributeOrder) and
 * the data div's entries in a fixed order. Anything else that differs is a real change.
 */
export async function readBookIgnoringOrder(
    page: Page,
    bookFolder: string,
): Promise<string> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const document = new DOMParser().parseFromString(source, "text/html");
        for (const element of [...document.querySelectorAll("*")]) {
            const attributes = [...element.attributes]
                .map((a) => [a.name, a.value])
                .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
            for (const [name] of attributes) element.removeAttribute(name);
            for (const [name, value] of attributes)
                element.setAttribute(name, value);
        }
        const dataDiv = document.getElementById("bloomDataDiv");
        if (dataDiv) {
            const entries = [...dataDiv.children].sort((a, b) => {
                const key = (e: Element) => e.outerHTML;
                return key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0;
            });
            for (const entry of entries) dataDiv.appendChild(entry);
        }
        return document.documentElement.outerHTML;
    }, html);
}

/**
 * Editing-only markup that has no business in a saved book, as Test Case ID 663 ("Clean Saved Book
 * HTML") asks: any mention of ckeditor, and the editor's other leftovers. One line per finding,
 * naming what was found and on what; an empty list means the book is clean.
 *
 * `allowedClasses` lets a test tolerate leftovers that are known and tracked elsewhere: the card
 * says to ignore BL-9992, whose classes are ui-draggable, ui-resizable, hoverUp and
 * ui-audioCurrent.
 */
export async function findEditorLeftovers(
    page: Page,
    bookFolder: string,
    allowedClasses: string[] = [],
): Promise<string[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, allowed }) => {
            const findings: string[] = [];
            if (/ckeditor/i.test(source)) findings.push('the text "ckeditor"');
            const document = new DOMParser().parseFromString(
                source,
                "text/html",
            );
            const describe = (e: Element) =>
                `<${e.tagName.toLowerCase()}${e.id ? ` id="${e.id}"` : ""} class="${e.getAttribute("class") ?? ""}">`;
            const leftoverClasses = [
                "bloom-ui",
                "ui-resizable-handle",
                "cke",
                "qtip",
                "nicescroll-rails",
                "nicescroll-cursors",
                "bloom-focusedCanvasElement",
                "ui-draggable",
                "ui-resizable",
                "hoverUp",
                "ui-audioCurrent",
            ].filter((c) => !allowed.includes(c));
            for (const element of [...document.querySelectorAll("*")]) {
                for (const c of element.classList) {
                    if (leftoverClasses.includes(c) || c.startsWith("cke_"))
                        findings.push(`class ${c} on ${describe(element)}`);
                }
                if (element.hasAttribute("data-hasqtip"))
                    findings.push(`data-hasqtip on ${describe(element)}`);
                if (
                    element
                        .getAttribute("aria-describedby")
                        ?.startsWith("qtip-")
                )
                    findings.push(
                        `a qTip aria-describedby on ${describe(element)}`,
                    );
            }
            for (const id of ["measureTextDiv"]) {
                if (document.getElementById(id))
                    findings.push(`the #${id} element`);
            }
            for (const element of [
                ...document.querySelectorAll("svg.comical-generated [id]"),
            ])
                findings.push(
                    `a regenerated id on Comical's SVG: ${element.id}`,
                );
            return findings;
        },
        { source: html, allowed: allowedClasses },
    );
}
