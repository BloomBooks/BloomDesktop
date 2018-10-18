// Ideas mainly taken from https://git.cryto.net/joepie91/measure-font.git
// As that is CC0, this module may be considered to fall under the usual
// Bloom license.

export class MeasureText {
    // Returns an object with three measurements:
    // actualDescent: distance in pixels between the baseline and
    // the bottom of the lowest descender in the last line of text
    // (This is slightly approximate since we really only want to consider the
    // part of the text that is on the last line when text is wrapped in a paragraph
    // of the specified width. We approximate this by measuring the part of the
    // text at the end that fits in the specified width.) (FontSize is in pixels.)
    // fontDescent: the descent of the font used by the browser to compute scrollHeight; we
    // think this is the descent actually recorded in the font.
    // layoutDescent: the distance from the baseline of the text to the bottom
    // of an auto-sized box containing it at the specified line height.
    // (this may actually be LESS than the height needed to really draw the descenders!)
    public static getDescentMeasurements(
        text: string,
        fontFamily: string,
        fontSize: number,
        width: number,
        lineHeight: string
    ) {
        const t0 = performance.now();
        // Get the descent that the browser uses for various purposes,
        // including painting the background of spans, and determining the
        // scrollHeight of a block of text. I _think_ it is based on the
        // descent that is recorded as a property of the font itself; we
        // unfortunately don't have any way to access this in JS.
        // Instead, we make a div which contains a character in the font
        // (It doesn't matter how much text, or whether it has any descenders,
        // but we want to avoid automatic font substitution so using some
        // real text meant to be rendered in the font is a good idea.)
        // We also put an inline block with no content and a very small fixed
        // height. HTML aligns the bottom of this with the baseline,
        // and in the absence of other constraints makes the div high enough
        // to contain the text including the kind of descent we want to measure.
        // Thus, the distance from the bottom of the block to the bottom of
        // the whole div provides the 'wrong' descent that we want to compare
        // with the real one.
        const div = document.createElement("div");
        const block = document.createElement("div");
        div.innerText = text.substr(0, 1);
        div.style.fontFamily = fontFamily;
        div.style.fontSize = fontSize + "px";

        // It has to be in the document to get measured, but we don't want the
        // user to see it.
        div.style.visibility = "hidden";
        block.style.display = "inline-block";
        block.style.height = "1px";
        div.appendChild(block);
        // We don't want to put it in the document, but unless we do all its
        // measurements are zero.
        document.body.appendChild(div);
        const bottomOfTextWithDefaultLineSpace = div.getBoundingClientRect()
            .bottom;
        const baselineOfTextWithDefaultLineSpace = block.getBoundingClientRect()
            .bottom;
        const fontDescent =
            bottomOfTextWithDefaultLineSpace -
            baselineOfTextWithDefaultLineSpace;

        div.style.lineHeight = lineHeight;
        const bottomOfTextWithActualLineSpace = div.getBoundingClientRect()
            .bottom;
        const baselineOfTextWithActualLineSpace = block.getBoundingClientRect()
            .bottom;
        const layoutDescent =
            bottomOfTextWithActualLineSpace - baselineOfTextWithActualLineSpace;
        document.body.removeChild(div);

        // Get the real descent. This is done by drawing the text on a canvas.
        // The code in drawText causes it to be drawn in such a position
        // that the text baseline is aligned with the top of the canvas.
        // Thus, whatever is drawn on the canvas is descenders.
        // We scan to find the lowest line of pixels on which anything
        // is drawn.
        // This is not quite perfect, because we're drawing the whole content
        // of the target box as a single line. We position it so that what
        // actually gets drawn is the end of the text, but it still might
        // include some descenders from the second-last line. It's conceivable
        // to improve it further and figure out exactly what text is on the last
        // line, but we decided it was not worth the effort to be that perfectionistic.

        // We only need enough space to draw what the browser thinks is the descent.
        const canvasHeight = fontDescent;
        const testingCanvas = this.createCanvas(width, canvasHeight);
        // The number of pixels of descent is one more than the index of
        // the bottom line on which we found part of a descender.
        // A small optimization is to give zero at once for empty strings.
        const descent = text
            ? this.getLowest(testingCanvas, text, fontFamily, fontSize) + 1
            : 0;

        //document.body.removeChild(testingCanvas); // reinstate if you put it in for debugging
        const t1 = performance.now();
        console.log(
            "measured descent of " +
                text +
                " in " +
                fontSize +
                "px " +
                fontFamily +
                " to be " +
                descent +
                " but estimated as " +
                fontDescent +
                " (calculation took " +
                (t1 - t0) +
                " ms)"
        );
        return {
            fontDescent: fontDescent,
            actualDescent: descent,
            layoutDescent: layoutDescent
        };
    }

    // Same results as getDescentMeasurements(), but gets all the arguments
    // from a given element.
    public static getDescentMeasurementsOfBox(box: HTMLElement) {
        const text = box.textContent ? box.textContent : "";
        const realStyle = window.getComputedStyle(box, null);
        // The FontFamily we get here includes quotes if there are spaces,
        // but we don't want them for the getDescentMeasurements routine.
        const fontFamily = realStyle
            .getPropertyValue("font-family")
            .replace(/"/g, "");
        const fontSize = realStyle.getPropertyValue("font-size");
        const lineHeight = realStyle.lineHeight;
        return this.getDescentMeasurements(
            text,
            fontFamily,
            parseInt(fontSize),
            box.clientWidth,
            lineHeight
        );
    }

    private static createCanvas(
        width: number,
        height: number
    ): HTMLCanvasElement {
        let canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        // May be helpful to reinstate this for debugging; lets you
        // see what is being painted for measuring.
        // canvas.style.border = "1px solid orange";
        // canvas.style.position = "absolute";
        // canvas.style.left = "0";
        // canvas.style.top = "0";
        // document.body.appendChild(canvas);
        return canvas;
    }

    private static drawText(
        canvas: HTMLCanvasElement,
        text: string,
        fontFamily: string,
        fontSize: number
    ): void {
        let context = canvas.getContext("2d");
        context.textAlign = "start"; // was "top", which is not a valid choice
        context.textBaseline = "alphabetic";
        context.font = `${fontSize}px '${fontFamily}'`;
        context.fillStyle = "white";
        const width = context.measureText(text).width;
        context.fillText(text, canvas.width - width, 0);
    }

    private static getLowest(
        canvas: HTMLCanvasElement,
        characters: string,
        fontFamily: string,
        fontSize: number
    ): number {
        this.resetCanvas(canvas);
        this.drawText(canvas, characters, fontFamily, fontSize);
        return this.findLowestEdge(canvas);
    }

    private static resetCanvas(canvas: HTMLCanvasElement): void {
        let context = canvas.getContext("2d");
        // not just black but transparent black, so all four bytes for each pixel are zero.
        // We only look at the first byte for each pixel, so 'black' would work, but this
        // feels more robust.
        context.fillStyle = "rgba(0,0,0,0)";
        context.fillRect(0, 0, canvas.width, canvas.height);
    }

    // Find the index of the lowest row that has anything drawn.
    // Returns -1 if nothing is drawn anywhere.
    private static findLowestEdge(canvas: HTMLCanvasElement) {
        return this.findEdge(canvas, canvas.height - 1, 0, -1);
    }

    // Search from firstRow to lastRow by step (typically +/-1).
    // Return the index of the first row found that has any
    // non-black pixels. (Actually, we're depending on the fact
    // that we write opaque white on transparent black, so we only
    // need to check one byte for each pixel.)
    private static findEdge(
        canvas: HTMLCanvasElement,
        firstRow: number,
        lastRow: number,
        step: number
    ): number {
        let imageData = this.getImageData(canvas).data;
        let valuesPerRow = canvas.width * 4;
        let hitEnd = false;
        if (step === 0) {
            throw new Error("Step cannot be 0");
        }
        let row = firstRow;
        while (!hitEnd) {
            let highestValue = this.scanRow(
                imageData,
                row * valuesPerRow,
                canvas.width
            );
            /* 240 is a somewhat randomly picked value to deal with anti-aliasing. */
            if (highestValue > 240) {
                return row;
            }
            row += step;
            if (step > 0) {
                hitEnd = row > lastRow;
            } else if (step < 0) {
                hitEnd = row < lastRow;
            }
        }
        return lastRow + step;
    }

    private static getImageData(canvas: HTMLCanvasElement) {
        let context = canvas.getContext("2d");
        return context.getImageData(0, 0, canvas.width, canvas.height);
    }

    private static scanRow(
        imageData: Uint8ClampedArray,
        offset: number,
        length: number
    ) {
        let highestValue = 0;
        for (let column = 0; column < length; column += 1) {
            let pixelValue = imageData[offset + column * 4];
            if (pixelValue > highestValue) {
                highestValue = pixelValue;
            }
        }
        return highestValue;
    }
}
