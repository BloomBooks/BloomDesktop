// The Edit tab's page list shows a comic page with its speech bubbles: the bubble's white
// background and its outline, not just the words inside it. ComicalJS draws a bubble's outline
// and background on a drawing surface while the page is edited, and Bloom saves that drawing into
// the page as an SVG (svg.comical-generated) when the person leaves the page. The page list's
// thumbnail is the saved page, so the drawing must be in it, around the bubble's text.
//
// BL-7916 reported thumbnails that showed a bubble's text without its outline and background. Its
// comment explains that a thumbnail follows the page only once the page is saved, which Bloom does
// when the person moves to another page, so the test leaves each page before it reads the
// thumbnail.
//
// Automates "Comic Thumbnails Check" (Test Case ID 315): two pages of a Digital Comic Book get a
// speech bubble each, as in BL-7916's screenshot, and the first then gets more text, so the test
// also sees a thumbnail drawn again for a page whose bubble changed.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    pageListFrame,
    readUnlessDocumentReplaced,
    selectPage,
} from "../helpers/pageThumbnails";

test.use({
    collectionSpec: {
        name: "comic-thumbnails-show-bubbles",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** A rectangle as the page list's document lays it out, in its pixels. */
interface IBox {
    left: number;
    top: number;
    right: number;
    bottom: number;
}

/** What a page's thumbnail shows of a speech bubble. */
interface IThumbnailBubble {
    /** Where the thumbnail shows the bubble's text, or undefined when it does not show it. */
    text?: IBox;
    /** How many shapes the thumbnail's saved drawing (svg.comical-generated) holds. */
    shapes: number;
    /**
     * Whether a shape of the drawing that is filled with an opaque, light color (the bubble's
     * background) covers the text, and stays near the bubble.
     */
    backgroundAroundText: boolean;
    /**
     * Whether a shape of the drawing that is stroked with a dark color (the outline) surrounds the
     * text, and stays near the bubble.
     */
    outlineAroundText: boolean;
    /** The drawing's shapes, to say what the thumbnail holds when a check fails. */
    shapeDescriptions: string[];
}

/** A page's thumbnail in the Edit tab's page list. */
function pageThumbnail(page: Page, pageId: string) {
    return pageListFrame(page).locator(
        `#pageGridWrapper .gridItem[id="${pageId}"]`,
    );
}

/**
 * Read what the page list's thumbnail of `pageId` shows of the speech bubble whose text contains
 * `text`. Its boxes come from the thumbnail as laid out, so they take in the scaling that makes the
 * page small.
 */
async function readThumbnailBubble(
    page: Page,
    pageId: string,
    text: string,
): Promise<IThumbnailBubble | undefined> {
    const thumbnail = pageThumbnail(page, pageId);
    if ((await thumbnail.count()) === 0) return undefined;
    return readUnlessDocumentReplaced(() =>
        thumbnail.evaluate((item, wanted) => {
            const boxOf = (r: DOMRect) => ({
                left: r.left,
                top: r.top,
                right: r.right,
                bottom: r.bottom,
            });
            // The innermost element holding the text: the paragraph the bubble shows it in.
            const holders = Array.from(
                item.querySelectorAll<HTMLElement>(
                    ".bloom-canvas-element .bloom-editable p, .bloom-canvas-element .bloom-editable",
                ),
            ).filter((e) => (e.textContent ?? "").includes(wanted));
            const holder = holders[0];
            let textBox: ReturnType<typeof boxOf> | undefined;
            // How far from the bubble a shape of its drawing may reach: the bubble's own box,
            // widened on each side by its width and height, which leaves room for its tail.
            let reach: ReturnType<typeof boxOf> | undefined;
            if (holder) {
                // The text itself, not the box that holds it, which may be wider.
                const range = holder.ownerDocument.createRange();
                range.selectNodeContents(holder);
                const r = range.getBoundingClientRect();
                if (r.width > 0 && r.height > 0) textBox = boxOf(r);
                const bubble = holder
                    .closest(".bloom-canvas-element")!
                    .getBoundingClientRect();
                reach = {
                    left: bubble.left - bubble.width,
                    top: bubble.top - bubble.height,
                    right: bubble.right + bubble.width,
                    bottom: bubble.bottom + bubble.height,
                };
            }
            const svg = item.querySelector("svg.comical-generated");
            const shapes = svg
                ? Array.from(
                      svg.querySelectorAll<SVGGraphicsElement>(
                          "path, rect, ellipse, circle, polygon",
                      ),
                  )
                : [];
            // "rgb(r, g, b)" or "rgba(r, g, b, a)" to its channels and alpha.
            const channels = (color: string) => {
                const m =
                    /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s/]+([\d.]+))?\s*\)$/.exec(
                        color,
                    );
                if (!m) return undefined;
                return {
                    light:
                        (parseFloat(m[1]) +
                            parseFloat(m[2]) +
                            parseFloat(m[3])) /
                        3,
                    alpha: m[4] === undefined ? 1 : parseFloat(m[4]),
                };
            };
            const contains = (
                outer: ReturnType<typeof boxOf>,
                inner: ReturnType<typeof boxOf>,
            ) =>
                outer.left <= inner.left + 0.5 &&
                outer.top <= inner.top + 0.5 &&
                outer.right >= inner.right - 0.5 &&
                outer.bottom >= inner.bottom - 0.5;
            let backgroundAroundText = false;
            let outlineAroundText = false;
            const shapeDescriptions: string[] = [];
            for (const shape of shapes) {
                const style = getComputedStyle(shape);
                const box = boxOf(shape.getBoundingClientRect());
                // Opacity multiplies down the tree, so a shape is only as opaque as the product
                // of its own opacity and that of every element around it in the thumbnail (the
                // SVG, its group, the canvas). display: none on any of them hides it too.
                let opacity = 1;
                let displayed = true;
                for (
                    let e: Element | null = shape;
                    e && e !== item.parentElement;
                    e = e.parentElement
                ) {
                    const s = getComputedStyle(e);
                    opacity *= parseFloat(s.opacity);
                    if (s.display === "none") displayed = false;
                }
                const shown =
                    displayed &&
                    opacity > 0 &&
                    style.visibility === "visible" &&
                    box.right - box.left > 0 &&
                    box.bottom - box.top > 0;
                const fill = channels(style.fill);
                const stroke = channels(style.stroke);
                const filledLight =
                    shown &&
                    !!fill &&
                    fill.alpha * parseFloat(style.fillOpacity) * opacity >=
                        0.9 &&
                    fill.light >= 200;
                const strokedDark =
                    shown &&
                    !!stroke &&
                    stroke.alpha * parseFloat(style.strokeOpacity) * opacity >
                        0 &&
                    parseFloat(style.strokeWidth) > 0 &&
                    stroke.light <= 100;
                // A shape that reaches well past the bubble, such as one under the whole
                // picture, is not the bubble's.
                const aroundText =
                    !!textBox &&
                    !!reach &&
                    contains(box, textBox) &&
                    contains(reach, box);
                if (aroundText && filledLight) backgroundAroundText = true;
                if (aroundText && strokedDark) outlineAroundText = true;
                shapeDescriptions.push(
                    `${shape.tagName} fill=${style.fill} stroke=${style.stroke} ` +
                        `box=${Math.round(box.left)},${Math.round(box.top)}-` +
                        `${Math.round(box.right)},${Math.round(box.bottom)}`,
                );
            }
            return {
                text: textBox,
                shapes: shapes.length,
                backgroundAroundText,
                outlineAroundText,
                shapeDescriptions,
            };
        }, text),
    );
}

/**
 * Wait until the page list's thumbnail of `pageId` shows the speech bubble whose text contains
 * `text`, with its white background and its dark outline around that text.
 */
async function expectThumbnailShowsBubble(
    page: Page,
    pageId: string,
    text: string,
    where: string,
): Promise<void> {
    let last: IThumbnailBubble | undefined;
    const why =
        `The page list's thumbnail of the ${where} does not show the speech bubble ` +
        `"${text}" with its background and outline.`;
    try {
        await expect
            .poll(
                async () => {
                    last = await readThumbnailBubble(page, pageId, text);
                    return {
                        showsText: !!last?.text,
                        backgroundAroundText:
                            last?.backgroundAroundText ?? false,
                        outlineAroundText: last?.outlineAroundText ?? false,
                    };
                },
                { timeout: 30000, message: why },
            )
            .toEqual({
                showsText: true,
                backgroundAroundText: true,
                outlineAroundText: true,
            });
    } catch (error) {
        // Say what the thumbnail holds, which the comparison alone does not.
        throw new Error(
            `${why} It shows: ${JSON.stringify(last)}\n${(error as Error).message}`,
        );
    }
}

/**
 * On the page being edited, add a speech bubble at `at` (fractions of the canvas) and type `text`
 * in it.
 */
async function addSpeechBubble(
    page: Page,
    text: string,
    at: { xFraction: number; yFraction: number },
): Promise<number> {
    const index = await dragPaletteItemOntoCanvas(page, "speech", at);
    await selectCanvasElement(page, index);
    await typeInCanvasElement(page, text);
    return index;
}

test("comic page thumbnails show each speech bubble's background and outline around its text [Test Case ID 315]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    const FIRST = "TC315 first bubble";
    const SECOND = "TC315 second bubble";
    const MORE = " says more";

    const made = await step(
        "Make a Digital Comic Book with two comic pages",
        async () => {
            await makeBookFromTemplate(page, "Digital Comic Book");
            let contentPages = await getContentPages(page);
            if (contentPages.length < 2)
                await addPage(
                    page,
                    "Comic",
                    2 - contentPages.length,
                    "Digital Comic Book",
                );
            contentPages = await getContentPages(page);
            expect(
                contentPages.length,
                "test setup: the Digital Comic Book should have two content pages",
            ).toBeGreaterThanOrEqual(2);
            return {
                coverId: (await getPages(page))[0].id,
                firstId: contentPages[0].id,
                secondId: contentPages[1].id,
            };
        },
    );
    if (!made) throw new Error("No book was made.");

    await step(
        "Before any bubble goes on, the comic pages' thumbnails hold no bubble drawing",
        async () => {
            for (const id of [made.firstId, made.secondId]) {
                // A thumbnail starts as a blank placeholder and gets the page's HTML a moment
                // later (PageThumbnail.tsx); the comic page's picture canvas shows it has.
                await expect(
                    pageThumbnail(page, id).locator(".bloom-canvas"),
                    "test setup: a comic page's thumbnail never got the page's content",
                ).not.toHaveCount(0, { timeout: 30000 });
                await expect(
                    pageThumbnail(page, id).locator(
                        "svg.comical-generated path",
                    ),
                    "test setup: a new comic page's thumbnail already holds a bubble drawing",
                ).toHaveCount(0);
            }
        },
    );

    const firstBubble = await step(
        "Put a speech bubble on each comic page, go to the cover, and check both thumbnails show their bubbles",
        async () => {
            await selectPage(page, made.firstId);
            const index = await addSpeechBubble(page, FIRST, {
                xFraction: 0.3,
                yFraction: 0.3,
            });
            await selectPage(page, made.secondId);
            await addSpeechBubble(page, SECOND, {
                xFraction: 0.6,
                yFraction: 0.5,
            });
            await selectPage(page, made.coverId);
            await expectThumbnailShowsBubble(
                page,
                made.firstId,
                FIRST,
                "first comic page",
            );
            await expectThumbnailShowsBubble(
                page,
                made.secondId,
                SECOND,
                "second comic page",
            );
            return index;
        },
    );
    if (firstBubble === undefined) throw new Error("No bubble was added.");

    await step(
        "Type more in the first page's bubble, go to the cover, and check its thumbnail shows the bubble around the new text",
        async () => {
            await selectPage(page, made.firstId);
            await selectCanvasElement(page, firstBubble);
            await typeInCanvasElement(page, MORE);
            await selectPage(page, made.coverId);
            await expectThumbnailShowsBubble(
                page,
                made.firstId,
                FIRST + MORE,
                "first comic page, after more text",
            );
            await expectThumbnailShowsBubble(
                page,
                made.secondId,
                SECOND,
                "second comic page, after the first changed",
            );
        },
    );
});
