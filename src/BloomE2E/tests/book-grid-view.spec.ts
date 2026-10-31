// Seeing the rest of the book while editing a page: the "All pages" segment of the page view chooser
// in the Edit tab's top bar lays the book out in spreads around the page being edited, the way the printed book is read.
// Only the page being edited is editable; clicking any other page makes that one the page being
// edited, in the same place on screen. Pages far off screen are not rendered, so a long book stays
// cheap.
//
// Automates the manual test "Show All Pages While Editing" (Test Case ID 835).
//
// The tests are serial because each one starts from the book the one before it left behind.

import * as fs from "node:fs";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
    type IBookPage,
} from "../helpers/bookMaking";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    choosePageView,
    clickPageInGrid,
    getPagePlacement,
    isPageChangeUnderway,
    scrollPageTopAboveView,
    waitForClickedPageToSettle,
} from "../helpers/pageView";
import { realClick } from "../helpers/realClick";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "book-grid-view", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

let bookFolder: string;

/** The text typed on the content page with this index. */
const textOfPage = (index: number) => `Words on content page ${index + 1}`;

/** The cell of the grid that holds the page with this id. */
function cell(page: Page, pageId: string) {
    return editablePageFrame(page).locator(
        `.bloom-book-grid-cell[data-page-id="${pageId}"]`,
    );
}

/** The page that shares a spread with the page at `index`: the cover stands alone on the right. */
function facingIndex(index: number): number {
    // The cover is page 0, on the right of the first spread, so odd indexes are left pages.
    return index % 2 === 1 ? index + 1 : index - 1;
}

/** Which segment of the page view chooser is chosen, checking that exactly one is. */
async function chosenPageView(page: Page): Promise<"one" | "all"> {
    const one = await page
        .getByTestId("view-one-page")
        .getAttribute("aria-pressed");
    const all = await page
        .getByTestId("view-all-pages")
        .getAttribute("aria-pressed");
    expect(
        [one, all].sort(),
        "Exactly one page view button should be chosen.",
    ).toEqual(["false", "true"]);
    return all === "true" ? "all" : "one";
}

/**
 * Where the page list sits in the Edit tab, and whether any container of the shell around it has
 * been scrolled. Nothing the page frame does may shove the page list aside.
 */
async function pageListPlacement(page: Page) {
    return page.evaluate(() => {
        const list = document.getElementById("pageList")!;
        let scrolled = "";
        for (
            let e: HTMLElement | null = list.parentElement;
            e;
            e = e.parentElement
        ) {
            if (e.scrollLeft !== 0 || e.scrollTop !== 0)
                scrolled += `${e.tagName}#${e.id}.${e.className} (${e.scrollLeft},${e.scrollTop}) `;
        }
        return {
            left: Math.round(list.getBoundingClientRect().left),
            scrolled,
        };
    });
}

/**
 * Where the page being edited is in its frame's viewport, and where each bubble (Bloom's hint and
 * source bubbles, which are qTip popups) showing on it is.
 */
async function pageAndBubbles(page: Page) {
    return editablePageFrame(page).evaluate(() => {
        const p = document
            .querySelector(".bloom-page")!
            .getBoundingClientRect();
        const bubbles = Array.from(
            document.querySelectorAll<HTMLElement>(".qtip"),
        )
            .filter(
                (q) =>
                    getComputedStyle(q).display !== "none" &&
                    q.getBoundingClientRect().height > 0,
            )
            .map((q) => {
                const r = q.getBoundingClientRect();
                const target = document.querySelector(
                    `[aria-describedby="${q.id}"]`,
                );
                return {
                    top: Math.round(r.top),
                    left: Math.round(r.left),
                    what: `${q.className} on ${target ? `${target.tagName}.${target.className}` : "nothing"}`,
                };
            });
        return {
            page: {
                top: Math.round(p.top),
                bottom: Math.round(p.bottom),
                left: Math.round(p.left),
                right: Math.round(p.right),
            },
            bubbles,
            viewportHeight: window.innerHeight,
        };
    });
}

/** Check the page is in view and every bubble is beside it, not left somewhere else. */
async function expectBubblesBesidePage(page: Page, when: string) {
    const { page: p, bubbles, viewportHeight } = await pageAndBubbles(page);
    expect(p.top, `${when}: the page is below the view`).toBeLessThan(
        viewportHeight,
    );
    expect(p.bottom, `${when}: the page is above the view`).toBeGreaterThan(0);
    expect(
        bubbles.length,
        `${when}: sanity check, the Credits page should be showing bubbles`,
    ).toBeGreaterThan(0);
    bubbles.forEach((b, i) => {
        expect(
            b.top >= p.top - 150 && b.top <= p.bottom + 150,
            `${when}: bubble ${i} (${b.what}) is at y=${b.top}, but the page runs from ${p.top} to ${p.bottom}`,
        ).toBe(true);
        expect(
            b.left >= p.left - 400 && b.left <= p.right + 400,
            `${when}: bubble ${i} (${b.what}) is at x=${b.left}, but the page runs from ${p.left} to ${p.right}`,
        ).toBe(true);
    });
}

async function boxOf(locator: ReturnType<Page["locator"]>, what: string) {
    const box = await locator.boundingBox();
    if (!box) throw new Error(`${what} has no box on screen.`);
    return box;
}

test.describe("showing the other pages of the book", () => {
    test("builds a book with six text pages [Test Case ID 835]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 6);
        const contentPages = await getContentPages(page);
        expect(contentPages.length).toBe(6);
        for (let i = 0; i < contentPages.length; i++) {
            await goToPage(page, contentPages[i].id);
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                textOfPage(i),
            );
        }
        // Leaving the last page saves it.
        await goToPage(page, contentPages[0].id);
    });

    test("All Pages shows every page, with the facing page beside the page being edited [Test Case ID 835]", async ({
        page,
    }) => {
        const pages = await getPages(page);
        const contentPages = await getContentPages(page);
        const edited = contentPages[1];
        await goToPage(page, edited.id);
        expect(await chosenPageView(page)).toBe("one");
        await expect(
            editablePageFrame(page).locator(".bloom-book-grid-cell"),
        ).toHaveCount(0);

        const listBefore = await pageListPlacement(page);
        expect(listBefore.scrolled).toBe("");

        // THE ACTION UNDER TEST: choose All Pages.
        await choosePageView(page, "all");

        await expect(
            editablePageFrame(page).locator(".bloom-book-grid-cell"),
        ).toHaveCount(pages.length);
        // Still exactly one page is editable in this document: the others are in their own frames.
        await expect(
            editablePageFrame(page).locator(".bloom-page"),
        ).toHaveCount(1);

        const editedIndex = pages.findIndex((p) => p.id === edited.id);
        const facing = pages[facingIndex(editedIndex)];
        const pageBox = await boxOf(
            editablePageFrame(page).locator(".bloom-page"),
            "The page being edited",
        );
        const facingCell = cell(page, facing.id);
        await facingCell.scrollIntoViewIfNeeded();
        const facingBox = await boxOf(facingCell, "The facing page");
        const pageBoxNow = await boxOf(
            editablePageFrame(page).locator(".bloom-page"),
            "The page being edited",
        );
        expect(Math.abs(facingBox.y - pageBoxNow.y)).toBeLessThan(2);
        expect(Math.abs(facingBox.height - pageBoxNow.height)).toBeLessThan(2);
        if (editedIndex % 2 === 1) {
            // A left page: the facing page is on its right.
            expect(
                Math.abs(facingBox.x - (pageBoxNow.x + pageBoxNow.width)),
            ).toBeLessThan(2);
        } else {
            expect(
                Math.abs(facingBox.x + facingBox.width - pageBoxNow.x),
            ).toBeLessThan(2);
        }
        expect(pageBox.width).toBeGreaterThan(0);
        // The page view chooser is in the top bar, above the page frame, and stays put when the
        // pages scroll.
        const frameBox = await boxOf(page.locator("#page"), "The page frame");
        const viewControl = page.getByTestId("page-view-control");
        const viewControlBox = await boxOf(
            viewControl,
            "The page view chooser",
        );
        expect(viewControlBox.y + viewControlBox.height).toBeLessThanOrEqual(
            frameBox.y,
        );
        await editablePageFrame(page).evaluate(() => window.scrollBy(0, 300));
        expect(await boxOf(viewControl, "The page view chooser")).toEqual(
            viewControlBox,
        );
        await editablePageFrame(page).evaluate(() => window.scrollBy(0, -300));

        // The page list has not been shoved aside.
        expect(await pageListPlacement(page)).toEqual(listBefore);

        // The facing page shows what was typed on it.
        const facingContentIndex = contentPages.findIndex(
            (p) => p.id === facing.id,
        );
        await expect(
            editablePageFrame(page)
                .frameLocator(
                    `.bloom-book-grid-cell[data-page-id="${facing.id}"] iframe`,
                )
                .locator(".bloom-editable[lang='en']")
                .first(),
        ).toHaveText(textOfPage(facingContentIndex), { timeout: 15000 });
    });

    test("All Pages stays chosen for the next page, and saving a page leaves the others out of the book [Test Case ID 835]", async ({
        page,
    }) => {
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[3].id);
        expect(await chosenPageView(page)).toBe("all");
        await expect(
            editablePageFrame(page).locator(".bloom-book-grid-cell"),
        ).toHaveCount((await getPages(page)).length);

        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "en",
            "Edited with the other pages showing",
        );
        await goToPage(page, contentPages[2].id);
        const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
        expect(html).toContain("Edited with the other pages showing");
        expect(html).not.toContain("bloom-book-grid");
        expect((html.match(/class="bloom-page /g) ?? []).length).toBe(
            (await getPages(page)).length,
        );
    });

    test("clicking another page makes it the page being edited, where it was on screen [Test Case ID 835]", async ({
        page,
    }) => {
        const pages = await getPages(page);
        const contentPages = await getContentPages(page);
        const edited = contentPages[2];
        expect(await getShownPageId(page)).toBe(edited.id);
        const editedIndex = pages.findIndex((p) => p.id === edited.id);
        const target = pages[facingIndex(editedIndex)];
        const targetCell = cell(page, target.id);
        await targetCell.scrollIntoViewIfNeeded();
        const textBox = editablePageFrame(page)
            .frameLocator(
                `.bloom-book-grid-cell[data-page-id="${target.id}"] iframe`,
            )
            .locator(".bloom-editable[lang='en']")
            .first();
        await expect(textBox).toBeVisible({ timeout: 15000 });
        const cellBox = await boxOf(targetCell, "The page to click");
        const textBoxBox = await boxOf(textBox, "The text box to click");

        // THE ACTION UNDER TEST: a real click on the other page's text.
        await page.mouse.click(
            textBoxBox.x + textBoxBox.width / 2,
            textBoxBox.y + textBoxBox.height / 2,
        );

        await expect
            .poll(() => getShownPageId(page), { timeout: 30000 })
            .toBe(target.id);
        await waitForEditablePage(page);
        const newPageBox = await boxOf(
            editablePageFrame(page).locator(".bloom-page"),
            "The page now being edited",
        );
        expect(Math.abs(newPageBox.x - cellBox.x)).toBeLessThan(3);
        expect(Math.abs(newPageBox.y - cellBox.y)).toBeLessThan(3);
        // The page it came from is now one of the other pages.
        await expect(cell(page, edited.id)).toHaveCount(1);
        await expect(cell(page, edited.id)).not.toHaveClass(
            /bloom-book-grid-edited/,
        );
        // Nothing shoved the page list aside.
        expect((await pageListPlacement(page)).scrolled).toBe("");
        // And the click put the text cursor in the text box that was clicked.
        await expect
            .poll(
                () =>
                    editablePageFrame(page).evaluate(
                        () =>
                            document.activeElement
                                ?.closest(".bloom-editable")
                                ?.getAttribute("lang") ?? "",
                    ),
                { timeout: 10000 },
            )
            .toBe("en");
    });

    test("the view holds steady while the clicked page becomes the page being edited [Test Case ID 835]", async ({
        page,
    }) => {
        const pages = await getPages(page);
        const leaving = await getShownPageId(page);
        const leavingIndex = pages.findIndex((p) => p.id === leaving);
        const target = pages[facingIndex(leavingIndex)];
        const targetCell = cell(page, target.id);
        await targetCell.scrollIntoViewIfNeeded();
        await expect(targetCell).toHaveClass(/bloom-book-grid-rendered/, {
            timeout: 15000,
        });

        // On every animation frame, look at whichever page frame is on top (the page being left
        // stays on top until the new one is ready) and record where the two pages are on screen
        // and whether each is drawn.
        await page.evaluate(
            ({ ids }) => {
                const w = window as unknown as Record<string, unknown>;
                const samples: unknown[] = [];
                w.__steadySamples = samples;
                w.__steadyRunning = true;
                const sample = () => {
                    const top = (document.getElementById("page-outgoing") ??
                        document.getElementById("page")) as HTMLIFrameElement;
                    const doc = top.contentDocument!;
                    const frameRect = top.getBoundingClientRect();
                    samples.push({
                        frame: top.id,
                        waiting:
                            !!document.getElementById("page-loading-cover"),
                        pages: ids.map((id) => {
                            const c = doc.querySelector(
                                `.bloom-book-grid-cell[data-page-id="${id}"]`,
                            );
                            if (!c) return { id, inGrid: false };
                            const r = c.getBoundingClientRect();
                            const edited = c.classList.contains(
                                "bloom-book-grid-edited",
                            );
                            return {
                                id,
                                inGrid: true,
                                x: Math.round(frameRect.left + r.left),
                                y: Math.round(frameRect.top + r.top),
                                drawn: edited
                                    ? doc.querySelector(".bloom-page")?.id ===
                                      id
                                    : c.classList.contains(
                                          "bloom-book-grid-rendered",
                                      ),
                            };
                        }),
                    });
                    if (w.__steadyRunning) requestAnimationFrame(sample);
                };
                requestAnimationFrame(sample);
            },
            { ids: [leaving!, target.id] },
        );

        // THE ACTION UNDER TEST: a real click on the other page.
        await realClick(targetCell.locator(".bloom-book-grid-veil"));
        await expect
            .poll(() => getShownPageId(page), { timeout: 30000 })
            .toBe(target.id);
        await waitForEditablePage(page);
        await expect(page.locator("#page-outgoing")).toHaveCount(0, {
            timeout: 15000,
        });

        const samples = (await page.evaluate(() => {
            const w = window as unknown as Record<string, unknown>;
            w.__steadyRunning = false;
            return w.__steadySamples;
        })) as {
            frame: string;
            waiting: boolean;
            pages: {
                id: string;
                inGrid: boolean;
                x?: number;
                y?: number;
                drawn?: boolean;
            }[];
        }[];
        // Sanity check: the sampling saw both frames, the old one first and the new one last.
        expect(samples.length).toBeGreaterThan(10);
        expect(samples.some((s) => s.frame === "page-outgoing")).toBe(true);
        expect(samples[samples.length - 1].frame).toBe("page");
        expect(samples[samples.length - 1].pages.every((p) => p.inGrid)).toBe(
            true,
        );

        // The wait cursor shows for as long as the page being left is on screen, and not after.
        samples
            .filter((s) => s.frame === "page-outgoing")
            .forEach((s, i) =>
                expect(
                    s.waiting,
                    `animation frame ${i} of the page being left was not showing the wait cursor`,
                ).toBe(true),
            );
        expect(samples[samples.length - 1].waiting).toBe(false);

        const first = samples[0].pages;
        samples.forEach((s, i) => {
            s.pages.forEach((p, j) => {
                const where = `animation frame ${i} (${s.frame}), page ${p.id}`;
                expect(p.inGrid, `${where} was not in the grid`).toBe(true);
                expect(p.drawn, `${where} was not drawn`).toBe(true);
                expect(
                    Math.abs(p.x! - first[j].x!),
                    `${where} moved sideways`,
                ).toBeLessThanOrEqual(2);
                expect(
                    Math.abs(p.y! - first[j].y!),
                    `${where} moved up or down`,
                ).toBeLessThanOrEqual(2);
            });
        });
    });

    test("clicking a picture on another page selects that picture on the page being edited [Test Case ID 835]", async ({
        page,
    }) => {
        // A picture page, and a page facing it to start from.
        const before = await getPages(page);
        await addPage(page, "Basic Text & Image");
        const pages = await getPages(page);
        const pictureIndex = pages.findIndex(
            (p) => !before.some((b) => b.id === p.id),
        );
        expect(pictureIndex).toBeGreaterThan(0);
        const start = pages[facingIndex(pictureIndex)];
        const target = pages[pictureIndex];
        await goToPage(page, start.id);
        const targetCell = cell(page, target.id);
        await targetCell.scrollIntoViewIfNeeded();
        const picture = editablePageFrame(page)
            .frameLocator(
                `.bloom-book-grid-cell[data-page-id="${target.id}"] iframe`,
            )
            .locator(".bloom-canvas-element:has(img)")
            .first();
        await expect(picture).toBeVisible({ timeout: 15000 });
        const pictureBox = await boxOf(picture, "The picture to click");

        // THE ACTION UNDER TEST: a real click on the picture on the other page.
        await page.mouse.click(
            pictureBox.x + pictureBox.width / 2,
            pictureBox.y + pictureBox.height / 2,
        );

        await expect
            .poll(() => getShownPageId(page), { timeout: 30000 })
            .toBe(target.id);
        await waitForEditablePage(page);
        const selected = editablePageFrame(page).locator(
            '.bloom-canvas-element[data-bloom-active="true"]',
        );
        await expect(selected).toHaveCount(1, { timeout: 10000 });
        await expect(selected.locator("img")).toHaveCount(1);
        const selectedBox = await boxOf(selected, "The selected picture");
        expect(Math.abs(selectedBox.x - pictureBox.x)).toBeLessThan(3);
        expect(Math.abs(selectedBox.y - pictureBox.y)).toBeLessThan(3);
    });

    test("in a long book only the pages near the screen are rendered [Test Case ID 835]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await addPage(page, "Just Text", 14);
        const pages = await getPages(page);
        expect(pages.length).toBeGreaterThan(20);
        await goToPage(page, pages[1].id);
        const frame = editablePageFrame(page);
        await expect(frame.locator(".bloom-book-grid-cell")).toHaveCount(
            pages.length,
        );
        const rendered = frame.locator(
            ".bloom-book-grid-cell.bloom-book-grid-rendered",
        );
        await expect(rendered.first()).toBeAttached({ timeout: 15000 });
        const renderedAtTop = await rendered.count();
        expect(renderedAtTop).toBeLessThan(pages.length - 1);
        const lastCell = cell(page, pages[pages.length - 1].id);
        await expect(lastCell).not.toHaveClass(/bloom-book-grid-rendered/);

        await frame.evaluate(() =>
            window.scrollTo(0, document.documentElement.scrollHeight),
        );
        await expect(lastCell).toHaveClass(/bloom-book-grid-rendered/, {
            timeout: 15000,
        });
        // And the pages at the top, now far away, have been dropped.
        await expect(cell(page, pages[0].id)).not.toHaveClass(
            /bloom-book-grid-rendered/,
            { timeout: 15000 },
        );
    });

    test("clicking a page whose top is above the view opens that page and no other [Test Case ID 835]", async ({
        page,
    }) => {
        // Bloom focuses the first empty text box on the page it opens, and the browser scrolls a
        // focused box that is out of view to the middle of the view. When that moved the page, the
        // click Bloom replays on the opened page landed on the page above it, which opened that
        // one, and so on back through the book. It takes a page whose first text box is near its
        // top, so the box can be wholly above the view while the rest of the page is on screen.
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[contentPages.length - 1].id);
        const before = await getPages(page);
        await addPage(page, "Image on Bottom");
        const pages = await getPages(page);
        const targetIndex = pages.findIndex(
            (p) => !before.some((b) => b.id === p.id),
        );
        const target = pages[targetIndex];
        expect(
            targetIndex,
            "Sanity check: the page to click should have pages above it",
        ).toBeGreaterThan(5);
        // Start two rows away, so the page to click is one of the other pages.
        await goToPage(page, pages[targetIndex - 4].id);
        expect(await chosenPageView(page)).toBe("all");
        const placed = await scrollPageTopAboveView(page, target.id, 380);
        expect(
            placed.page.top,
            "Sanity check: the top of the page to click should be above the view",
        ).toBeLessThan(placed.visible.top);

        // THE ACTION UNDER TEST: a real click on the part of that page that is on screen.
        await clickPageInGrid(page, target.id, 150);

        await expect
            .poll(() => getShownPageId(page), {
                timeout: 30000,
                message:
                    "The clicked page never became the page being edited, or Bloom went on to other pages by itself",
            })
            .toBe(target.id);
        await waitForEditablePage(page);
        await waitForClickedPageToSettle(page);
        expect(await getShownPageId(page)).toBe(target.id);
        expect(
            await isPageChangeUnderway(page),
            "Something clicked another page after the clicked page opened",
        ).toBe(false);
        // And the page is still where it was clicked.
        const after = await getPagePlacement(page, target.id);
        expect(Math.abs(after.page.top - placed.page.top)).toBeLessThan(3);
    });

    test("the page's bubbles stay beside it in All Pages and after going back to One Page [Test Case ID 835]", async ({
        page,
    }) => {
        const credits = (await getPages(page)).find(
            (p) => p.caption === "Credits Page",
        );
        expect(credits, "The book should have a Credits Page").toBeDefined();
        await goToPage(page, credits!.id);
        expect(await chosenPageView(page)).toBe("all");
        await expect
            .poll(async () => (await pageAndBubbles(page)).bubbles.length, {
                timeout: 15000,
            })
            .toBeGreaterThan(0);
        await expectBubblesBesidePage(page, "With All Pages");

        // THE ACTION UNDER TEST: back to One Page.
        await choosePageView(page, "one");
        await expectBubblesBesidePage(page, "After choosing One Page");

        await choosePageView(page, "all");
        await expectBubblesBesidePage(page, "After choosing All Pages again");
    });

    test("choosing One Page removes the other pages [Test Case ID 835]", async ({
        page,
    }) => {
        const contentPages: IBookPage[] = await getContentPages(page);
        await goToPage(page, contentPages[0].id);
        expect(await chosenPageView(page)).toBe("all");
        const control = page.getByTestId("page-view-control");
        const controlBefore = await boxOf(control, "The page view chooser");
        await choosePageView(page, "one");
        await expect(
            editablePageFrame(page).locator(".bloom-book-grid-cell"),
        ).toHaveCount(0);
        // The chooser stays exactly where it was, in the top bar.
        expect(await boxOf(control, "The page view chooser")).toEqual(
            controlBefore,
        );
        await goToPage(page, contentPages[1].id);
        expect(await chosenPageView(page)).toBe("one");
        await expect(
            editablePageFrame(page).locator(".bloom-book-grid-cell"),
        ).toHaveCount(0);
    });
});
