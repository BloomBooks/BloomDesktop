// Clipboard operations that go through C# ("the host") instead of navigator.clipboard.
//
// This is not just plumbing preference: Chromium silently discards clipboard failures.
// With the Windows clipboard held by another process (a clipboard manager, a remote-desktop
// client, another program mid-copy), navigator.clipboard.writeText() still RESOLVES,
// document.execCommand("copy") still returns true, and navigator.clipboard.readText()
// resolves with an empty string -- indistinguishable from an empty clipboard. So a failed
// copy in the browser is completely undetectable from Javascript, and the user is left
// thinking they copied something they didn't (BL-16459).
//
// The WinForms clipboard C# uses does throw, so routing through C# is what lets a failure
// be noticed. C# reports it as a toast; see BloomClipboard.cs. Nothing is lost by going
// this way: these calls only ever handled plain text anyway.

import { AxiosResponse } from "axios";
import { getAsync, postJson, postJsonAsync } from "../../utils/bloomApi";

// Put text on the clipboard. Resolves true if it actually got there. False covers both "the
// clipboard refused it" -- in which case C# has already told the user -- and "there was nothing
// to copy", which C# reports as false without bothering anyone. Either way a caller that is
// about to destroy the original (cut) should leave it alone.
export async function copyTextToClipboard(text: string): Promise<boolean> {
    const response = await postJsonAsync("common/clipboardText", { text });
    return (response as AxiosResponse | undefined)?.data === true;
}

// The clipboard endpoint replies with text/plain, but axios runs JSON.parse over every response
// body by default, so a clipboard holding 42 would arrive as a number, one holding true as a
// boolean, and one holding {"a":1} as an object -- and then be discarded as "not a string",
// meaning pasting a number silently inserted nothing. Ask for the body exactly as sent.
const kReplyIsPlainText = { transformResponse: [(body: string) => body] };

// Read text from the clipboard on the user's behalf, i.e. for an actual paste, or "" if there
// is none. If the clipboard could not be read at all, C# has told the user and we get "" here
// too -- so callers do nothing, which is the same thing they would do with an empty clipboard.
export async function readTextFromClipboard(): Promise<string> {
    const response = await getAsync("common/clipboardText", kReplyIsPlainText);
    return typeof response.data === "string" ? response.data : "";
}

// Read the clipboard only to find out whether there is text to paste -- to decide whether to
// enable a menu item or a button -- rather than to paste it. C# deliberately stays silent about
// a failure here: these checks run on their own schedule (when a canvas element's menu opens,
// while a page initializes), and the user has not asked for anything, so a clipboard held for a
// moment by another program must not produce an "unable to paste" message out of nowhere.
export async function readClipboardTextForAvailabilityCheck(): Promise<string> {
    const response = await getAsync(
        "common/clipboardText?checkingAvailability=true",
        kReplyIsPlainText,
    );
    return typeof response.data === "string" ? response.data : "";
}

// Ask C# to check whether the clipboard is usable, and toast if it isn't, after the browser
// has done a copy of its own. Deliberately fire-and-forget: there is nothing for us to do
// with the answer, and we must not delay the editing gesture.
export function verifyBrowserCopyReachedClipboard(): void {
    postJson("common/verifyClipboardAfterBrowserCopy", {});
}

// The same for a browser-performed paste, differing only in which message the user gets.
export function verifyBrowserPasteGotClipboard(): void {
    postJson("common/verifyClipboardAfterBrowserPaste", {});
}

// The browser does its clipboard work as part of the default action, after our handler returns,
// so a check has to wait for the next turn of the event loop. These are the right way to ask
// "did what the browser just did actually work?".
export function verifyBrowserCopyAfterDefault(): void {
    window.setTimeout(verifyBrowserCopyReachedClipboard, 0);
}

export function verifyBrowserPasteAfterDefault(): void {
    window.setTimeout(verifyBrowserPasteGotClipboard, 0);
}

// Watch for copies and cuts the browser performs itself, so each one gets verified. We do not
// preventDefault or touch the data: Chromium's own copy is better than anything we would
// reconstruct (it carries the HTML flavor, images, and whatever else was selected). We only look
// afterwards to see whether it can have worked.
//
// Note there is deliberately no "skip it if the event was defaultPrevented" check here. That
// seems the obvious way to avoid reporting twice when Bloom handles a clipboard event itself,
// and it silently defeats the whole feature: CKEditor calls preventDefault on copy and cut
// inside a .bloom-editable, which is precisely where the user is when this matters. Reporting
// twice is harmless anyway -- both reports carry the same message, and the toast host shows one.
//
// These also do not cover Ctrl+V, whose event CKEditor swallows before it reaches the document
// at all; keyboard shortcuts are handled from the keydown handler in bloomEditing.ts.
export function listenForBrowserClipboardOperations(doc: Document): void {
    doc.addEventListener("copy", verifyBrowserCopyAfterDefault);
    doc.addEventListener("cut", verifyBrowserCopyAfterDefault);
    doc.addEventListener("paste", verifyBrowserPasteAfterDefault);
}
