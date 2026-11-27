// Drive Publish: BloomPUB's "Make All BloomPUBs from Collection", and read what it made.
//
// The command sits on the BloomPUB screen and needs an enterprise subscription
// (FeatureRegistry's "BulkBloomPub"). It opens a dialog (BulkBloomPubDialog.tsx) with three
// options: a .bloomshelf file for the collection's bookshelf, a distribution tag, and whether to
// compress everything into one .bloombundle. Make closes the dialog and starts the work behind the
// BloomPUB screen's progress box (BulkBloomPubCreator.PublishAllBooksAsync).
//
// Bloom writes the files into %TEMP%\BloomPubs, with no save dialog, and names the folder in the
// progress box ("Creating files in <folder>..."). It empties that folder first (TemporaryFolder),
// and every Bloom of the same Windows user shares it, so a person's own bulk export may still be
// sitting there. makeAllBloomPubs therefore moves any folder already there aside before Make, reads
// what Bloom made into memory, and puts the person's folder back. Bloom then opens the folder in
// File Explorer (BulkBloomPubCreator, also under --e2e), and makeAllBloomPubs closes that window,
// and no Explorer window that was open before Make, before it touches the folder.
//
// A .bloombundle is an uncompressed tar of the .bloompub files and the .bloomshelf file
// (BloomTarArchive); a .bloompub is a zip. Bloom Reader reads both, so readBloomBundle and
// readZip check the files in the form Bloom Reader receives them.

import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { inflateRawSync } from "node:zlib";
import { expect, type Locator, type Page } from "@playwright/test";
import {
    closeNewExplorerWindowsOnFolder,
    listExplorerWindowHandles,
} from "./fileExplorer";
import { realClick } from "./realClick";
import { selectPublishDestination } from "./publish";

/** The dialog's title, which is also the label of the command that opens it. */
const DIALOG_TITLE = "Make All BloomPUBs from Collection";

/** The labels of the dialog's two check boxes (BulkBloomPubDialog.tsx). */
const BOOKSHELF_FILE_LABEL = "Produce a .bloomshelf file";
const BLOOM_BUNDLE_LABEL = "Compress into a single .bloombundle file";

/** The folder Bloom makes the files in: TemporaryFolder("BloomPubs") in BulkBloomPubCreator. */
const OUTPUT_FOLDER = Path.join(os.tmpdir(), "BloomPubs");

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

/** What one run of Make All BloomPUBs made. */
export interface IMakeAllBloomPubsResult {
    /** The files Bloom made, by name, with their contents, read before the folder was restored. */
    files: Map<string, Buffer>;
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
 * last message is "Done." Returns the files Bloom made, and closes the box with its Close button.
 *
 * Bloom empties %TEMP%\BloomPubs before it writes there, so a folder already there is renamed
 * aside first and put back afterwards, whether or not the run succeeds. If it cannot be put back,
 * the error names where it is.
 */
export async function makeAllBloomPubs(
    page: Page,
): Promise<IMakeAllBloomPubsResult> {
    const setAside = fs.existsSync(OUTPUT_FOLDER)
        ? `${OUTPUT_FOLDER}-set-aside-by-e2e-${process.pid}-${Date.now()}`
        : undefined;
    if (setAside) fs.renameSync(OUTPUT_FOLDER, setAside);
    try {
        return await makeAllBloomPubsInto(page);
    } finally {
        fs.rmSync(OUTPUT_FOLDER, { recursive: true, force: true });
        if (setAside) {
            try {
                fs.renameSync(setAside, OUTPUT_FOLDER);
            } catch (error) {
                throw new Error(
                    `Could not put the folder that was in ${OUTPUT_FOLDER} back. It is in ` +
                        `${setAside}. (${error})`,
                );
            }
        }
    }
}

/** makeAllBloomPubs, once any earlier folder is out of the way. */
async function makeAllBloomPubsInto(
    page: Page,
): Promise<IMakeAllBloomPubsResult> {
    const dialog = bulkBloomPubDialog(page);
    const explorerWindowsBefore = await listExplorerWindowHandles();
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
    // Compared as real paths: the temp folder may be named in its short 8.3 form on one side.
    const realPath = (path: string) =>
        fs.existsSync(path) ? fs.realpathSync.native(path).toLowerCase() : path;
    if (realPath(folder) !== realPath(OUTPUT_FOLDER)) {
        throw new Error(
            `Bloom made the files in ${folder}, not in ${OUTPUT_FOLDER}, the folder this helper ` +
                "protects and reads.",
        );
    }
    // Bloom opens the folder in File Explorer once it is done: close that window, and only that
    // one. Explorer names the folder by its long path. This is cleanup, so finding no window
    // does not fail the export.
    await closeNewExplorerWindowsOnFolder(
        fs.realpathSync.native(OUTPUT_FOLDER),
        explorerWindowsBefore,
        15000,
    );

    const progressDialog = page
        .getByRole("dialog")
        .filter({ has: progressLog });
    await realClick(
        progressDialog.getByRole("button", { name: "Close", exact: true }),
    );
    await expect(progressDialog, "the progress box did not close").toBeHidden();

    const files = new Map<string, Buffer>();
    for (const name of fs.readdirSync(OUTPUT_FOLDER).sort()) {
        files.set(name, fs.readFileSync(Path.join(OUTPUT_FOLDER, name)));
    }
    return { files, log };
}

/** One file inside an archive: a .bloombundle or a zip. */
export interface IArchiveEntry {
    name: string;
    content: Buffer;
}

/**
 * Read the files a .bloombundle holds. It is a plain tar (BloomTarArchive, over SharpZipLib's
 * TarOutputStream), which writes a name longer than 100 bytes as a GNU "././@LongLink" entry
 * before the file it names, so that is read too.
 */
export function readBloomBundle(data: Buffer): IArchiveEntry[] {
    const entries: IArchiveEntry[] = [];
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
 * Read the files a zip holds, such as a .bloompub: their names from its central directory, and
 * their contents, stored or deflated. Throws when the data is not a zip or uses another method.
 */
export function readZip(zip: Buffer): IArchiveEntry[] {
    // The end-of-central-directory record is the last thing in a zip, followed by a comment of
    // up to 65535 bytes.
    let end = -1;
    for (
        let i = zip.length - 22;
        i >= Math.max(0, zip.length - 22 - 65535);
        i--
    ) {
        if (zip.readUInt32LE(i) === 0x06054b50) {
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
    const entries: IArchiveEntry[] = [];
    for (let i = 0; i < count; i++) {
        if (zip.readUInt32LE(offset) !== 0x02014b50) {
            throw new Error(
                `The zip's central directory is damaged at entry ${i}.`,
            );
        }
        const method = zip.readUInt16LE(offset + 10);
        const compressedSize = zip.readUInt32LE(offset + 20);
        const nameLength = zip.readUInt16LE(offset + 28);
        const extraLength = zip.readUInt16LE(offset + 30);
        const commentLength = zip.readUInt16LE(offset + 32);
        const localHeader = zip.readUInt32LE(offset + 42);
        const name = zip.toString(
            "utf8",
            offset + 46,
            offset + 46 + nameLength,
        );
        offset += 46 + nameLength + extraLength + commentLength;

        // The data follows the local header, whose name and extra field can differ in length
        // from the central directory's.
        const dataStart =
            localHeader +
            30 +
            zip.readUInt16LE(localHeader + 26) +
            zip.readUInt16LE(localHeader + 28);
        const raw = zip.subarray(dataStart, dataStart + compressedSize);
        if (method !== 0 && method !== 8) {
            throw new Error(
                `${name} in the zip uses compression method ${method}, which this cannot read.`,
            );
        }
        entries.push({
            name,
            content: method === 8 ? inflateRawSync(raw) : Buffer.from(raw),
        });
    }
    return entries;
}

/** The title a .bloompub's meta.json gives its book: what Bloom Reader lists it as. */
export function getBloomPubTitle(bloomPub: Buffer): string | undefined {
    const meta = readZip(bloomPub).find((entry) => entry.name === "meta.json");
    if (!meta) return undefined;
    return JSON.parse(meta.content.toString("utf8").replace(/^﻿/, "")).title;
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
    return JSON.parse(content.toString("utf8").replace(/^﻿/, ""));
}

/**
 * The video files a .bloompub carries: the files in its video folder, each named with that folder
 * ("video/name.mp4"), sorted by name. Bloom leaves out a video no page plays, and writes a trimmed
 * video as a new, shorter file.
 */
export function getBloomPubVideos(bloomPub: Buffer): IArchiveEntry[] {
    return readZip(bloomPub)
        .map((entry) => ({ ...entry, name: entry.name.replace(/\\/g, "/") }))
        .filter((entry) => entry.name.startsWith("video/"))
        .sort((a, b) => a.name.localeCompare(b.name));
}

/** The videos one page of a .bloompub plays. */
export interface IBloomPubPageVideos {
    /** The page's id, which is the id the page has in the book. */
    pageId: string;
    /**
     * What each video box on the page points to, in order, exactly as the page has it: a path
     * from the root of the .bloompub, such as "video/name.mp4", with a "#t=" fragment when the
     * video is trimmed. This is what a reader's device looks for in the .bloompub.
     */
    sources: string[];
}

/**
 * The pages of a .bloompub that play a video, in the order of the book, with what each plays.
 * `page` is used only as an HTML parser, as helpers/bookHtml.ts does.
 */
export async function getBloomPubPageVideos(
    page: Page,
    bloomPub: Buffer,
): Promise<IBloomPubPageVideos[]> {
    const bookFiles = readZip(bloomPub).filter((entry) =>
        entry.name.toLowerCase().endsWith(".htm"),
    );
    if (bookFiles.length !== 1)
        throw new Error(
            `The .bloompub should hold one .htm file. It holds: ${bookFiles.map((entry) => entry.name).join(", ")}.`,
        );
    return page.evaluate((source) => {
        const book = new DOMParser().parseFromString(source, "text/html");
        const pages: { pageId: string; sources: string[] }[] = [];
        for (const bookPage of book.querySelectorAll(".bloom-page")) {
            const sources = Array.from(
                bookPage.querySelectorAll("video source"),
            ).map((element) => element.getAttribute("src") ?? "");
            if (sources.length > 0)
                pages.push({ pageId: bookPage.id, sources });
        }
        return pages;
    }, bookFiles[0].content.toString("utf8"));
}
