// Downloads the report's test images into output/transparency-report/images/ and writes
// output/transparency-report/images.json, which the C# harness reads.
// The list of images is docs/transparency-report/images.json. An entry whose `source` is a
// bloomlibrary.org book URL is fetched from that book's files on Bloom Library; any other
// `source` is a path relative to the repo root.
// Usage: node docs/transparency-report/fetch-images.mjs
import { readFile, writeFile, mkdir, copyFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repoRoot = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    "../..",
);
const outDir = path.join(repoRoot, "output", "transparency-report");
const imagesDir = path.join(outDir, "images");
const bucket = "https://s3.amazonaws.com/BloomLibraryBooks";

const entries = JSON.parse(
    await readFile(
        path.join(repoRoot, "docs/transparency-report/images.json"),
        "utf8",
    ),
);
await mkdir(imagesDir, { recursive: true });

const listingCache = new Map();

/** Turns the text of an XML element back into the string it encodes (S3 escapes keys this way). */
function decodeXmlText(text) {
    return text.replace(
        /&(amp|lt|gt|quot|apos|#\d+|#x[0-9A-Fa-f]+);/g,
        (whole, name) => {
            if (name === "amp") return "&";
            if (name === "lt") return "<";
            if (name === "gt") return ">";
            if (name === "quot") return '"';
            if (name === "apos") return "'";
            if (name.startsWith("#x"))
                return String.fromCodePoint(parseInt(name.slice(2), 16));
            return String.fromCodePoint(parseInt(name.slice(1), 10));
        },
    );
}

/** Returns the S3 keys of the files at the top level of a book's folder on Bloom Library. */
async function listBookFiles(bookId) {
    if (!listingCache.has(bookId)) {
        // S3 returns at most 1000 keys per request; a book with many audio files needs more
        // than one page, each starting after the last key of the one before.
        const keys = [];
        let marker = "";
        while (true) {
            const response = await fetch(
                `${bucket}?prefix=${bookId}/&max-keys=1000&marker=${encodeURIComponent(marker)}`,
            );
            if (!response.ok)
                throw new Error(`Listing ${bookId} failed: ${response.status}`);
            const xml = await response.text();
            const pageKeys = [...xml.matchAll(/<Key>([^<]*)<\/Key>/g)].map(
                (m) => decodeXmlText(m[1]),
            );
            keys.push(...pageKeys);
            if (!/<IsTruncated>true<\/IsTruncated>/.test(xml)) break;
            marker = pageKeys[pageKeys.length - 1];
        }
        listingCache.set(
            bookId,
            keys.filter((k) => k.split("/").length === 4),
        );
    }
    return listingCache.get(bookId);
}

for (const entry of entries) {
    const target = path.join(imagesDir, entry.file);
    const bookMatch = entry.source.match(
        /bloomlibrary\.org\/book\/([A-Za-z0-9]+)/,
    );
    if (bookMatch) {
        const keys = await listBookFiles(bookMatch[1]);
        // originalName may have had characters other than letters, digits, ".", "_" and "-"
        // replaced by "_", so compare names in that form.
        const simplify = (name) => name.replace(/[^A-Za-z0-9._-]/g, "_");
        const key = keys.find(
            (k) => simplify(k.split("/")[3]) === simplify(entry.originalName),
        );
        if (!key)
            throw new Error(
                `${entry.id}: ${entry.originalName} is no longer in book ${bookMatch[1]}`,
            );
        const url = `${bucket}/${key.split("/").map(encodeURIComponent).join("/")}`;
        const response = await fetch(url);
        if (!response.ok)
            throw new Error(`${entry.id}: download failed: ${response.status}`);
        await writeFile(target, Buffer.from(await response.arrayBuffer()));
    } else {
        await copyFile(path.join(repoRoot, entry.source), target);
    }
    console.log(`${entry.id} <- ${entry.originalName}`);
}
await writeFile(
    path.join(outDir, "images.json"),
    JSON.stringify(entries, null, 1),
);
console.log(`${entries.length} images in ${imagesDir}`);
