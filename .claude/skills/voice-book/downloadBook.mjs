/* eslint-env node */
/* global console, process, fetch, Buffer */
// Downloads a book's source files from Bloom Library into a new collection of its own, the
// same way Bloom's "Download into Bloom" for editing does.
//
// Usage: node downloadBook.mjs <bloomlibrary.org book URL or book id> <folder to hold the new collection>
// Prints the paths of the new .bloomCollection file and book folder. Fails if the collection
// folder already exists.

import fs from "node:fs";
import path from "node:path";

// Bloom Library's public parse-server application id (read-only access to public book records).
const kParseAppId = "R6qNTeumQXjJCMutAJYAwPtip1qBulkFyLefkCE5";
const kBucket = "BloomLibraryBooks";

/** Gets the book id out of a URL such as https://bloomlibrary.org/language:swh/book/HRahJAASYF?lang=swh. */
function bookIdFrom(arg) {
    const match = /\/book\/([A-Za-z0-9]+)/.exec(arg);
    return match ? match[1] : arg;
}

/** Fetches the book's record from Bloom Library's parse server. */
async function getBookRecord(bookId) {
    const where = encodeURIComponent(JSON.stringify({ objectId: bookId }));
    const response = await fetch(
        `https://server.bloomlibrary.org/parse/classes/books?where=${where}&keys=title,baseUrl`,
        { headers: { "X-Parse-Application-Id": kParseAppId } },
    );
    if (!response.ok)
        throw new Error(
            `Parse server ${response.status}: ${await response.text()}`,
        );
    const results = (await response.json()).results;
    if (results.length !== 1) throw new Error(`No book with id ${bookId}`);
    return results[0];
}

/** Lists every object key under a prefix in the books bucket. */
async function listKeys(prefix) {
    const keys = [];
    let token;
    do {
        let url = `https://s3.amazonaws.com/${kBucket}?list-type=2&prefix=${encodeURIComponent(prefix)}`;
        if (token) url += `&continuation-token=${encodeURIComponent(token)}`;
        const xml = await (await fetch(url)).text();
        for (const m of xml.matchAll(/<Key>([^<]*)<\/Key>/g))
            keys.push(decodeXml(m[1]));
        token = /<NextContinuationToken>([^<]*)</.exec(xml)?.[1];
    } while (token);
    return keys;
}

/** Decodes the XML entities S3 uses in keys. */
function decodeXml(s) {
    return s
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&amp;/g, "&");
}

async function main() {
    const [bookArg, parentFolder] = process.argv.slice(2);
    if (!bookArg || !parentFolder) {
        console.error(
            "Usage: node downloadBook.mjs <bloomlibrary.org book URL or id> <folder to hold the new collection>",
        );
        process.exit(2);
    }
    const record = await getBookRecord(bookIdFrom(bookArg));
    // baseUrl looks like https://s3.amazonaws.com/BloomLibraryBooks/<id>%2f<timestamp>%2f<folder>%2f
    const prefix = decodeURIComponent(
        record.baseUrl.split(`/${kBucket}/`)[1].replace(/\+/g, " "),
    );
    const folderName = prefix.split("/").filter(Boolean).pop();
    // Named the way Bloom's own "Download into Bloom" names it (BookDownload.DownloadBook).
    const collectionName = `From Bloom Library - ${folderName}`
        .replace(/[<>:"/\\|?*]/g, " ")
        .slice(0, 50)
        .trim();
    const collectionFolder = path.join(parentFolder, collectionName);
    if (fs.existsSync(collectionFolder))
        throw new Error(`${collectionFolder} already exists`);
    const bookFolder = path.join(collectionFolder, folderName);

    // The PDF is a published artifact Bloom does not need, and it is often the largest file.
    const keys = (await listKeys(prefix)).filter(
        (k) => !k.endsWith("/") && !k.toLowerCase().endsWith(".pdf"),
    );
    for (const key of keys) {
        const dest = path.join(bookFolder, key.slice(prefix.length));
        fs.mkdirSync(path.dirname(dest), { recursive: true });
        const url = `https://s3.amazonaws.com/${kBucket}/${key.split("/").map(encodeURIComponent).join("/")}`;
        const response = await fetch(url);
        if (!response.ok)
            throw new Error(`Download of ${key} failed: ${response.status}`);
        fs.writeFileSync(dest, Buffer.from(await response.arrayBuffer()));
    }
    // The uploader saved the collection's settings with the book; they become the new collection's file.
    const collectionFiles = path.join(bookFolder, "collectionFiles");
    const collectionFile = path.join(
        collectionFolder,
        collectionName + ".bloomCollection",
    );
    fs.renameSync(
        path.join(collectionFiles, "book.uploadCollectionSettings"),
        collectionFile,
    );
    fs.rmSync(collectionFiles, { recursive: true });
    console.log(JSON.stringify({ collectionFile, bookFolder }, null, 2));
}

await main();
