// ================================================================================================
// FIRST USE: appearance-migration-branded-books.spec.ts (Test Case ID 93). Bloom 6.0 replaced a
// book's own layout CSS (customBookStyles.css, or the collection's customCollectionStyles.css)
// with the appearance system: a theme and settings in appearance.json. For the custom CSS files
// that many books share, Bloom knows a theme that does the same job (src/content/
// appearanceMigrations), and swaps one for the other the first time it brings such a book up to
// date. These helpers find which migration a book's CSS matches, make a copy of a book that Bloom
// cannot migrate (so it keeps its old CSS under the legacy theme), and measure where the boxes of
// every page sit, so a test can compare the migrated book with the book under its old CSS.
// ================================================================================================

import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, type FrameLocator } from "@playwright/test";

/** The appearance migrations in the source tree, which the build copies for Bloom to read. */
export const kAppearanceMigrationsFolder = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "../../content/appearanceMigrations",
);

/** The theme Bloom gives a book whose custom CSS it neither knows nor can live with. */
export const kLegacyTheme = "legacy-5-6";

/** The custom CSS files Bloom looks at when it first moves a book to the appearance system. */
export type CustomCssFile =
    | "customBookStyles.css"
    | "customCollectionStyles.css";

/** One of Bloom's appearance migrations, as its folder holds it. */
export interface IAppearanceMigration {
    /** The migration's folder name, e.g. "efl-ebook-1". */
    name: string;
    /** The settings it gives the book: its appearance.json, without the comment lines. */
    settings: Record<string, unknown>;
    /** The CSS it puts in the book as customBookStyles2.css. */
    replacementCss: string;
}

/**
 * The checksum Bloom keys a custom CSS file by (AppearanceMigrator.GetChecksum): the MD5 of the
 * file without the boilerplate comment that nearly all such files start with, and without
 * leading and trailing white space.
 */
export function appearanceChecksum(css: string): string {
    const boilerplate =
        /\/\* *Some books may need control over aspects of layout that cannot yet be adjusted(.|[\r\n])*?\*\//g;
    return crypto
        .createHash("md5")
        .update(css.replace(boilerplate, "").trim(), "utf8")
        .digest("hex");
}

/**
 * Bloom's appearance.json files are JSON with "//" comment lines and trailing commas; read one as
 * Bloom does.
 */
export function readAppearanceJson(path: string): Record<string, unknown> {
    const text = fs
        .readFileSync(path, "utf8")
        .replace(/^﻿/, "")
        .split(/\r?\n/)
        .filter((line) => !line.trim().startsWith("//"))
        .join("\n")
        .replace(/,(\s*})/g, "$1");
    return JSON.parse(text) as Record<string, unknown>;
}

/**
 * The migration Bloom applies to a book whose custom CSS file holds `css`, or undefined when
 * Bloom knows none for it.
 */
export function findAppearanceMigration(
    css: string,
): IAppearanceMigration | undefined {
    const checksum = appearanceChecksum(css);
    for (const name of fs.readdirSync(kAppearanceMigrationsFolder)) {
        const jsonPath = Path.join(
            kAppearanceMigrationsFolder,
            name,
            "appearance.json",
        );
        if (!fs.existsSync(jsonPath)) continue;
        const match = / with checksum ([a-f0-9, ]*)/.exec(
            fs.readFileSync(jsonPath, "utf8"),
        );
        if (!match?.[1].split(",").some((c) => c.trim() === checksum)) continue;
        return {
            name,
            settings: readAppearanceJson(jsonPath),
            replacementCss: fs.readFileSync(
                Path.join(
                    kAppearanceMigrationsFolder,
                    name,
                    "customBookStyles.css",
                ),
                "utf8",
            ),
        };
    }
    return undefined;
}

/** The book's maintenance level, as its .htm file's meta element says. */
export function readMaintenanceLevel(bookFolder: string): number {
    const htm = fs
        .readdirSync(bookFolder)
        .find((file) => file.toLowerCase().endsWith(".htm"));
    if (!htm) throw new Error(`${bookFolder} has no .htm file.`);
    const match = /<meta name="maintenanceLevel" content="(\d+)"/.exec(
        fs.readFileSync(Path.join(bookFolder, htm), "utf8"),
    );
    return match ? Number(match[1]) : 0;
}

/**
 * Copy the book in `bookFolder` to a new folder beside it named `newName`, with a new book id, and
 * add a comment to its `cssFile`. The comment changes nothing on the page but changes the file's
 * checksum, so Bloom no longer recognizes the CSS and keeps it under the legacy theme: the copy
 * shows the book as its old CSS lays it out. Returns the copy's folder.
 */
export function copyBookKeepingItsOldCss(
    bookFolder: string,
    newName: string,
    cssFile: CustomCssFile,
): string {
    const copy = Path.join(Path.dirname(bookFolder), newName);
    fs.cpSync(bookFolder, copy, { recursive: true });
    const oldName = Path.basename(bookFolder);
    fs.renameSync(
        Path.join(copy, `${oldName}.htm`),
        Path.join(copy, `${newName}.htm`),
    );
    const metaPath = Path.join(copy, "meta.json");
    const meta = JSON.parse(fs.readFileSync(metaPath, "utf8"));
    const newId = crypto.randomUUID();
    const oldId = meta.bookInstanceId as string;
    meta.bookInstanceId = newId;
    fs.writeFileSync(metaPath, JSON.stringify(meta));
    const htmPath = Path.join(copy, `${newName}.htm`);
    fs.writeFileSync(
        htmPath,
        fs.readFileSync(htmPath, "utf8").split(oldId).join(newId),
    );
    const cssPath = Path.join(copy, cssFile);
    fs.appendFileSync(
        cssPath,
        "\n/* A copy that keeps this CSS, for comparison (Test Case ID 93). */\n",
    );
    return copy;
}

/** A box on a page, in millimetres from the page's top left corner. */
export interface IBoxOnPage {
    left: number;
    top: number;
    width: number;
    height: number;
}

/** Where the boxes of one page sit. */
export interface IPageLayout {
    /** The page's id. */
    id: string;
    /** The page's size class and kind, e.g. "A5Portrait cover". */
    label: string;
    /** The page's size in millimetres. */
    width: number;
    height: number;
    /**
     * The page's width on screen, in CSS pixels. The millimetre numbers are scaled by the page's
     * nominal width, so they cannot show a page drawn larger or smaller as a whole; this can,
     * when compared with the same book's page shown in the same place.
     */
    pixelWidth: number;
    /** The page's margin box: the area inside its margins. */
    marginBox?: IBoxOnPage;
    /** Each picture's slot, in page order. */
    pictures: IBoxOnPage[];
    /**
     * The file name of each picture the page's picture slots show, loaded, in page order. A slot
     * can show while CSS hides its picture, so this is counted apart from `pictures`.
     */
    shownPictures: string[];
    /** Each text group that shows text, in page order. */
    textGroups: IBoxOnPage[];
    /** The start of the text of each shown text box whose text does not fit in it. */
    overflowingTexts: string[];
    /** The file name of each shown picture that did not load. */
    brokenPictures: string[];
    /** The file name of each branding picture (img.branding) the page shows, loaded. */
    brandingPictures: string[];
}

/**
 * Where the boxes of each page of the book shown in `frame` sit, in millimetres from the page's
 * corner. Every number is a ratio of distances on screen scaled by the page's width in
 * millimetres (read from its size class), so it does not depend on the frame's zoom.
 */
export async function readPageLayouts(
    frame: FrameLocator,
): Promise<IPageLayout[]> {
    return frame.locator("body").evaluate((body) => {
        // Page sizes in millimetres, by the size part of the page's class (see basePage.less).
        const kSizes: Record<string, [number, number]> = {
            A3: [297, 420],
            A4: [210, 297],
            A5: [148, 210],
            A6: [105, 148],
            B5: [176, 250],
            Letter: [215.9, 279.4],
            HalfLetter: [139.7, 215.9],
            QuarterLetter: [107.95, 139.7],
            Legal: [215.9, 355.6],
            Device16x9: [100, 177.777778],
            Cm13: [130, 130],
            USComic: [168.275, 260.35],
            Size6x9: [152.4, 228.6],
            PictureStoryLandscape: [215.9, 279.4],
        };
        const pages = Array.from(
            body.ownerDocument.querySelectorAll<HTMLElement>(".bloom-page"),
        );
        return pages.map((p) => {
            const sizeClass =
                Array.from(p.classList).find((c) =>
                    /(Portrait|Landscape)$/.test(c),
                ) ?? "";
            const size = /^(.*?)(Portrait|Landscape)$/.exec(sizeClass);
            const known = size ? kSizes[size[1]] : undefined;
            const pageRect = p.getBoundingClientRect();
            let mmWide = pageRect.width;
            if (known) mmWide = size![2] === "Portrait" ? known[0] : known[1];
            const scale = mmWide / pageRect.width;
            const shown = (e: Element) => {
                const r = e.getBoundingClientRect();
                return (
                    r.width > 0 &&
                    r.height > 0 &&
                    e.checkVisibility({
                        visibilityProperty: true,
                        opacityProperty: true,
                    })
                );
            };
            const box = (e: Element) => {
                const r = e.getBoundingClientRect();
                const round = (n: number) => Math.round(n * 100) / 100;
                return {
                    left: round((r.left - pageRect.left) * scale),
                    top: round((r.top - pageRect.top) * scale),
                    width: round(r.width * scale),
                    height: round(r.height * scale),
                };
            };
            const marginBox = p.querySelector(".marginBox");
            const slots = Array.from(
                p.querySelectorAll(".bloom-canvas, .bloom-imageContainer"),
            )
                .filter((e) => !e.parentElement?.closest(".bloom-canvas"))
                .filter(shown);
            return {
                id: p.id,
                label: `${sizeClass} ${p.getAttribute("data-xmatter-page") ?? p.getAttribute("data-pagelineage")?.split(";").at(-1) ?? ""}`,
                width: Math.round(pageRect.width * scale * 100) / 100,
                height: Math.round(pageRect.height * scale * 100) / 100,
                pixelWidth: Math.round(pageRect.width * 100) / 100,
                marginBox: marginBox ? box(marginBox) : undefined,
                pictures: slots.map(box),
                shownPictures: slots.flatMap((slot) =>
                    Array.from(slot.querySelectorAll("img"))
                        .filter((img) => shown(img) && img.naturalWidth > 0)
                        .map((img) => img.getAttribute("src") ?? ""),
                ),
                textGroups: Array.from(
                    p.querySelectorAll(".bloom-translationGroup"),
                )
                    .filter(
                        (g) =>
                            shown(g) &&
                            Array.from(
                                g.querySelectorAll(
                                    ".bloom-editable.bloom-visibility-code-on",
                                ),
                            ).some((e) => (e.textContent ?? "").trim() !== ""),
                    )
                    .map(box),
                // A box whose text needs more room than the box has: what Bloom's Edit tab marks
                // as overflowing (a pixel of rounding allowed).
                overflowingTexts: Array.from(
                    p.querySelectorAll<HTMLElement>(
                        ".bloom-editable.bloom-visibility-code-on",
                    ),
                )
                    .filter(
                        (e) =>
                            shown(e) &&
                            (e.textContent ?? "").trim() !== "" &&
                            (e.scrollHeight > e.clientHeight + 1 ||
                                e.scrollWidth > e.clientWidth + 1),
                    )
                    .map((e) => (e.textContent ?? "").trim().slice(0, 40)),
                brokenPictures: Array.from(p.querySelectorAll("img"))
                    .filter(
                        (img) =>
                            shown(img) &&
                            img.complete &&
                            img.naturalWidth === 0,
                    )
                    .map((img) => img.getAttribute("src") ?? ""),
                brandingPictures: Array.from(
                    p.querySelectorAll<HTMLImageElement>("img.branding"),
                )
                    .filter((img) => shown(img) && img.naturalWidth > 0)
                    .map((img) => img.getAttribute("src") ?? ""),
            };
        });
    });
}

/**
 * Wait until the preview `frame` holds the document of the book with id `bookId` and shows all
 * `pageCount` of its pages, with its fonts and every picture loaded (or failed), so that what
 * readPageLayouts measures is that book's finished layout.
 *
 * The Collections tab keeps one preview frame and changes its address when another book is
 * selected (CollectionsTabBookPane.tsx), so for a moment the frame's address names the new book
 * while its document is still the old one. Two copies of a book have the same page ids, so the
 * pages cannot tell them apart; the address of the document itself can.
 */
export async function waitForBookLaidOut(
    frame: FrameLocator,
    bookId: string,
    pageCount: number,
): Promise<void> {
    await expect
        .poll(
            () =>
                frame
                    .locator("body")
                    .evaluate((body) => body.ownerDocument.location.href),
            {
                message: `the preview should hold the document of book ${bookId}`,
                timeout: 60000,
            },
        )
        .toContain(`dummy=${bookId}`);
    await frame
        .locator(".bloom-page")
        .nth(pageCount - 1)
        .waitFor({ timeout: 60000 });
    await frame.locator("body").evaluate(async (body) => {
        const doc = body.ownerDocument;
        await doc.fonts.ready;
        await Promise.all(
            Array.from(doc.images).map((img) =>
                img.complete
                    ? undefined
                    : new Promise((done) => {
                          img.addEventListener("load", done, { once: true });
                          img.addEventListener("error", done, { once: true });
                      }),
            ),
        );
    });
}

/** How many pages the book in `bookFolder` has, as its saved .htm file has them. */
export function countPagesOnDisk(bookFolder: string): number {
    const htm = fs
        .readdirSync(bookFolder)
        .find((file) => file.toLowerCase().endsWith(".htm"));
    if (!htm) throw new Error(`${bookFolder} has no .htm file.`);
    return (
        fs
            .readFileSync(Path.join(bookFolder, htm), "utf8")
            .match(/class="bloom-page[ "]/g) ?? []
    ).length;
}
