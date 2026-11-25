// Read how a book page is laid out, wherever Bloom is drawing it, so that a test can ask whether
// two drawings of the same page agree: the page in the Edit tab and the same page in a publication.
//
// Every place is given relative to the page itself, in thousandths of the page's width. The Edit
// tab draws the page at the zoom the user chose and a preview draws it at the size of its device
// frame, so a pixel value says nothing; where each thing sits on the page is what a reader sees.
//
// What is read is what a reader sees of what the page's author placed: each canvas, each picture
// on it, each text box on it, and the words in each box. It is deliberately not the page's
// elements one for one, because a publication does not keep them all: the picture a canvas page
// has as its background is a canvas element of its own while the page is edited, and a plain
// picture filling the canvas once the page is published. Either way the reader sees the picture in
// one place, and that place is what is read. The controls that only the Edit tab adds (handles,
// the format gear, the boxes of languages the page is not showing) are not part of the page, and
// are left out.

import { expect, type Frame } from "@playwright/test";
import { describeRect, kEdgeTolerance, sameEdge, type IRect } from "./geometry";

/**
 * How many of the page's pixels larger the picture behind a canvas is drawn in the Edit tab than
 * in a publication: a pixel past each of two opposite edges. See expectSameLayout.
 */
const kPictureBleedPixels = 2;

/** One thing on a page, and where it is, in thousandths of the page's width from its top left. */
export interface IPageLayoutItem {
    /** What the thing is, e.g. "canvas 1, picture 2" or "line 2 of the words in canvas 1, text 1". */
    name: string;
    /** For a text box and for its words, the text; for anything else, nothing. */
    text?: string;
    /** True for a picture, which may differ a little more than anything else; see expectSameLayout. */
    isPicture?: boolean;
    rect: IRect;
}

/** How one page is laid out; see getPageLayout. */
export interface IPageLayout {
    /** The page's height in the same unit as everything else, so 1000 times height over width. */
    pageHeight: number;
    /**
     * How many of those thousandths one CSS pixel of the page comes to, before any zoom: 1000 over
     * the page's width in CSS pixels. It is what turns a tolerance in pixels into this unit.
     */
    pixel: number;
    items: IPageLayoutItem[];
}

/**
 * How the page with id `pageId` is laid out in `frame`, which is any document that draws that
 * page: the Edit tab's page frame (editablePageFrame), or a page of an ePUB preview
 * (showEpubPreviewPage). The pictures must have loaded, because where one is drawn depends on its
 * shape.
 *
 * For each canvas on the page, in this order: the canvas; each picture on it, as the area the
 * picture itself covers; and each canvas element that shows text, as the element, its text box,
 * and each line of the words in the box.
 *
 * Throws when the document is not showing that page.
 */
export async function getPageLayout(
    frame: Frame,
    pageId: string,
): Promise<IPageLayout> {
    const layout = await frame.evaluate((id) => {
        interface IBox {
            left: number;
            top: number;
            width: number;
            height: number;
        }
        const bookPage = document.getElementById(id);
        if (!bookPage) return undefined;
        const pageBox = bookPage.getBoundingClientRect();
        const unit = pageBox.width / 1000;
        const place = (box: IBox) => ({
            x: (box.left - pageBox.left) / unit,
            y: (box.top - pageBox.top) / unit,
            width: box.width / unit,
            height: box.height / unit,
        });
        const isDrawn = (element: Element) => {
            const box = element.getBoundingClientRect();
            return (
                box.width > 0 &&
                box.height > 0 &&
                getComputedStyle(element).visibility !== "hidden"
            );
        };
        // The area a picture covers. An <img> that is told to fit inside its box (object-fit:
        // contain) is drawn smaller than the box in one direction, and a cropped one is larger
        // than the container that shows part of it; neither box is what a reader sees.
        const areaOfPicture = (picture: HTMLImageElement): IBox => {
            let { left, top, width, height } = picture.getBoundingClientRect();
            const style = getComputedStyle(picture);
            if (
                (style.objectFit === "contain" ||
                    style.objectFit === "scale-down") &&
                picture.naturalWidth > 0 &&
                picture.naturalHeight > 0
            ) {
                const scale = Math.min(
                    width / picture.naturalWidth,
                    height / picture.naturalHeight,
                );
                const drawnWidth = picture.naturalWidth * scale;
                const drawnHeight = picture.naturalHeight * scale;
                const [atX, atY] = style.objectPosition
                    .split(" ")
                    .map((part) => parseFloat(part) / 100);
                left += (width - drawnWidth) * atX;
                top += (height - drawnHeight) * atY;
                width = drawnWidth;
                height = drawnHeight;
            }
            // What shows of it: a cropped picture's container hides the rest of it, and the
            // canvas hides whatever of a picture lies beyond the canvas's own edge.
            let right = left + width;
            let bottom = top + height;
            for (const clipper of [
                picture.closest(".bloom-imageContainer"),
                picture.closest(".bloom-canvas"),
            ]) {
                if (
                    !clipper ||
                    getComputedStyle(clipper).overflow === "visible"
                )
                    continue;
                const shown = clipper.getBoundingClientRect();
                left = Math.max(left, shown.left);
                top = Math.max(top, shown.top);
                right = Math.min(right, shown.right);
                bottom = Math.min(bottom, shown.bottom);
            }
            return { left, top, width: right - left, height: bottom - top };
        };
        // The area each line of an element's text covers, from the top line down. The text
        // is measured run by run, and runs that overlap vertically by more than half the
        // height of the shorter one are on one line.
        const linesOfWords = (element: Element): IBox[] => {
            const lines: {
                left: number;
                top: number;
                right: number;
                bottom: number;
            }[] = [];
            const texts = document.createTreeWalker(
                element,
                NodeFilter.SHOW_TEXT,
            );
            for (let text = texts.nextNode(); text; text = texts.nextNode()) {
                const run = document.createRange();
                run.selectNodeContents(text);
                for (const piece of run.getClientRects()) {
                    if (piece.width === 0 || piece.height === 0) continue;
                    const line = lines.find(
                        (candidate) =>
                            Math.min(candidate.bottom, piece.bottom) -
                                Math.max(candidate.top, piece.top) >
                            Math.min(
                                candidate.bottom - candidate.top,
                                piece.height,
                            ) /
                                2,
                    );
                    if (line) {
                        line.left = Math.min(line.left, piece.left);
                        line.top = Math.min(line.top, piece.top);
                        line.right = Math.max(line.right, piece.right);
                        line.bottom = Math.max(line.bottom, piece.bottom);
                    } else
                        lines.push({
                            left: piece.left,
                            top: piece.top,
                            right: piece.right,
                            bottom: piece.bottom,
                        });
                }
            }
            return lines
                .sort((a, b) => a.top - b.top)
                .map((line) => ({
                    left: line.left,
                    top: line.top,
                    width: line.right - line.left,
                    height: line.bottom - line.top,
                }));
        };
        const items: {
            name: string;
            text?: string;
            isPicture?: boolean;
            rect: { x: number; y: number; width: number; height: number };
        }[] = [];
        [...bookPage.querySelectorAll(".bloom-canvas")].forEach(
            (canvas, canvasIndex) => {
                const canvasName = `canvas ${canvasIndex + 1}`;
                items.push({
                    name: canvasName,
                    rect: place(canvas.getBoundingClientRect()),
                });
                [
                    ...canvas.querySelectorAll<HTMLImageElement>(
                        ":scope > img, .bloom-imageContainer > img",
                    ),
                ]
                    .filter(isDrawn)
                    .forEach((picture, pictureIndex) =>
                        items.push({
                            name: `${canvasName}, picture ${pictureIndex + 1}`,
                            isPicture: true,
                            rect: place(areaOfPicture(picture)),
                        }),
                    );
                let textCount = 0;
                [...canvas.querySelectorAll(".bloom-canvas-element")]
                    .filter(isDrawn)
                    .forEach((element) => {
                        const box = [
                            ...element.querySelectorAll(".bloom-editable"),
                        ].find(isDrawn);
                        if (!box) return;
                        const textName = `${canvasName}, text ${++textCount}`;
                        const text = (box.textContent ?? "").trim();
                        items.push({
                            name: textName,
                            text,
                            rect: place(element.getBoundingClientRect()),
                        });
                        items.push({
                            name: `the text box of ${textName}`,
                            text,
                            rect: place(box.getBoundingClientRect()),
                        });
                        // Where the words themselves fall, line by line, which is what
                        // shows a different font, size, alignment or line break inside an
                        // unchanged box.
                        linesOfWords(box).forEach((line, lineIndex) =>
                            items.push({
                                name: `line ${lineIndex + 1} of the words in ${textName}`,
                                text,
                                rect: place(line),
                            }),
                        );
                    });
            },
        );
        return {
            pageHeight: pageBox.height / unit,
            pixel: 1000 / bookPage.offsetWidth,
            items,
        };
    }, pageId);
    if (!layout)
        throw new Error(
            `The document at ${frame.url()} is not showing a page with id "${pageId}".`,
        );
    return layout;
}

/**
 * Assert that a page is laid out in one place as it is in another: the page has the same shape,
 * and the same things are on it, each in the same place and holding the same text. `actualPlace`
 * and `expectedPlace` name the two for the failure message, e.g. "The fixed-mode ePUB" and "the
 * Edit tab".
 *
 * Places may differ by geometry's edge tolerance, in thousandths of the page's width: less than
 * a pixel of an A5 page, which is what scaling leaves over.
 *
 * A picture's may differ by two of the page's pixels more. While a page is edited, Bloom draws
 * the picture behind a canvas a pixel past each edge of the canvas that it reaches, so that no
 * white line shows there at the editing zoom (kBackgroundImageBleedPx in
 * backgroundImageBleed.ts). The canvas hides the overhang, but the picture is that much larger
 * all over, so the edges of it that fall inside the canvas sit up to a pixel further out than
 * they do in a publication, which draws the picture to fit.
 */
export function expectSameLayout(
    actual: IPageLayout,
    expected: IPageLayout,
    actualPlace: string,
    expectedPlace: string,
): void {
    const describeItem = (item: IPageLayoutItem) =>
        item.text === undefined ? item.name : `${item.name} ("${item.text}")`;
    expect(
        actual.items.map(describeItem),
        `${actualPlace} should show the same things on the page as ${expectedPlace}.`,
    ).toEqual(expected.items.map(describeItem));

    const differences: string[] = [];
    if (!sameEdge(actual.pageHeight, expected.pageHeight))
        differences.push(
            `the page is ${Math.round(actual.pageHeight)} high for every 1000 wide, ` +
                `not ${Math.round(expected.pageHeight)}`,
        );
    const pictureAllowance = kPictureBleedPixels * expected.pixel;
    expected.items.forEach((item, index) => {
        const there = actual.items[index].rect;
        const here = item.rect;
        const allowance = item.isPicture ? pictureAllowance : 0;
        const near = (a: number, b: number) =>
            Math.abs(a - b) <= kEdgeTolerance + allowance;
        const same =
            near(there.x, here.x) &&
            near(there.y, here.y) &&
            near(there.width, here.width) &&
            near(there.height, here.height);
        if (!same)
            differences.push(
                `${describeItem(item)} is at ${describeRect(there)}, not ${describeRect(here)}`,
            );
    });
    expect(
        differences,
        `${actualPlace} should lay the page out as ${expectedPlace} does (places are in ` +
            `thousandths of the page's width, from its top left corner):\n  ` +
            differences.join("\n  "),
    ).toEqual([]);
}
