// Builds a page holding one inline image inside one bloom-editable, styled by the REAL
// src/content/bookLayout/inlineImages.less (compiled here, not copied), and measures
// where the text actually ended up on each line. Everything the tests assert about wrap
// geometry comes out of a browser laying that CSS out, so the tests fail if the CSS
// stops meaning what it says.

import { Page } from "playwright/test";
import * as fs from "fs";
import * as path from "path";
import less from "less";

const lessPath = path.join(
    __dirname,
    "../../../../content/bookLayout/inlineImages.less",
);

// A 1x1 fully transparent PNG. The picture's own pixels never matter here: the wrapper
// sizes the img from --inline-image-width and --inline-image-aspect-ratio, and the
// contour under test is a polygon, not the image's alpha.
const TRANSPARENT_PNG =
    "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

export const EDITABLE_WIDTH = 400;
export const LINE_HEIGHT = 20;
export const FONT_SIZE = 16; // so that the CSS's 1em gap is 16px

let compiledCss: string | undefined;

/** Compile inlineImages.less once per worker. */
const getInlineImageCss = async (): Promise<string> => {
    if (compiledCss === undefined) {
        const source = fs.readFileSync(lessPath, "utf8");
        const result = await less.render(source, { filename: lessPath });
        compiledCss = result.css;
    }
    return compiledCss;
};

export interface IInlineImageOptions {
    /** dock suffix: "Left" | "Right" | "Middle" | "Bottom" */
    dock: string;
    /** value for --inline-image-width, e.g. "40%" */
    width?: string;
    /** value for --inline-image-offset, e.g. "60px" */
    offset?: string;
    /** value for --inline-image-contour, e.g. "polygon(...) content-box" */
    contour?: string;
    /** value for --inline-image-contour-margin */
    contourMargin?: string;
}

/**
 * Load a page with one inline image in one editable, plus enough text to wrap past it.
 * The host styles are deliberately minimal and monospaced so that a line's measured
 * edges mean something exact.
 */
export const loadInlineImagePage = async (
    page: Page,
    options: IInlineImageOptions,
): Promise<void> => {
    const css = await getInlineImageCss();
    const vars = [
        `--inline-image-width: ${options.width ?? "40%"}`,
        `--inline-image-offset: ${options.offset ?? "0px"}`,
        `--inline-image-aspect-ratio: 1`,
        options.contour ? `--inline-image-contour: ${options.contour}` : "",
        options.contourMargin
            ? `--inline-image-contour-margin: ${options.contourMargin}`
            : "",
    ]
        .filter(Boolean)
        .join("; ");

    await page.setContent(`<!doctype html>
<html><head><meta charset="utf-8"><style>
  body { margin: 0; }
  .bloom-editable {
      width: ${EDITABLE_WIDTH}px;
      font: ${FONT_SIZE}px/${LINE_HEIGHT}px monospace;
  }
  .bloom-editable p { margin: 0; }
  ${css}
</style></head><body>
  <div class="bloom-translationGroup">
    <div class="bloom-editable bloom-visibility-code-on bloom-content1" lang="en">
      <div class="bloom-inlineImage bloom-inlineImage${options.dock}"
           contenteditable="false" style="${vars}">
        <img src="${TRANSPARENT_PNG}">
      </div>
      <p id="text">${"i ".repeat(600)}</p>
    </div>
  </div>
</body></html>`);
    await page.waitForFunction(
        () => (document.querySelector("#text") as HTMLElement).offsetHeight > 0,
    );
};

export interface ILine {
    /** top of the line, relative to the top of the editable's content box */
    top: number;
    /** left edge of the first glyph, relative to the editable's content box */
    left: number;
    /** right edge of the last glyph */
    right: number;
}

/**
 * Where the text really is, line by line. Measured from glyph rectangles rather than
 * from the CSS, so it reports what the reader would see.
 */
export const measureLines = async (page: Page): Promise<ILine[]> =>
    page.evaluate(() => {
        const editable = document.querySelector(
            ".bloom-editable",
        ) as HTMLElement;
        const node = (document.querySelector("#text") as HTMLElement)
            .firstChild as Text;
        const box = editable.getBoundingClientRect();
        const lines = new Map<number, { left: number; right: number }>();
        const range = document.createRange();
        for (let i = 0; i < node.length; i++) {
            if (node.data[i] === " ") continue;
            range.setStart(node, i);
            range.setEnd(node, i + 1);
            const r = range.getBoundingClientRect();
            if (!r.width && !r.height) continue;
            const top = Math.round(r.top - box.top);
            const current = lines.get(top) ?? {
                left: Number.MAX_VALUE,
                right: -Number.MAX_VALUE,
            };
            current.left = Math.min(current.left, r.left - box.left);
            current.right = Math.max(current.right, r.right - box.left);
            lines.set(top, current);
        }
        return [...lines.entries()]
            .map(([top, ext]) => ({
                top,
                left: Math.round(ext.left),
                right: Math.round(ext.right),
            }))
            .sort((a, b) => a.top - b.top);
    });

/** The advance width of one character, which is the resolution of a `right` reading. */
export const measureCharacterWidth = async (page: Page): Promise<number> =>
    page.evaluate(() => {
        const node = (document.querySelector("#text") as HTMLElement)
            .firstChild as Text;
        const range = document.createRange();
        range.setStart(node, 0);
        range.setEnd(node, 2); // "i " -- one character plus its space
        return range.getBoundingClientRect().width;
    });

/** The left edges of the lines starting at the given tops, for readable assertions. */
export const leftEdgesAt = (lines: ILine[], tops: number[]): number[] =>
    tops.map((top) => {
        const line = lines.find((l) => l.top === top);
        if (!line) throw new Error(`no line of text at y=${top}`);
        return line.left;
    });
