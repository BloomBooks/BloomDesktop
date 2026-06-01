import { readFileSync, writeFileSync } from "node:fs";
const dir = "C:/Users/hatto/AppData/Local/Temp/sib_xlsx";

function decode(s) {
    return s
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(+d))
        .replace(/&#x([0-9a-fA-F]+);/g, (_, h) =>
            String.fromCodePoint(parseInt(h, 16)),
        )
        .replace(/&amp;/g, "&");
}
const ssXml = readFileSync(`${dir}/xl/sharedStrings.xml`, "utf8");
const shared = [];
let m;
const siRe = /<si>([\s\S]*?)<\/si>/g;
while ((m = siRe.exec(ssXml))) {
    const tRe = /<t[^>]*>([\s\S]*?)<\/t>/g;
    let t,
        p = [];
    while ((t = tRe.exec(m[1]))) p.push(decode(t[1]));
    shared.push(p.join(""));
}
const xml = readFileSync(`${dir}/xl/worksheets/sheet1.xml`, "utf8");
const rows = [];
const rowRe = /<row[^>]*r="(\d+)"[^>]*>([\s\S]*?)<\/row>/g;
let r;
while ((r = rowRe.exec(xml))) {
    const cells = {};
    const cRe =
        /<c r="([A-Z]+)\d+"(?:[^>]*?\st="([^"]+)")?[^>]*>([\s\S]*?)<\/c>/g;
    let c;
    while ((c = cRe.exec(r[2]))) {
        const col = c[1],
            type = c[2],
            body = c[3];
        let val = "";
        const vm = /<v>([\s\S]*?)<\/v>/.exec(body);
        if (type === "s" && vm) val = shared[+vm[1]] ?? "";
        else if (vm) val = decode(vm[1]);
        cells[col] = val;
    }
    rows.push({ rowNum: +r[1], cells });
}

const COLS = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K"];
const KEYS = [
    "fr",
    "en",
    "latin",
    "size",
    "abundance",
    "habitat",
    "food",
    "url",
    "licenseRaw",
    "photographer",
    "copyright",
];
const data = rows
    .filter((r) => r.rowNum >= 2)
    .map((r) => {
        const o = { row: r.rowNum };
        COLS.forEach((c, i) => (o[KEYS[i]] = (r.cells[c] || "").trim()));
        return o;
    });

writeFileSync(
    "C:/Users/hatto/AppData/Local/Temp/birds.json",
    JSON.stringify(data, null, 1),
);
console.log("Total birds:", data.length);
console.log(
    "Missing URL:",
    data.filter((b) => !b.url).map((b) => b.row),
);
console.log(
    "Missing fr name:",
    data.filter((b) => !b.fr).map((b) => b.row),
);
const lic = {};
for (const b of data) lic[b.licenseRaw] = (lic[b.licenseRaw] || 0) + 1;
console.log("\nLicense values:");
for (const [k, v] of Object.entries(lic).sort((a, b) => b[1] - a[1]))
    console.log(`  ${v.toString().padStart(3)}  "${k}"`);
const exts = {};
for (const b of data) {
    const e = (b.url.split("?")[0].match(/\.([a-zA-Z0-9]+)$/) || [
        ,
        "?",
    ])[1].toLowerCase();
    exts[e] = (exts[e] || 0) + 1;
}
console.log("\nURL file extensions:");
for (const [k, v] of Object.entries(exts)) console.log(`  ${v}  .${k}`);
