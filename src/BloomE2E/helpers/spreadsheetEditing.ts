// Edit a spreadsheet Bloom exported, the way a person does in their spreadsheet program before
// importing it again. A test has no spreadsheet program to drive, so this rewrites the .xlsx file
// itself: an .xlsx is a zip of XML parts, and the rows are in xl/worksheets/sheet1.xml, with their
// text in xl/sharedStrings.xml.

import * as fs from "node:fs";
import * as zlib from "node:zlib";

/** One file in a zip. */
export interface IZipEntry {
    name: string;
    data: Buffer;
}

/** The files of the zip in `zip`, in order. Handles stored and deflated files, which is all a .xlsx uses. */
function readZip(zip: Buffer): IZipEntry[] {
    // The end-of-central-directory record is the last thing in the file, followed only by a
    // comment of up to 64 KB.
    let end = zip.length - 22;
    while (end >= 0 && zip.readUInt32LE(end) !== 0x06054b50) end--;
    if (end < 0)
        throw new Error("This is not a zip file: it has no central directory.");
    const count = zip.readUInt16LE(end + 10);
    let at = zip.readUInt32LE(end + 16);
    const entries: IZipEntry[] = [];
    for (let i = 0; i < count; i++) {
        if (zip.readUInt32LE(at) !== 0x02014b50)
            throw new Error(`Zip central directory entry ${i} is damaged.`);
        const method = zip.readUInt16LE(at + 10);
        const compressedSize = zip.readUInt32LE(at + 20);
        const nameLength = zip.readUInt16LE(at + 28);
        const extraLength = zip.readUInt16LE(at + 30);
        const commentLength = zip.readUInt16LE(at + 32);
        const localHeader = zip.readUInt32LE(at + 42);
        const name = zip.toString("utf8", at + 46, at + 46 + nameLength);
        const dataStart =
            localHeader +
            30 +
            zip.readUInt16LE(localHeader + 26) +
            zip.readUInt16LE(localHeader + 28);
        const raw = zip.subarray(dataStart, dataStart + compressedSize);
        let data: Buffer;
        if (method === 0) data = Buffer.from(raw);
        else if (method === 8) data = zlib.inflateRawSync(raw);
        else
            throw new Error(
                `${name} in the zip uses compression method ${method}, which this cannot read.`,
            );
        entries.push({ name, data });
        at += 46 + nameLength + extraLength + commentLength;
    }
    return entries;
}

/** A zip holding `entries`, each deflated. */
export function writeZip(entries: IZipEntry[]): Buffer {
    const locals: Buffer[] = [];
    const centrals: Buffer[] = [];
    let offset = 0;
    for (const { name, data } of entries) {
        const nameBytes = Buffer.from(name, "utf8");
        const compressed = zlib.deflateRawSync(data);
        const crc = zlib.crc32(data);
        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4); // version needed
        local.writeUInt16LE(0x0800, 6); // names are UTF-8
        local.writeUInt16LE(8, 8); // deflated
        local.writeUInt32LE(crc, 14);
        local.writeUInt32LE(compressed.length, 18);
        local.writeUInt32LE(data.length, 22);
        local.writeUInt16LE(nameBytes.length, 26);
        locals.push(local, nameBytes, compressed);

        const central = Buffer.alloc(46);
        central.writeUInt32LE(0x02014b50, 0);
        central.writeUInt16LE(20, 4); // version made by
        central.writeUInt16LE(20, 6); // version needed
        central.writeUInt16LE(0x0800, 8);
        central.writeUInt16LE(8, 10);
        central.writeUInt32LE(crc, 16);
        central.writeUInt32LE(compressed.length, 20);
        central.writeUInt32LE(data.length, 24);
        central.writeUInt16LE(nameBytes.length, 28);
        central.writeUInt32LE(offset, 42);
        centrals.push(central, nameBytes);

        offset += local.length + nameBytes.length + compressed.length;
    }
    const centralDirectory = Buffer.concat(centrals);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(entries.length, 8);
    end.writeUInt16LE(entries.length, 10);
    end.writeUInt32LE(centralDirectory.length, 12);
    end.writeUInt32LE(offset, 16);
    return Buffer.concat([...locals, centralDirectory, end]);
}

/** The text of each shared string of the workbook, by index. */
function readSharedStrings(xml: string): string[] {
    const decode = (text: string) =>
        text
            .replace(/&lt;/g, "<")
            .replace(/&gt;/g, ">")
            .replace(/&quot;/g, '"')
            .replace(/&apos;/g, "'")
            .replace(/&amp;/g, "&");
    return Array.from(xml.matchAll(/<si>([\s\S]*?)<\/si>/g), (si) =>
        decode(
            Array.from(
                si[1].matchAll(/<t[^>]*>([\s\S]*?)<\/t>/g),
                (t) => t[1],
            ).join(""),
        ),
    );
}

/** The column letters of a cell reference such as "AB12". */
function columnOf(cellRef: string): string {
    return cellRef.replace(/\d+$/, "");
}

/**
 * Delete the rows of the spreadsheet at `xlsxPath` that hold the book page Bloom exported as page
 * `pageNumber` (its "(exported page)" column), as a person does by selecting
 * those rows in their spreadsheet program and choosing Delete: the rows below move up to close the
 * gap. The result is saved as `savePath`, which should be in the same folder so that the
 * spreadsheet's pictures are still beside it: the export's own file stays open in the spreadsheet
 * program Bloom opened it in, which keeps it locked. Returns how many rows went; throws when there
 * were none, so a test cannot go on with a spreadsheet it did not change.
 */
export function deleteSpreadsheetPageRows(
    xlsxPath: string,
    pageNumber: number,
    savePath: string,
): number {
    const entries = readZip(fs.readFileSync(xlsxPath));
    const part = (name: string) => {
        const entry = entries.find((e) => e.name === name);
        if (!entry) throw new Error(`${xlsxPath} has no ${name}.`);
        return entry;
    };
    const sheet = part("xl/worksheets/sheet1.xml");
    const strings = readSharedStrings(
        part("xl/sharedStrings.xml").data.toString("utf8"),
    );
    const xml = sheet.data.toString("utf8");

    const rows = Array.from(
        xml.matchAll(/<row r="(\d+)"[^>]*?(?:\/>|>([\s\S]*?)<\/row>)/g),
        (m) => ({ text: m[0], number: Number(m[1]), body: m[2] ?? "" }),
    );
    /** The value of each cell of a row, by its column letters. */
    const cellsOf = (body: string) =>
        new Map(
            Array.from(
                body.matchAll(
                    /<c r="([A-Z]+\d+)"([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g,
                ),
                (c) => {
                    const value =
                        /<v>([\s\S]*?)<\/v>/.exec(c[3] ?? "")?.[1] ?? "";
                    return [
                        columnOf(c[1]),
                        /\bt="s"/.test(c[2]) ? strings[Number(value)] : value,
                    ] as const;
                },
            ),
        );
    // Bloom's first row holds the key of each column.
    const keys = cellsOf(rows[0].body);
    const columnWithKey = (key: string) => {
        const column = Array.from(keys).find(([, value]) => value === key)?.[0];
        if (!column) throw new Error(`${xlsxPath} has no "${key}" column.`);
        return column;
    };
    const pageColumn = columnWithKey("(exported page)");

    // Every row of the page, whatever its type: a picture's description has a row of its own
    // ("[image description]") with the same page number as the page's content rows.
    const doomed = new Set(
        rows
            .filter(
                (row) =>
                    cellsOf(row.body).get(pageColumn) === String(pageNumber),
            )
            .map((row) => row.number),
    );
    if (doomed.size === 0)
        throw new Error(`${xlsxPath} has no rows of page ${pageNumber}.`);

    let deletedSoFar = 0;
    let newXml = xml;
    for (const row of rows) {
        if (doomed.has(row.number)) {
            newXml = newXml.replace(row.text, () => "");
            deletedSoFar++;
        } else if (deletedSoFar > 0) {
            const moved = row.number - deletedSoFar;
            const movedText = row.text
                .replace(`<row r="${row.number}"`, `<row r="${moved}"`)
                .replace(
                    /(<c r="[A-Z]+)(\d+)"/g,
                    (_, column: string) => `${column}${moved}"`,
                );
            newXml = newXml.replace(row.text, () => movedText);
        }
    }
    const lastRow = rows.at(-1)!.number - deletedSoFar;
    newXml = newXml.replace(
        /<dimension ref="([A-Z]+\d+):([A-Z]+)\d+"/,
        (_, start: string, endColumn: string) =>
            `<dimension ref="${start}:${endColumn}${lastRow}"`,
    );
    sheet.data = Buffer.from(newXml, "utf8");
    fs.writeFileSync(savePath, writeZip(entries));
    return deletedSoFar;
}
