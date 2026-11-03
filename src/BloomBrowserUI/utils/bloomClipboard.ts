// Every clipboard read and write the page makes goes through this module, in place of
// navigator.clipboard.
//
// In a normal run it simply uses navigator.clipboard. Under --e2e it uses the in-memory clipboard
// that Bloom's C# keeps in BloomClipboard (BloomClipboard.cs), through the e2e/clipboard endpoint,
// so that an e2e run never touches the clipboard of the Windows session it runs in. The browser's
// own copy and paste (Ctrl+C and Ctrl+V pressed in a text box) cannot be redirected like this;
// an e2e test pastes by sending the paste event itself (see src/BloomE2E/helpers/clipboard.ts).
import { getAsync, postJsonAsync } from "./bloomApi";

/** What Bloom's in-memory clipboard holds under --e2e; null for each form it does not hold. */
interface IE2eClipboardContents {
    text: string | null;
    html: string | null;
    picturePath: string | null;
    files: string[] | null;
}

let runningE2eTests: Promise<boolean> | undefined;

/**
 * Whether this Bloom runs under --e2e. Asked once and remembered, unless asking fails: a failure
 * is not remembered, so that one failed request cannot break every later copy and paste on the
 * page.
 */
function isRunningE2eTests(): Promise<boolean> {
    if (!runningE2eTests) {
        runningE2eTests = getAsync("common/instanceInfo").then(
            (response) => !!response.data.runningE2eTests,
            (error) => {
                runningE2eTests = undefined;
                throw error;
            },
        );
    }
    return runningE2eTests;
}

async function getE2eContents(): Promise<IE2eClipboardContents> {
    return (await getAsync("e2e/clipboard")).data as IE2eClipboardContents;
}

/** The clipboard's plain text; an empty string when it has none. */
export async function readClipboardText(): Promise<string> {
    if (!(await isRunningE2eTests())) return navigator.clipboard.readText();
    return (await getE2eContents()).text ?? "";
}

/** Replace whatever the clipboard holds with this plain text. */
export async function writeClipboardText(text: string): Promise<void> {
    if (!(await isRunningE2eTests()))
        return navigator.clipboard.writeText(text);
    await postJsonAsync("e2e/clipboard", { text });
}

/**
 * The HTML that the clipboard holds, one string per clipboard item that has some; an empty list
 * when it has none.
 */
export async function readClipboardHtml(): Promise<string[]> {
    if (await isRunningE2eTests()) {
        const html = (await getE2eContents()).html;
        return html ? [html] : [];
    }
    const result: string[] = [];
    for (const item of await navigator.clipboard.read()) {
        if (!item.types.includes("text/html")) continue;
        result.push(await (await item.getType("text/html")).text());
    }
    return result;
}
