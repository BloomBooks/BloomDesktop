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

/**
 * Have Bloom check the page being edited for overflow now, and wait until a check of the whole
 * page has run and found nothing overflowing. Fails, saying which boxes Bloom marked, if the page
 * stays marked as overflowing.
 *
 * Bloom checks a box 100 ms after a key comes up in it, and then checks every box it watches on
 * the page, each against itself and against the page's margins, and marks the page
 * `pageOverflows` or takes that mark off (OverflowChecker.AdjustSizeOrMarkOverflow,
 * CheckPageAncestorOverflow, UpdatePageOverflow). The equation boxes are not among the boxes Bloom
 * watches, since they lack bloom-visibility-code-on, but the heading is. So this presses Shift in
 * the heading, a key that types nothing, and puts `pageOverflows` on the page while the key is
 * down. Only that page-wide check takes the mark off, and only when no box it watches overflows,
 * so its going is the sign both that a check ran after the mark went on, which is after the last
 * change to the page, and what it found.
 */
export async function expectBloomFindsNoOverflowOnPage(
    page: Page,
    languageTag: string,
    what: string,
): Promise<void> {
    const kMark = "pageOverflows";
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
        (h, mark) => h.closest(".bloom-page")!.classList.add(mark),
        kMark,
    );
    await page.keyboard.up("Shift");
    // The boxes Bloom marked travel with the page's mark, so a failure shows them as they were.
    await expect
        .poll(
            () =>
                heading.evaluate((h, mark) => {
                    const p = h.closest(".bloom-page")!;
                    return {
                        pageMarkedOverflowing: p.classList.contains(mark),
                        boxesMarkedOverflowing: Array.from(
                            p.querySelectorAll(
                                ".overflow, .thisOverflowingParent",
                            ),
                        ).map((box) => box.className),
                    };
                }, kMark),
            {
                timeout: 15000,
                message: `Bloom never found ${what} free of overflow after Shift was pressed in its heading.`,
            },
        )
        .toEqual({ pageMarkedOverflowing: false, boxesMarkedOverflowing: [] });
}

/**
 * The problems of the page being edited that do not lie wholly inside the page's margins, by
 * their number on the page (1 for the first). Bloom's own overflow check measures only text boxes
 * it watches, which leaves out the problems' rows of pictures and equations.
 */
export async function getProblemsPastMargins(page: Page): Promise<number[]> {
    return editablePageFrame(page)
        .locator(".bloom-page .marginBox")
        .evaluate((marginBox) => {
            const margin = marginBox.getBoundingClientRect();
            const kTolerance = 1;
            return Array.from(
                marginBox.querySelectorAll(".problemSet .problem"),
            )
                .map((problem, index) => ({
                    r: problem.getBoundingClientRect(),
                    number: index + 1,
                }))
                .filter(
                    ({ r }) =>
                        r.top < margin.top - kTolerance ||
                        r.bottom > margin.bottom + kTolerance ||
                        r.left < margin.left - kTolerance ||
                        r.right > margin.right + kTolerance,
                )
                .map(({ number }) => number);
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
