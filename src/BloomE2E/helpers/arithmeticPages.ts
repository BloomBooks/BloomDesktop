// Read and fill the pages of Bloom's Arithmetic Template (src/content/templates/template books/
// Arithmetic Template). Each of its pages has a heading and a column of problems; a problem is a
// row of two picture boxes over a row of three equation boxes, an equals sign and an empty box
// for the answer. The equation boxes are language-neutral (lang="*") text boxes that stand on
// their own, outside any translation group, so the translation-group helpers in bookMaking.ts
// cannot reach them.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Locator, type Page } from "@playwright/test";
import { bookHtmlPath } from "./bookHtml";
import { editablePageFrame } from "./bookMaking";
import { pressKey } from "./keys";

/**
 * The translation group of a page's heading, for the translation-group helpers. The template puts
 * the heading's style on the group's text boxes, not on the group.
 */
export const kArithmeticHeadingGroup =
    ".bloom-translationGroup:has(> .bloom-editable.Heading1-style)";

/** The problems of the page being edited, in order. */
export function arithmeticProblems(page: Page): Locator {
    return editablePageFrame(page).locator(".bloom-page .problemSet .problem");
}

/** The picture boxes of one problem, in order. */
export function problemPictureBoxes(problem: Locator): Locator {
    return problem.locator(".imageRow > .bloom-canvas");
}

/** The equation boxes of one problem, in order. */
export function problemEquationBoxes(problem: Locator): Locator {
    return problem.locator(".numberRow > .bloom-editable");
}

/** What one problem is made of. */
export interface IProblemLayout {
    pictureBoxes: number;
    equationBoxes: number;
    /** Whether the problem has its equals sign and its empty box for the answer. */
    hasEqualsAndAnswerBox: boolean;
}

/** The selector-side half of describing a problem: run in a browser document. */
function describeProblemsIn(root: ParentNode): IProblemLayout[] {
    return Array.from(root.querySelectorAll(".problemSet .problem")).map(
        (problem) => ({
            pictureBoxes: problem.querySelectorAll(".imageRow > .bloom-canvas")
                .length,
            equationBoxes: problem.querySelectorAll(
                ".numberRow > .bloom-editable",
            ).length,
            hasEqualsAndAnswerBox:
                problem.querySelector(".numberRow > .equals") !== null &&
                problem.querySelector(".numberRow > .fillInBox") !== null,
        }),
    );
}

/**
 * The problems of each page of the one HTML file in `folder` (a template's own folder), by page
 * id. `page` is used only as a DOM parser.
 */
export async function readArithmeticLayoutsOnDisk(
    page: Page,
    folder: string,
): Promise<Record<string, IProblemLayout[]>> {
    const htmlFiles = fs
        .readdirSync(folder)
        .filter((name) => /\.html?$/i.test(name));
    if (htmlFiles.length !== 1)
        throw new Error(
            `Expected one HTML file in ${folder}, found: ${htmlFiles.join(", ") || "(none)"}.`,
        );
    const html = fs.readFileSync(Path.join(folder, htmlFiles[0]), "utf8");
    return page.evaluate(
        ({ source, describe }) => {
            // The describing function travels as text: page.evaluate cannot pass a function.
            const describeProblems = new Function(`return (${describe})`)() as (
                root: ParentNode,
            ) => IProblemLayout[];
            const dom = new DOMParser().parseFromString(source, "text/html");
            const result: Record<string, IProblemLayout[]> = {};
            for (const p of Array.from(dom.querySelectorAll(".bloom-page[id]")))
                result[p.id] = describeProblems(p);
            return result;
        },
        { source: html, describe: describeProblemsIn.toString() },
    );
}

/** The problems of the page being edited, as it shows them. */
export async function getArithmeticLayoutShown(
    page: Page,
): Promise<IProblemLayout[]> {
    return editablePageFrame(page)
        .locator(".bloom-page")
        .evaluate(
            (pageElement, describe) =>
                (
                    new Function(`return (${describe})`)() as (
                        root: ParentNode,
                    ) => IProblemLayout[]
                )(pageElement),
            describeProblemsIn.toString(),
        );
}

/**
 * Press Tab, as a person moving through the page does, until `box` has the focus. Fails if it
 * has not after `maxPresses`, naming where the focus went instead.
 */
export async function tabInto(
    page: Page,
    box: Locator,
    what: string,
    maxPresses = 6,
): Promise<void> {
    const focused = () =>
        box.evaluate((b) => b === b.ownerDocument.activeElement);
    const visited: string[] = [];
    for (let i = 0; i < maxPresses && !(await focused()); i++) {
        await pressKey(page, "Tab");
        visited.push(
            await editablePageFrame(page).evaluate(() => {
                const a = document.activeElement as HTMLElement | null;
                if (!a) return "(nothing)";
                return `${a.tagName.toLowerCase()}.${Array.from(a.classList).join(".")}`;
            }),
        );
    }
    expect(
        await focused(),
        `Pressing Tab never took the focus into ${what}; it went to: ${visited.join(" > ")}`,
    ).toBe(true);
}

/** What Bloom's overflow check of the page being edited found. */
export interface IPageOverflowCheck {
    /** Each box Bloom marked as overflowing (`overflow` or `thisOverflowingParent`), by its classes. */
    markedBoxes: string[];
    /** Whether Bloom marked the page as having too much on it (`pageOverflows`). */
    pageOverflows: boolean;
}

/**
 * Have Bloom check the page being edited for overflow now, through its heading, and return what
 * the check found once it has run.
 *
 * Bloom checks a box 100 ms after a key comes up in it, and that check also checks every box on
 * the page against the page's margins and marks the page (OverflowChecker.AdjustSizeOrMarkOverflow,
 * AddOverflowHandlers). The equation boxes are not among the boxes Bloom checks, since they lack
 * bloom-visibility-code-on, but the heading is. So this presses Shift in the heading, a key that
 * types nothing. The sign that the check ran is a mark put on the heading's translation group
 * while the key is down, `childOverflowingThis`: the check of the heading starts by taking that
 * mark off every element around it (AdjustSizeAndMarkSelfOverflow), and puts it back only on an
 * element the heading runs past.
 */
export async function checkPageForOverflow(
    page: Page,
    languageTag: string,
): Promise<IPageOverflowCheck> {
    const kMark = "childOverflowingThis";
    const heading = editablePageFrame(page)
        .locator(
            `${kArithmeticHeadingGroup} > .bloom-editable[lang="${languageTag}"]`,
        )
        .first();
    await heading.click();
    await expect(
        heading,
        "The heading does not have the focus, so Shift would go elsewhere.",
    ).toBeFocused({ timeout: 15000 });
    await page.keyboard.down("Shift");
    await heading.evaluate(
        (h, mark) => h.parentElement!.classList.add(mark),
        kMark,
    );
    await page.keyboard.up("Shift");
    await expect
        .poll(
            () =>
                heading.evaluate(
                    (h, mark) => h.parentElement!.classList.contains(mark),
                    kMark,
                ),
            {
                timeout: 15000,
                message:
                    "Bloom did not check the heading for overflow after Shift was pressed in it, or found the heading running past its group.",
            },
        )
        .toBe(false);
    return heading.evaluate((h) => {
        const p = h.closest(".bloom-page")!;
        return {
            markedBoxes: Array.from(
                p.querySelectorAll(".overflow, .thisOverflowingParent"),
            ).map((box) => box.className),
            pageOverflows: p.classList.contains("pageOverflows"),
        };
    });
}

/**
 * How an equation box holds its text: the text of each of its paragraphs, and any text that sits
 * directly in the box outside a paragraph. Bloom keeps a box's text in paragraphs; text outside
 * one, with an empty paragraph after it, is what made Bloom mark a box as overflowing (BL-9012).
 */
export interface IEquationBoxText {
    paragraphs: string[];
    textOutsideParagraphs: string;
}

/** How one text box on the page being edited holds its text. */
export async function getEquationBoxText(
    box: Locator,
): Promise<IEquationBoxText> {
    return box.evaluate((element) => ({
        paragraphs: Array.from(element.querySelectorAll("p")).map(
            (p) => p.textContent ?? "",
        ),
        textOutsideParagraphs: Array.from(element.childNodes)
            .filter((n) => n.nodeType === Node.TEXT_NODE)
            .map((n) => n.textContent ?? "")
            .join("")
            // CKEditor's caret-holding filler is not text the person typed.
            .replace(/[\s​]/g, ""),
    }));
}

/** What one problem of a saved page holds. */
export interface ISavedProblem {
    /** The file name of the picture in each picture box; "placeHolder.png" for an empty one. */
    pictures: string[];
    /** The text of each equation box. */
    equations: string[];
}

/** What one page of the saved book holds, for a page made from the Arithmetic Template. */
export interface ISavedArithmeticPage {
    /** The text of the heading in `languageTag`. */
    heading: string;
    problems: ISavedProblem[];
}

/**
 * The headings, pictures and equations of the pages of the book in `bookFolder` with these ids,
 * as saved on disk. `page` is used only as a DOM parser.
 */
export async function readSavedArithmeticPages(
    page: Page,
    bookFolder: string,
    pageIds: string[],
    languageTag: string,
): Promise<Record<string, ISavedArithmeticPage>> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, ids, lang }) => {
            const dom = new DOMParser().parseFromString(source, "text/html");
            const result: Record<string, ISavedArithmeticPage> = {};
            for (const id of ids) {
                const p = dom.querySelector(`.bloom-page[id="${id}"]`);
                if (!p) continue;
                result[id] = {
                    heading:
                        p
                            .querySelector(
                                `.bloom-translationGroup > .bloom-editable.Heading1-style[lang="${lang}"]`,
                            )
                            ?.textContent?.trim() ?? "",
                    problems: Array.from(
                        p.querySelectorAll(".problemSet .problem"),
                    ).map((problem) => ({
                        pictures: Array.from(
                            problem.querySelectorAll(
                                ".imageRow > .bloom-canvas .bloom-backgroundImage img",
                            ),
                        ).map((img) =>
                            decodeURIComponent(
                                (img.getAttribute("src") ?? "")
                                    .split("?")[0]
                                    .split("/")
                                    .pop() ?? "",
                            ),
                        ),
                        equations: Array.from(
                            problem.querySelectorAll(
                                ".numberRow > .bloom-editable",
                            ),
                        ).map((box) => box.textContent?.trim() ?? ""),
                    })),
                };
            }
            return result;
        },
        { source: html, ids: pageIds, lang: languageTag },
    );
}
