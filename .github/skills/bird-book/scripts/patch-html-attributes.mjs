import fs from "node:fs";
const bookPath =
    "C:/Users/hatto/OneDrive/Documents/Bloom/Wildlife Identifier Books/Identification de la faune/Identification de la faune.htm";
const results = JSON.parse(
    fs.readFileSync(
        "C:/Users/hatto/AppData/Local/Temp/meta_results.json",
        "utf8",
    ),
);
const byFile = new Map(results.map((r) => [r.file, r]));

const esc = (s) =>
    (s || "")
        .replace(/&/g, "&amp;")
        .replace(/"/g, "&quot;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");

let html = fs.readFileSync(bookPath, "utf8");
let patched = 0,
    skipped = 0;
html = html.replace(/<img\b[^>]*?>/g, (tag) => {
    const src = (tag.match(/\ssrc="([^"]+)"/) || [, ""])[1];
    const r = byFile.get(src);
    if (!r) {
        skipped++;
        return tag;
    }
    // strip any existing IP attrs, then append fresh ones
    let t = tag.replace(/\s+data-(?:copyright|creator|license)="[^"]*"/g, "");
    const attrs = ` data-copyright="${esc(r.copyright)}" data-creator="${esc(r.creator)}" data-license="${esc(r.license)}"`;
    t = t.replace(/\s*\/?>\s*$/, attrs + " />");
    patched++;
    return t;
});

fs.writeFileSync(bookPath, html, { encoding: "utf8" });
console.log(
    `patched ${patched} img tags, skipped ${skipped} (placeholders/non-bird).`,
);
