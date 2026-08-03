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

// Put text on the clipboard. Resolves true if it actually got there; if not, C# has
// already told the user, and callers that are about to destroy the original (cut) should
// leave it alone.
export async function copyTextToClipboard(text: string): Promise<boolean> {
    const response = await postJsonAsync("common/clipboardText", { text });
    return (response as AxiosResponse | undefined)?.data === true;
}

// Read text from the clipboard, or "" if there is none. If the clipboard could not be read
// at all, C# has told the user and we get "" here too -- so callers do nothing, which is
// the same thing they would do with an empty clipboard.
export async function readTextFromClipboard(): Promise<string> {
    const response = await getAsync("common/clipboardText");
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
