// Read a PDF that Bloom made: each page's size, the text on it with where it sits, and pictures of
// the pages. Everything goes through the Ghostscript that ships with Bloom (DistFiles/ghostscript),
// so this package needs no PDF library of its own.
//
// Positions are in PDF points (1/72 inch) from the top left of the page, as Ghostscript's txtwrite
// device reports them.

import { execFileSync } from "node:child_process";
import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
    "..",
);
const ghostscript = Path.join(
    repoRoot,
    "DistFiles",
    "ghostscript",
    "gswin32c.exe",
);

/**
 * A run of text on a PDF page and where it sits. Ghostscript gives each run's baseline, so top and
 * bottom are the same; the size is in fontSize. Large text often comes as one run per letter.
 */
export interface IPdfText {
    text: string;
    /** The font size, in points. */
    fontSize: number;
    left: number;
    top: number;
    right: number;
    bottom: number;
}

/** One page of a PDF. */
export interface IPdfPage {
    widthPt: number;
    heightPt: number;
    /** Every run of text on the page, in the order Ghostscript found them. */
    texts: IPdfText[];
    /** All the page's text, runs joined with spaces. */
    text: string;
    /**
     * All the page's text with no white space at all, which is the reliable thing to search:
     * Ghostscript splits some text into one run per letter, so the spaces in `text` are not the
     * page's own.
     */
    compactText: string;
}

function runGhostscript(args: string[]): string {
    return execFileSync(ghostscript, args, {
        encoding: "utf8",
        maxBuffer: 256 * 1024 * 1024,
        windowsHide: true,
    });
}

/** Read every page of the PDF at `pdfPath`. */
export function readPdf(pdfPath: string): IPdfPage[] {
    const sizes = readPageSizes(pdfPath);
    const texts = readPageTexts(pdfPath);
    if (texts.length !== sizes.length)
        throw new Error(
            `Ghostscript found ${sizes.length} pages in ${pdfPath} but text for ${texts.length}.`,
        );
    return sizes.map((size, i) => ({
        ...size,
        texts: texts[i],
        text: texts[i].map((t) => t.text).join(" "),
        compactText: texts[i].map((t) => t.text.replace(/\s/g, "")).join(""),
    }));
}

/** The width and height, in points, of each page's MediaBox. */
function readPageSizes(
    pdfPath: string,
): { widthPt: number; heightPt: number }[] {
    const psPath = pdfPath.replace(/\\/g, "/");
    const output = runGhostscript([
        "-q",
        "-dNODISPLAY",
        "-dNOSAFER",
        "-dNOPAUSE",
        "-dBATCH",
        "-c",
        `(${psPath}) (r) file runpdfbegin 1 1 pdfpagecount {pdfgetpage /MediaBox pget pop ==} for quit`,
    ]);
    return output
        .split(/\r?\n/)
        .filter((line) => line.trim().startsWith("["))
        .map((line) => {
            const [x0, y0, x1, y1] = line
                .replace(/[[\]]/g, " ")
                .trim()
                .split(/\s+/)
                .map(Number);
            return { widthPt: x1 - x0, heightPt: y1 - y0 };
        });
}

/** The runs of text on each page, from Ghostscript's txtwrite device. */
function readPageTexts(pdfPath: string): IPdfText[][] {
    const output = runGhostscript([
        "-q",
        "-dNOPAUSE",
        "-dBATCH",
        "-sDEVICE=txtwrite",
        "-dTextFormat=0",
        "-sOutputFile=-",
        pdfPath,
    ]);
    return output
        .split(/<page\b[^>]*>/)
        .slice(1)
        .map((pageXml) =>
            [
                ...pageXml.matchAll(
                    /<span bbox="([^"]*)"[^>]*?size="([^"]*)"[^>]*>([\s\S]*?)<\/span>/g,
                ),
            ]
                .map((match) => {
                    const [left, top, right, bottom] = match[1]
                        .trim()
                        .split(/\s+/)
                        .map(Number);
                    const text = [
                        ...match[3].matchAll(/<char [^>]*c="([^"]*)"/g),
                    ]
                        .map((c) => decodeXml(c[1]))
                        .join("");
                    return {
                        text: text.trim(),
                        fontSize: Number(match[2]),
                        left,
                        top,
                        right,
                        bottom,
                    };
                })
                .filter((t) => t.text.length > 0),
        );
}

function decodeXml(value: string): string {
    return value
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&#x([0-9a-fA-F]+);/g, (_, hex) =>
            String.fromCodePoint(parseInt(hex, 16)),
        )
        .replace(/&#(\d+);/g, (_, dec) => String.fromCodePoint(Number(dec)))
        .replace(/&amp;/g, "&");
}

/**
 * How many pixels of one page (numbered from 1), rendered at `dpi`, satisfy `test`, which gets
 * each pixel's red, green and blue from 0 to 255. Ghostscript's ppmraw device writes a header and
 * then plain RGB bytes, so no image library is needed to read it.
 */
export function countPixels(
    pdfPath: string,
    pageNumber: number,
    test: (red: number, green: number, blue: number) => boolean,
    dpi = 30,
): number {
    const ppm = execFileSync(
        ghostscript,
        [
            "-q",
            "-dNOPAUSE",
            "-dBATCH",
            "-sDEVICE=ppmraw",
            `-r${dpi}`,
            `-dFirstPage=${pageNumber}`,
            `-dLastPage=${pageNumber}`,
            "-sOutputFile=-",
            pdfPath,
        ],
        { maxBuffer: 256 * 1024 * 1024, windowsHide: true },
    );
    // "P6", width, height, max value, each followed by white space (with comment lines allowed
    // between them), then the pixels.
    let offset = 0;
    const fields: string[] = [];
    while (fields.length < 4) {
        while (/\s/.test(String.fromCharCode(ppm[offset]))) offset++;
        // Ghostscript puts a "# Image generated by..." comment in the header.
        if (ppm[offset] === "#".charCodeAt(0)) {
            while (ppm[offset] !== "\n".charCodeAt(0)) offset++;
            continue;
        }
        let field = "";
        while (!/\s/.test(String.fromCharCode(ppm[offset])))
            field += String.fromCharCode(ppm[offset++]);
        fields.push(field);
    }
    offset++;
    let count = 0;
    for (let i = offset; i + 2 < ppm.length; i += 3)
        if (test(ppm[i], ppm[i + 1], ppm[i + 2])) count++;
    return count;
}

/**
 * Render each page of the PDF to a PNG in `outputFolder` (page-001.png, page-002.png, ...) at
 * `dpi`, and return their paths in page order.
 */
export function rasterizePdf(
    pdfPath: string,
    outputFolder: string,
    dpi = 50,
): string[] {
    fs.mkdirSync(outputFolder, { recursive: true });
    runGhostscript([
        "-q",
        "-dNOPAUSE",
        "-dBATCH",
        "-sDEVICE=png16m",
        `-r${dpi}`,
        `-sOutputFile=${Path.join(outputFolder, "page-%03d.png")}`,
        pdfPath,
    ]);
    return fs
        .readdirSync(outputFolder)
        .filter((name) => /^page-\d{3}\.png$/.test(name))
        .sort()
        .map((name) => Path.join(outputFolder, name));
}
