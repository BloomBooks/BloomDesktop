// Uploads the old translated xmatter pack descriptions (formerly description-<lang>.txt files in
// src/content/templates/xMatter/*) to Crowdin as suggestions for the XLF ids that replaced them.
// See README.md in this folder for when and how to run it.
//
// Usage (PowerShell, in one call, so the token never lands in a file or the shell history):
//   $env:BOT_TOKEN = [System.Environment]::GetEnvironmentVariable('<bot token variable>','User')
//   node docs/xmatter-descriptions-to-crowdin/upload.mjs           # dry run: shows what it would do
//   node docs/xmatter-descriptions-to-crowdin/upload.mjs --upload  # really upload
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const API = "https://api.crowdin.com/api/v2";
const PROJECT_ID = 261564; // sil-bloom
const BLOOM_XLF_FILE_ID = 34; // DistFiles/localization/en/Bloom.xlf
const reallyUpload = process.argv.includes("--upload");
const token = process.env.BOT_TOKEN;
if (!token)
    throw new Error("Set BOT_TOKEN to SILCrowdinBot's Crowdin token first.");

const here = dirname(fileURLToPath(import.meta.url));
const data = JSON.parse(readFileSync(join(here, "translations.json"), "utf8"));

/** Call the Crowdin REST API and return the parsed JSON body, throwing on any failure. */
async function crowdin(method, path, body, headers = {}) {
    const response = await fetch(API + path, {
        method,
        headers: {
            Authorization: `Bearer ${token}`,
            ...(body && !(body instanceof Uint8Array)
                ? { "Content-Type": "application/json" }
                : {}),
            ...headers,
        },
        body:
            body === undefined || body instanceof Uint8Array
                ? body
                : JSON.stringify(body),
    });
    const text = await response.text();
    if (!response.ok)
        throw new Error(`${method} ${path} failed: ${response.status} ${text}`);
    return text ? JSON.parse(text) : undefined;
}

/** Escape text for use inside an XML element or attribute. */
const xmlEscape = (s) =>
    s
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");

// Every write must go in as the bot, never a developer.
const me = (await crowdin("GET", "/user")).data;
if (me.username !== "SILCrowdinBot")
    throw new Error(
        `This token authenticates as ${me.username}, not SILCrowdinBot. Stopping.`,
    );

// The ids must already be in Crowdin, which happens only after the XLF change reaches master.
const ids = Object.keys(data);
const stringIdsById = {};
for (let offset = 0; ; offset += 500) {
    const page = await crowdin(
        "GET",
        `/projects/${PROJECT_ID}/strings?fileId=${BLOOM_XLF_FILE_ID}&limit=500&offset=${offset}`,
    );
    for (const s of page.data)
        if (ids.includes(s.data.identifier))
            stringIdsById[s.data.identifier] = s.data.id;
    if (page.data.length < 500) break;
}
const missing = ids.filter((id) => !(id in stringIdsById));
if (missing.length)
    throw new Error(
        `Not in Crowdin yet (has the XLF change merged to master and synced?):\n${missing.join("\n")}`,
    );

// The repo's folder names (pbu, prs, zh-CN...) are Crowdin's "two letters code" for a language
// (crowdin.yml maps them that way), which is not always its language id.
const project = (await crowdin("GET", `/projects/${PROJECT_ID}`)).data;
const crowdinLanguageIdForFolder = {};
for (const lang of project.targetLanguages)
    crowdinLanguageIdForFolder[lang.twoLettersCode] = lang.id;
crowdinLanguageIdForFolder["zh-CN"] ??= "zh-CN";

const folders = [
    ...new Set(ids.flatMap((id) => Object.keys(data[id].translations))),
].sort();
for (const folder of folders) {
    const languageId = crowdinLanguageIdForFolder[folder];
    if (!languageId)
        throw new Error(`No Crowdin target language for folder "${folder}".`);
    // A file holding only our units, so the upload cannot touch any other string.
    const units = ids
        .filter((id) => data[id].translations[folder])
        .map(
            (id) =>
                `      <trans-unit id="${xmlEscape(id)}" sil:dynamic="true">\n` +
                `        <source xml:lang="en">${xmlEscape(data[id].english)}</source>\n` +
                `        <target xml:lang="${folder}" state="translated">${xmlEscape(data[id].translations[folder])}</target>\n` +
                `      </trans-unit>\n`,
        );
    const xlf =
        `<?xml version="1.0" encoding="utf-8"?>\n` +
        `<xliff version="1.2" xmlns="urn:oasis:names:tc:xliff:document:1.2" xmlns:sil="http://sil.org/software/XLiff">\n` +
        `  <file original="Bloom.exe" source-language="en" datatype="plaintext" target-language="${folder}">\n` +
        `    <body>\n${units.join("")}    </body>\n  </file>\n</xliff>\n`;
    if (xlf.includes("approved="))
        throw new Error(
            "Refusing to upload a file with an approved attribute.",
        );
    console.log(`${folder} -> ${languageId}: ${units.length} translation(s)`);
    if (!reallyUpload) continue;
    const storage = await crowdin(
        "POST",
        "/storages",
        new TextEncoder().encode(xlf),
        {
            "Content-Type": "application/octet-stream",
            "Crowdin-API-FileName": `xmatter-descriptions-${folder}.xlf`,
        },
    );
    await crowdin(
        "POST",
        `/projects/${PROJECT_ID}/translations/${languageId}`,
        {
            storageId: storage.data.id,
            fileId: BLOOM_XLF_FILE_ID,
            importEqSuggestions: false,
            autoApproveImported: false,
            translateHidden: false,
        },
    );
}
console.log(
    reallyUpload
        ? "Uploaded. Check attribution and that nothing is approved (see README.md)."
        : "Dry run only; pass --upload to upload.",
);
