// Drive Publish: BloomPUB's "Make All BloomPUBs from Collection", and read what it made.
//
// The command sits on the BloomPUB screen and needs an enterprise subscription
// (FeatureRegistry's "BulkBloomPub"). It opens a dialog (BulkBloomPubDialog.tsx) with three
// options: a .bloomshelf file for the collection's bookshelf, a distribution tag, and whether to
// compress everything into one .bloombundle. Make closes the dialog and starts the work behind the
// BloomPUB screen's progress box (BulkBloomPubCreator.PublishAllBooksAsync).
//
// Bloom writes the files into a temporary folder, %TEMP%\BloomPubs, which it empties first and
// names in the progress box ("Creating files in <folder>..."), with no save dialog. The helpers
// read the folder from the box. Bloom then opens the folder in File Explorer, and nothing here
// closes that window (AUTOMATION-DEBT.md, "Make All BloomPUBs opens File Explorer").
//
// A .bloombundle is an uncompressed tar of the .bloompub files and the .bloomshelf file
// (BloomTarArchive); a .bloompub is a zip. Bloom Reader reads both, so readBloomBundle and
// listZipEntries check the files in the form Bloom Reader receives them.

import * as fs from "node:fs";
import { expect, type Locator, type Page } from "@playwright/test";
import { realClick } from "./realClick";
import { selectPublishDestination } from "./publish";

/** The dialog's title, which is also the label of the command that opens it. */
const DIALOG_TITLE = "Make All BloomPUBs from Collection";

/** The labels of the dialog's two check boxes (BulkBloomPubDialog.tsx). */
const BOOKSHELF_FILE_LABEL = "Produce a .bloomshelf file";
const BLOOM_BUNDLE_LABEL = "Compress into a single .bloombundle file";

/** The Make All BloomPUBs dialog, once open. */
function bulkBloomPubDialog(page: Page): Locator {
    return page.getByRole("dialog").filter({ hasText: DIALOG_TITLE });
}

/**
 * Go to Publish: BloomPUB and click "Make All BloomPUBs from Collection". Returns once the dialog
 * shows its options, which it fills in only after Bloom answers with the collection's saved ones.
 * A book must be selected, and the collection must have an enterprise subscription, or the command
 * offers an upgrade instead.
 */
export async function openMakeAllBloomPubsDialog(page: Page): Promise<void> {
    await selectPublishDestination(page, "BloomPUB");
    // A link-styled command with no href (LinkWithDisabledStyles), so it has no link role. In a
    // narrow window its text wraps onto two lines, and the middle of its box is then off the text,
    // so it is clicked with Playwright's click, which aims at the text itself.
    const command = page.getByText(DIALOG_TITLE, { exact: true });
    await command.waitFor({ state: "visible", timeout: 60000 });
    await command.click();
    await expect(
        bulkBloomPubDialog(page).getByLabel(BLOOM_BUNDLE_LABEL),
        `"${DIALOG_TITLE}" did not open with its options`,
    ).toBeVisible({ timeout: 30000 });
}

/** One of the dialog's check boxes, by which option it is. */
function bulkBloomPubCheckbox(
    page: Page,
    option: "bookshelfFile" | "bloomBundle",
): Locator {
    return bulkBloomPubDialog(page).getByLabel(
        option === "bookshelfFile" ? BOOKSHELF_FILE_LABEL : BLOOM_BUNDLE_LABEL,
    );
}

/**
 * Tick or untick one of the dialog's check boxes, "Produce a .bloomshelf file" or "Compress into a
 * single .bloombundle file", by clicking it as a person does. Returns once the box shows the state
 * asked for. The .bloomshelf box is enabled only when the collection has a bookshelf.
 */
export async function setMakeAllBloomPubsOption(
    page: Page,
    option: "bookshelfFile" | "bloomBundle",
    checked: boolean,
): Promise<void> {
    const box = bulkBloomPubCheckbox(page, option);
    await expect(box, `the ${option} check box is disabled`).toBeEnabled();
    if ((await box.isChecked()) !== checked) {
        await realClick(box);
    }
    await expect(box).toBeChecked({ checked });
}

/** What one run of Make All BloomPUBs left behind. */
export interface IMakeAllBloomPubsResult {
    /** The temporary folder Bloom made the files in, as it named it in the progress box. */
    folder: string;
    /** The files in that folder, by name, sorted. */
    files: string[];
    /** Everything the progress box said, one message to a line, for a failure message. */
    log: string;
}

/** The messages the progress box is showing, one to each paragraph, in order. */
async function getProgressMessages(progressLog: Locator): Promise<string[]> {
    return (await progressLog.locator("p").allInnerTexts()).map((message) =>
        message.trim(),
    );
}

/**
 * Click Make in the open Make All BloomPUBs dialog and wait for Bloom to finish: the progress box's
 * last message is "Done." Reads which folder Bloom made the files in from the box's first message,
 * and closes the box with its Close button.
 */
export async function makeAllBloomPubs(
    page: Page,
): Promise<IMakeAllBloomPubsResult> {
    const dialog = bulkBloomPubDialog(page);
    await realClick(dialog.getByRole("button", { name: "Make", exact: true }));
    await expect(
        dialog,
        `"${DIALOG_TITLE}" did not close after Make`,
    ).toBeHidden();

    const progressLog = page.getByTestId("progress-box-log");
    await expect
        .poll(async () => (await getProgressMessages(progressLog)).at(-1), {
            timeout: 180000,
            message: "Make All BloomPUBs never said it was done",
        })
        .toBe("Done.");
    const messages = await getProgressMessages(progressLog);
    const log = messages.join("\n");
    const folder = messages
        .map((message) => /^Creating files in (.+)\.\.\.$/.exec(message)?.[1])
        .find((path) => path !== undefined);
    if (!folder) {
        throw new Error(
            `The progress box never named the folder it made the files in. It said:\n${log}`,
        );
    }

    const progressDialog = page
        .getByRole("dialog")
        .filter({ has: progressLog });
    await realClick(
        progressDialog.getByRole("button", { name: "Close", exact: true }),
    );
    await expect(progressDialog, "the progress box did not close").toBeHidden();

    return { folder, files: fs.readdirSync(folder).sort(), log };
}

/** One file inside a .bloombundle. */
export interface IBundleEntry {
    name: string;
    content: Buffer;
}

/**
 * Read the files a .bloombundle holds. It is a plain tar (BloomTarArchive, over SharpZipLib's
 * TarOutputStream), which writes a name longer than 100 bytes as a GNU "././@LongLink" entry
 * before the file it names, so that is read too.
 */
export function readBloomBundle(bundlePath: string): IBundleEntry[] {
    const data = fs.readFileSync(bundlePath);
    const entries: IBundleEntry[] = [];
    let offset = 0;
    let longName: string | undefined;
    while (offset + 512 <= data.length) {
        const header = data.subarray(offset, offset + 512);
        if (header.every((b) => b === 0)) break; // the end-of-archive blocks
        const field = (start: number, length: number) =>
            header
                .subarray(start, start + length)
                .toString("utf8")
                .replace(/\0.*$/s, "");
        const size = parseInt(field(124, 12).trim() || "0", 8);
        const type = field(156, 1);
        const prefix = field(345, 155);
        const content = data.subarray(offset + 512, offset + 512 + size);
        offset += 512 + Math.ceil(size / 512) * 512;
        if (type === "L") {
            longName = content.toString("utf8").replace(/\0.*$/s, "");
            continue;
        }
        const shortName = prefix ? `${prefix}/${field(0, 100)}` : field(0, 100);
        const name = longName ?? shortName;
        longName = undefined;
        if (type === "0" || type === "") {
            entries.push({ name, content: Buffer.from(content) });
        }
    }
    return entries;
}

/**
 * The names of the files a zip holds, such as a .bloompub, read from its central directory.
 * Throws when the data is not a zip.
 */
export function listZipEntries(zip: Buffer): string[] {
    // The end-of-central-directory record is the last thing in a zip, followed by a comment of
    // up to 65535 bytes.
    const kEndSignature = 0x06054b50;
    let end = -1;
    for (
        let i = zip.length - 22;
        i >= Math.max(0, zip.length - 22 - 65535);
        i--
    ) {
        if (zip.readUInt32LE(i) === kEndSignature) {
            end = i;
            break;
        }
    }
    if (end < 0) {
        throw new Error(
            "The data is not a zip: it has no end of central directory.",
        );
    }
    const count = zip.readUInt16LE(end + 10);
    let offset = zip.readUInt32LE(end + 16);
    const names: string[] = [];
    for (let i = 0; i < count; i++) {
        if (zip.readUInt32LE(offset) !== 0x02014b50) {
            throw new Error(
                `The zip's central directory is damaged at entry ${i}.`,
            );
        }
        const nameLength = zip.readUInt16LE(offset + 28);
        const extraLength = zip.readUInt16LE(offset + 30);
        const commentLength = zip.readUInt16LE(offset + 32);
        names.push(zip.toString("utf8", offset + 46, offset + 46 + nameLength));
        offset += 46 + nameLength + extraLength + commentLength;
    }
    return names;
}

/** What a .bloomshelf file says about the bookshelf Bloom Reader groups the books under. */
export interface IBookshelfFile {
    /** The bookshelf's url key, the collection's bookshelf in Settings. */
    id: string;
    /** The bookshelf's name in each language, such as [{ en: "Test Bookshelf 1" }]. */
    label: Record<string, string>[];
    /** Its color, as hex digits with no "#". */
    color: string;
}

/** Read a .bloomshelf file's JSON, which Bloom writes in UTF-8 with a byte order mark. */
export function readBookshelfFile(content: Buffer): IBookshelfFile {
    return JSON.parse(content.toString("utf8").replace(/^\uFEFF/, ""));
}
