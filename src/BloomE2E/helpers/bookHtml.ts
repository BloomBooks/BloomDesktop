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

/** What one page of a book holds, reduced to the things the copy-page test measures. */
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
     * The page's origami layout, as one string per split: the orientation and the two
     * component sizes. Comparing these says whether a custom layout survived the copy.
     */
    layout: string[];
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
 * Read the book at `bookFolder` and describe its numbered (non-front/back-matter) pages, in
 * order. `page` is used only as a DOM parser.
 */
export async function readBook(
    page: Page,
    bookFolder: string,
): Promise<IBookContents> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const document = new DOMParser().parseFromString(source, "text/html");
        const styleElement = document.querySelector(
            'style[title="userModifiedStyles"]',
        );
        const pages = [
            ...document.querySelectorAll("div.bloom-page.numberedPage"),
        ].map((pageDiv) => ({
            id: pageDiv.id,
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
        }));
        return {
            pages,
            userModifiedStyles: styleElement?.textContent ?? "",
        };
    }, html);
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
