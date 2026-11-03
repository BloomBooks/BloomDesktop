// Bloom's clipboard, as a test sees it.
//
// Under --e2e, Bloom does not use the Windows clipboard. Its C# goes through BloomClipboard and
// its pages through utils/bloomClipboard.ts, and both use an in-memory clipboard that a test
// fills and reads through the e2e/clipboard hook (E2eTestingApi). So a test that copies or
// pastes leaves the developer's clipboard alone, and two e2e runs on one machine cannot see each
// other's copies. This is the clipboard's counterpart of armFileChooser in helpers/talkingBook.ts.
//
// The one part Bloom cannot redirect is the browser's own paste: a real Ctrl+V makes WebView2
// read the Windows clipboard before any of Bloom's code runs. So pasteFromClipboard does not
// press the keys. It sends the page the paste event that Ctrl+V would have produced, carrying
// what Bloom's clipboard holds, and everything Bloom does with a paste from there on runs as it
// does for a person (AUTOMATION-DEBT.md, "The browser's own copy and paste use the Windows
// clipboard").

import type { Frame, Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiGetJson, apiPost } from "./api";

/** What Bloom's clipboard holds. A form that is missing is one the clipboard does not hold. */
export interface IClipboardContents {
    /** Plain text. */
    text?: string;
    /** HTML, as a browser's paste event offers it. */
    html?: string;
    /** The path of a picture file; the clipboard holds that picture. */
    picture?: string;
    /** Copied files, as Explorer's Copy offers them. */
    files?: string[];
}

interface IE2eClipboardJson {
    text: string | null;
    html: string | null;
    picturePath: string | null;
    files: string[] | null;
}

/**
 * Put these contents on Bloom's clipboard, replacing whatever it held, as copying them in
 * another program would put them on the Windows clipboard. Returns when Bloom reports holding
 * them.
 */
export async function copyToClipboard(
    page: Page,
    contents: IClipboardContents,
): Promise<void> {
    if (contents.picture && !fs.existsSync(contents.picture))
        throw new Error(
            `copyToClipboard: there is no picture at ${contents.picture}.`,
        );
    const json: IE2eClipboardJson = {
        text: contents.text ?? null,
        html: contents.html ?? null,
        picturePath: contents.picture ?? null,
        files: contents.files ?? null,
    };
    await apiPost(
        page,
        "e2e/clipboard",
        JSON.stringify(json),
        "application/json",
    );
    // Compare as Bloom reports it, so that the order of the fields does not matter.
    const held = await getClipboard(page);
    if (JSON.stringify(held) !== JSON.stringify(fromJson(json)))
        throw new Error(
            `Bloom's clipboard does not hold what was copied. Copied ${JSON.stringify(contents)}; ` +
                `it holds ${JSON.stringify(held)}.`,
        );
}

/**
 * What Bloom's clipboard holds now, for instance after a copy in Bloom. A form that is missing is
 * one it does not hold.
 */
export async function getClipboard(page: Page): Promise<IClipboardContents> {
    return fromJson(await apiGetJson<IE2eClipboardJson>(page, "e2e/clipboard"));
}

/** The contents that e2e/clipboard's JSON describes, without the forms it says are missing. */
function fromJson(json: IE2eClipboardJson): IClipboardContents {
    const contents: IClipboardContents = {};
    if (json.text !== null) contents.text = json.text;
    if (json.html !== null) contents.html = json.html;
    if (json.picturePath !== null) contents.picture = json.picturePath;
    if (json.files !== null) contents.files = json.files;
    return contents;
}

/**
 * Paste what Bloom's clipboard holds into whatever has the focus, as Ctrl+V does. Put the focus
 * there first, e.g. with clickInGroup.
 *
 * This sends the focused element the paste event a real Ctrl+V would produce: its clipboardData
 * holds the clipboard's text, its HTML, and its picture as an image file, which is how Chromium
 * offers a picture from the Windows clipboard. Bloom's paste handling and CKEditor's then run as
 * they do for a person, and anything that asks Bloom's C# for the clipboard gets the same
 * contents. A paste event that nothing handles changes nothing, as with a script-sent event in
 * any browser, so this cannot paste into a plain input box.
 *
 * Returns as soon as the event has been sent; what the paste should do is the caller's question.
 */
export async function pasteFromClipboard(page: Page): Promise<void> {
    const contents = await getClipboard(page);
    const picture = contents.picture
        ? {
              name: Path.basename(contents.picture),
              type: pictureMimeType(contents.picture),
              base64: fs.readFileSync(contents.picture).toString("base64"),
          }
        : null;
    const frame = await focusedFrame(page);
    const sent = await frame.evaluate(
        ({ text, html, picture }) => {
            const target = document.activeElement;
            if (!target || target === document.body) return false;
            const transfer = new DataTransfer();
            if (text !== undefined) transfer.setData("text/plain", text);
            if (html !== undefined) transfer.setData("text/html", html);
            if (picture) {
                const bytes = Uint8Array.from(atob(picture.base64), (c) =>
                    c.charCodeAt(0),
                );
                transfer.items.add(
                    new File([bytes], picture.name, { type: picture.type }),
                );
            }
            target.dispatchEvent(
                new ClipboardEvent("paste", {
                    bubbles: true,
                    cancelable: true,
                    composed: true,
                    clipboardData: transfer,
                }),
            );
            return true;
        },
        { text: contents.text, html: contents.html, picture },
    );
    if (!sent)
        throw new Error(
            "pasteFromClipboard: nothing on the page has the focus, so a paste would go nowhere. " +
                "Click in the box to paste into first.",
        );
}

/**
 * The frame whose document holds the focused element: the focus passes from each document into
 * the frame element that has it, down to a document whose focused element is not a frame.
 */
async function focusedFrame(page: Page): Promise<Frame> {
    let frame = page.mainFrame();
    for (;;) {
        const active = await frame.evaluateHandle(() => document.activeElement);
        const element = active.asElement();
        const inner = element ? await element.contentFrame() : null;
        await active.dispose();
        if (!inner) return frame;
        frame = inner;
    }
}

function pictureMimeType(path: string): string {
    const extension = Path.extname(path).toLowerCase();
    switch (extension) {
        case ".png":
            return "image/png";
        case ".jpg":
        case ".jpeg":
            return "image/jpeg";
        case ".gif":
            return "image/gif";
        case ".bmp":
            return "image/bmp";
        default:
            throw new Error(
                `pasteFromClipboard: no picture type is known for "${extension}" files (${path}).`,
            );
    }
}
