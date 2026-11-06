// Builds output/transparency-report/report/index.html from images.json and results.json,
// which the explicit C# test TransparencyReportTests writes. See README.md in this folder.
// Uses only Node built-ins so that it runs without installing anything.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
);
const reportRoot = path.join(repoRoot, "output", "transparency-report");
const outDir = path.join(reportRoot, "out");
// Options:
//   --colours <list>   only these backgrounds, a comma-separated list of "#RRGGBB" or "white",
//                      shown in the order given (otherwise backgrounds run lightest first)
//   --methods-shown <list>  tick only these methods when the page opens (comma-separated ids);
//                      the others can still be ticked in the page
//   --page-dir <dir>   write the page here instead of output/transparency-report/report
//   --artifact         write the page body only (no <html>, <head> or <body>), for publishing
//                      as a claude.ai artifact, which supplies those itself
const args = process.argv.slice(2);
function optionValue(name) {
    const i = args.indexOf(name);
    return i >= 0 ? args[i + 1] : undefined;
}
const chosenColours = optionValue("--colours")
    ?.split(",")
    .map((c) =>
        c.trim().toLowerCase() === "white" ? "#FFFFFF" : c.trim().toUpperCase(),
    );
const forArtifact = args.includes("--artifact");
const methodsShown = optionValue("--methods-shown")
    ?.split(",")
    .map((m) => m.trim());
const pageDir = path.resolve(
    optionValue("--page-dir") ?? path.join(reportRoot, "report"),
);
const sizeLimitBytes = 60 * 1024 * 1024;

const images = JSON.parse(
    fs.readFileSync(path.join(reportRoot, "images.json"), "utf8"),
);
const results = JSON.parse(
    fs.readFileSync(path.join(reportRoot, "results.json"), "utf8"),
);
const resultsById = new Map(results.images.map((r) => [r.id, r]));

// ---- Background colours ----

// CIE L*a*b* (D65) to sRGB components in 0..1, without clamping.
function labToSrgb(L, a, b) {
    const fy = (L + 16) / 116;
    const fx = fy + a / 500;
    const fz = fy - b / 200;
    const finv = (t) => (t ** 3 > 0.008856 ? t ** 3 : (t - 16 / 116) / 7.787);
    const X = 0.95047 * finv(fx);
    const Y = 1.0 * finv(fy);
    const Z = 1.08883 * finv(fz);
    const lin = [
        3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z,
        -0.969266 * X + 1.8760108 * Y + 0.041556 * Z,
        0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z,
    ];
    return lin.map((c) =>
        c <= 0.0031308 ? 12.92 * c : 1.055 * c ** (1 / 2.4) - 0.055,
    );
}

// L* of an sRGB colour given as "#RRGGBB".
function lightnessOfHex(hex) {
    const channels = [1, 3, 5].map(
        (i) => parseInt(hex.slice(i, i + 2), 16) / 255,
    );
    const lin = channels.map((c) =>
        c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4,
    );
    const Y = 0.2126729 * lin[0] + 0.7151522 * lin[1] + 0.072175 * lin[2];
    const fy = Y > 0.008856 ? Math.cbrt(Y) : 7.787 * Y + 16 / 116;
    return 116 * fy - 16;
}

function toHex(srgb) {
    return (
        "#" +
        srgb
            .map((c) =>
                Math.round(Math.min(1, Math.max(0, c)) * 255)
                    .toString(16)
                    .padStart(2, "0"),
            )
            .join("")
            .toUpperCase()
    );
}

// The colour with this L* and hue angle, at the given chroma or, when that is outside the
// sRGB range, at the highest chroma that is inside it. This keeps L* and hue exact.
function colourAt(L, chroma, hueDegrees) {
    const h = (hueDegrees * Math.PI) / 180;
    const inGamut = (c) =>
        labToSrgb(L, c * Math.cos(h), c * Math.sin(h)).every(
            (v) => v >= -1e-6 && v <= 1 + 1e-6,
        );
    let c = chroma;
    if (!inGamut(c)) {
        let low = 0;
        let high = chroma;
        for (let i = 0; i < 30; i++) {
            const mid = (low + high) / 2;
            if (inGamut(mid)) low = mid;
            else high = mid;
        }
        c = low;
    }
    return toHex(labToSrgb(L, c * Math.cos(h), c * Math.sin(h)));
}

const hues = [
    { name: "warm", chroma: 35, hue: 60 },
    { name: "cool", chroma: 35, hue: 230 },
    { name: "near grey", chroma: 4, hue: 80 },
];
const backgrounds = [];
for (const L of [95, 85, 75, 65, 55, 45, 35, 25]) {
    for (const h of hues) {
        backgrounds.push({
            hex: colourAt(L, h.chroma, h.hue),
            label: `${h.name}`,
            group: "range",
        });
    }
}
backgrounds.push({
    hex: "#B2DEE3",
    label: "cover in the bug report",
    group: "bug",
});
backgrounds.push({ hex: "#FFFFFF", label: "white", group: "white" });

// Bloom's cover colour swatches, read from the front end so this list cannot drift from it.
const paletteSource = fs.readFileSync(
    path.join(
        repoRoot,
        "src",
        "BloomBrowserUI",
        "react_components",
        "color-picking",
        "bloomPalette.ts",
    ),
    "utf8",
);
const paletteMatch = paletteSource.match(
    /CoverBackgroundPalette\s*:\s*string\[\]\s*=\s*\[([\s\S]*?)\]/,
);
if (!paletteMatch)
    throw new Error("Could not find CoverBackgroundPalette in bloomPalette.ts");
for (const m of paletteMatch[1].matchAll(/"(#[0-9A-Fa-f]{6})"/g)) {
    backgrounds.push({
        hex: m[1].toUpperCase(),
        label: "Bloom swatch",
        group: "swatch",
    });
}
if (chosenColours) {
    const chosen = chosenColours.map(
        (hex) =>
            backgrounds.find((bg) => bg.hex === hex) ?? {
                hex,
                label: "chosen colour",
                group: "chosen",
            },
    );
    backgrounds.splice(0, backgrounds.length, ...chosen);
}
for (const bg of backgrounds)
    bg.lightness = Math.round(lightnessOfHex(bg.hex) * 10) / 10;
if (!chosenColours) backgrounds.sort((a, b) => b.lightness - a.lightness);

// ---- Copy images ----

// Remove only what an earlier run generated, so a --page-dir that holds other files keeps them.
for (const generated of ["index.html", "img", "full"])
    fs.rmSync(path.join(pageDir, generated), { recursive: true, force: true });
fs.mkdirSync(path.join(pageDir, "img"), { recursive: true });
let totalBytes = 0;
function copyInto(sourceFile, relativeTarget) {
    const target = path.join(pageDir, relativeTarget);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(sourceFile, target);
    totalBytes += fs.statSync(target).size;
    return relativeTarget.split(path.sep).join("/");
}

const algorithms = results.algorithms;
const sections = [];
for (const image of images) {
    const result = resultsById.get(image.id);
    const rows = [];
    const originalPreview = path.join(
        outDir,
        "original",
        `${image.id}.preview.png`,
    );
    if (result && fs.existsSync(originalPreview)) {
        rows.push({
            id: "original",
            src: copyInto(
                originalPreview,
                path.join("img", "original", `${image.id}.png`),
            ),
        });
    }
    for (const algorithm of algorithms) {
        const preview = path.join(
            outDir,
            algorithm.id,
            `${image.id}.preview.png`,
        );
        const stats = result?.algorithms?.[algorithm.id];
        if (!fs.existsSync(preview)) {
            rows.push({ id: algorithm.id, error: stats?.error ?? "no output" });
            continue;
        }
        rows.push({
            id: algorithm.id,
            src: copyInto(
                preview,
                path.join("img", algorithm.id, `${image.id}.png`),
            ),
            stats,
        });
    }
    sections.push({ ...image, result, rows });
}
if (totalBytes > sizeLimitBytes) {
    throw new Error(
        `The previews come to ${(totalBytes / 1e6).toFixed(1)} MB, over the limit of ${sizeLimitBytes / 1e6} MB`,
    );
}

// The large view uses the full-size results when they fit in the size limit, else the previews.
const fullSizeFiles = [];
for (const section of sections) {
    for (const row of section.rows) {
        if (!row.src) continue;
        const source =
            row.id === "original"
                ? path.join(reportRoot, "images", section.file)
                : path.join(outDir, row.id, `${section.id}.png`);
        const target = path.join(
            "full",
            row.id,
            section.id + path.extname(source),
        );
        fullSizeFiles.push({
            row,
            source,
            target,
            size: fs.statSync(source).size,
        });
    }
}
const fullSizeBytes = fullSizeFiles.reduce((sum, f) => sum + f.size, 0);
const includeFullSize = totalBytes + fullSizeBytes <= sizeLimitBytes;
if (includeFullSize) {
    for (const f of fullSizeFiles) f.row.full = copyInto(f.source, f.target);
}

// ---- Page ----

for (const id of methodsShown ?? [])
    if (!algorithms.some((a) => a.id === id))
        throw new Error(
            `--methods-shown names "${id}", which results.json does not have`,
        );
const data = {
    algorithms,
    methodsShown: methodsShown ?? algorithms.map((a) => a.id),
    backgrounds,
    sections,
    generated: results.generated,
};
const html = `${
    forArtifact
        ? ""
        : `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
`
}<title>Transparency Comparison</title>
<meta name="claude:viewport-fit-sides" content="cover">
<style>
/* The page around the pictures follows the viewer's light or dark theme; the cells behind the
   pictures always use the background colour being tested. */
:root { --bg: #ffffff; --text: #1d1d1f; --muted: #5f6368; --line: #d6d6d6; --panel: #f4f4f2; --accent: #1a5fb4; --warn: #a51d2d; --flag-bg: #fff3c4; --flag-text: #1d1d1f; --cell: 160px; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg: #17181a; --text: #e8e8e6; --muted: #a3a6ab; --line: #3a3c40; --panel: #222427; --accent: #8cb4f0; --warn: #ff8a8a; --flag-bg: #5a4a10; --flag-text: #fff3c4; color-scheme: dark; } }
:root[data-theme="dark"] { --bg: #17181a; --text: #e8e8e6; --muted: #a3a6ab; --line: #3a3c40; --panel: #222427; --accent: #8cb4f0; --warn: #ff8a8a; --flag-bg: #5a4a10; --flag-text: #fff3c4; color-scheme: dark; }
* { box-sizing: border-box; }
body { margin: 0; padding-block: 16px 48px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); font: 15px/1.45 system-ui, "Segoe UI", sans-serif; color: var(--text); background: var(--bg); }
h1 { font-size: 24px; margin: 0 0 8px; text-wrap: balance; }
h2 { font-size: 19px; margin: 0; display: flex; align-items: center; gap: 10px; }
a { color: var(--accent); }
.intro { max-width: 70em; }
.intro dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 8px 0 12px; }
.intro dt { font-family: ui-monospace, Consolas, monospace; font-weight: 600; }
.intro dd { margin: 0; }
.intro details { margin: 8px 0; }
.intro summary { cursor: pointer; font-weight: 600; }
.intro ol { margin: 6px 0; padding-left: 2.5em; }
.intro ol li { margin: 3px 0; }
.timeline { list-style: none; margin: 12px 0 4px; padding: 0; display: flex; flex-wrap: wrap; gap: 0; }
.timeline li { position: relative; flex: 1 1 150px; min-width: 0; padding: 18px 12px 6px 0; border-top: 3px solid var(--line); font-size: 13px; display: flex; flex-direction: column; gap: 2px; }
.timeline li::before { content: ""; position: absolute; top: -8px; left: 0; width: 13px; height: 13px; border-radius: 50%; background: var(--bg); border: 3px solid var(--muted); }
.timeline li.now { border-top-color: var(--accent); }
.timeline li.now::before { border-color: var(--accent); background: var(--accent); }
.timeline .when { color: var(--muted); font-variant-numeric: tabular-nums; }
.timeline b { font-family: ui-monospace, Consolas, monospace; font-size: 13px; }
.timeline .where { color: var(--muted); overflow-wrap: anywhere; }
.ideas { color: var(--muted); font-size: 13px; margin: 4px 0 12px; }
.ideas b { font-family: ui-monospace, Consolas, monospace; }
.candidate { background: var(--flag-bg); color: var(--flag-text); border-radius: 3px; padding: 0 4px; font-weight: 600; font-family: system-ui, sans-serif; font-size: 12px; }
.filters { position: sticky; top: env(safe-area-inset-top, 0px); z-index: 5; background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 8px 12px; margin: 12px 0 20px; display: flex; flex-wrap: wrap; gap: 8px 28px; align-items: center; }
.filters fieldset { border: 0; padding: 0; margin: 0; display: flex; flex-wrap: wrap; gap: 2px 12px; align-items: center; }
.filters legend { font-weight: 600; padding: 0; margin-right: 8px; float: left; }
.filters label { white-space: nowrap; font-size: 14px; }
.filters .size { font-size: 13px; color: var(--muted); }
section.colour { border-top: 2px solid var(--line); padding: 16px 0 8px; }
.swatch { display: inline-block; width: 28px; height: 28px; border: 1px solid var(--muted); border-radius: 4px; }
.meta { color: var(--muted); margin: 4px 0 10px; font-size: 14px; }
.verdict-no { color: var(--warn); font-weight: 600; }
.scroller { overflow-x: auto; border: 1px solid var(--line); border-radius: 4px; }
table { border-collapse: collapse; }
th, td { padding: 0; }
thead th { background: var(--bg); font-weight: 500; font-size: 12px; text-align: left; padding: 4px 6px; border-bottom: 1px solid var(--line); vertical-align: bottom; width: var(--cell); max-width: var(--cell); overflow-wrap: anywhere; }
thead th b { font-family: ui-monospace, Consolas, monospace; font-size: 12px; }
thead th .flag { color: var(--warn); font-weight: 600; display: block; }
tbody th { position: sticky; left: 0; z-index: 1; background: var(--bg); text-align: left; vertical-align: middle; padding: 6px 10px 6px 6px; font-weight: 400; font-size: 12px; min-width: 130px; border-right: 1px solid var(--line); }
tbody th b { font-family: ui-monospace, Consolas, monospace; font-size: 13px; display: flex; align-items: center; justify-content: space-between; gap: 6px; }
.hide-method { border: 0; background: none; padding: 2px; color: var(--muted); cursor: pointer; border-radius: 3px; display: inline-flex; }
.hide-method:hover { color: var(--text); background: var(--panel); }
thead th:first-child { position: sticky; left: 0; z-index: 2; }
tbody tr + tr th, tbody tr + tr td { border-top: 1px solid var(--line); }
td.cell { cursor: zoom-in; }
td.cell div { width: var(--cell); height: calc(var(--cell) * 0.78); display: flex; align-items: center; justify-content: center; }
td.cell img { width: calc(var(--cell) - 8px); height: calc(var(--cell) * 0.78 - 8px); object-fit: contain; display: block; }
td.error { font-size: 12px; color: var(--warn); padding: 6px; width: var(--cell); }
.hidden { display: none !important; }
#viewer { position: fixed; inset: 0; background: rgba(0,0,0,0.75); z-index: 20; display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 16px; }
#viewer .frame { max-width: 100%; max-height: calc(100% - 60px); overflow: auto; padding: 24px; }
#viewer img { display: block; max-width: calc(100vw - 100px); max-height: calc(100vh - 150px); object-fit: contain; }
#viewer .caption { color: #fff; margin-top: 10px; text-align: center; font-size: 14px; }
</style>
${forArtifact ? "" : "</head>\n<body>"}
<div class="intro">
<h1>Making white backgrounds transparent: how each method looks on many colours</h1>
<ol id="timeline" class="timeline" aria-label="When each method was used"></ol>
<p id="ideas" class="ideas"></p>
<p>Each picture was run through Bloom's own C# code for each method. There is one section per background colour; the browser draws each result over that colour, as Bloom does. In each section, the rows are the methods and the columns are the pictures. Each section's heading gives the colour's lightness (CIE L*, where 100 is white and 0 is black).</p>
<p><b>Hold Ctrl and turn the mouse wheel</b> to make the pictures larger or smaller; the text stays the same size. Click any picture to see it large: then Left and Right move to the next picture, Up and Down to the next method, Page Up and Page Down to the next colour, and Escape closes it.</p>
<dl id="algorithm-list"></dl>
<p>The first row of each section shows the original picture, with its white background, for reference.</p>
<details><summary>The pictures</summary><ol id="picture-list"></ol></details>
</div>
<div class="filters">
<fieldset id="kind-filter"><legend>Picture kind</legend></fieldset>
<fieldset id="algorithm-filter"><legend>Methods</legend></fieldset>
<fieldset id="lightness-filter"><legend>Background lightness</legend></fieldset>
<span class="size">Ctrl + mouse wheel: picture size <span id="size-readout"></span></span>
</div>
<main id="sections"></main>
<div id="viewer" class="hidden"><div class="frame"><img alt=""></div><div class="caption"></div></div>
<script>
const data = ${JSON.stringify(data).replace(/</g, "\\u003c")};
const lightnessBands = [
    { id: "very-light", label: "very light (L* 80 and up)", min: 80, max: 101 },
    { id: "light", label: "light (60 to 80)", min: 60, max: 80 },
    { id: "middle", label: "middle (40 to 60)", min: 40, max: 60 },
    { id: "dark", label: "dark (below 40)", min: -1, max: 40 },
];
const bandOf = (L) => lightnessBands.find((b) => L >= b.min && L < b.max).id;
const algorithmById = new Map(data.algorithms.map((a) => [a.id, a]));
const pictures = data.sections;
const rowIds = ["original", ...data.algorithms.map((a) => a.id)];

function el(tag, attrs, ...children) {
    const e = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
        if (k === "class") e.className = v;
        else if (k.startsWith("data-")) e.setAttribute(k, v);
        else e[k] = v;
    }
    for (const c of children) if (c != null) e.append(c);
    return e;
}
function percent(share) { return (share * 100).toFixed(share > 0 && share < 0.001 ? 2 : 1) + "%"; }

// Explains, in one sentence, anything that makes a picture's rows look alike or not apply.
function pictureWarning(p) {
    if (!p.result || p.result.error) return "Not processed: " + (p.result ? p.result.error : "no results") + ".";
    if (p.result.alreadyHasTransparency) return "Already has see-through pixels, so Bloom and every method leave it unchanged.";
    if (!p.result.shouldMakeTransparent) return "Bloom's check for line art says no, so Bloom would show the original; the rows show what each method would do anyway.";
    if (p.result.indexedAndOpaque) return "Stores its colours as a palette, so every method takes the same shortcut and gives the same result.";
    return null;
}

// The timeline: every method with a date, oldest first; the ideas that were never committed follow it.
const timeline = document.getElementById("timeline");
const dated = data.algorithms.filter((a) => a.date).sort((a, b) => a.date.localeCompare(b.date));
for (const a of dated) {
    const when = new Date(a.date + "T00:00:00").toLocaleDateString("en", { year: "numeric", month: "short", day: "numeric" });
    const li = el("li", { class: a.isNewCandidate || a.id.startsWith("proposed") ? "now" : "" },
        el("span", { class: "when" }, when),
        el("b", {}, a.id),
        el("span", { class: "where" }, a.where));
    timeline.append(li);
}
const ideas = data.algorithms.filter((a) => !a.date);
if (ideas.length) {
    const p = document.getElementById("ideas");
    p.append("Never in a version: ");
    ideas.forEach((a, i) => p.append(i ? ", " : "", el("b", {}, a.id), " (" + a.where + ")"));
    p.append(".");
}

const list = document.getElementById("algorithm-list");
for (const a of data.algorithms) {
    list.append(el("dt", {}, a.id));
    const dd = el("dd", {}, a.description);
    if (a.isNewCandidate) dd.append(" ", el("span", { class: "candidate" }, "new candidate"));
    list.append(dd);
}
const pictureList = document.getElementById("picture-list");
for (const p of pictures) {
    const li = el("li", {}, el("b", {}, p.id), " (" + p.kind + "). ");
    if (p.result && p.result.width) li.append(p.result.width + " × " + p.result.height + " pixels. ");
    if (p.note) li.append(p.note + " ");
    const warning = pictureWarning(p);
    if (warning) li.append(el("span", { class: "verdict-no" }, warning + " "));
    if (p.source) li.append(el("a", { href: p.source, target: "_blank", rel: "noopener" }, "source"));
    pictureList.append(li);
}

function addCheckbox(fieldset, value, label, checked = true) {
    const box = el("input", { type: "checkbox", checked, value });
    box.addEventListener("change", applyFilters);
    fieldset.append(el("label", {}, box, " " + label));
}
const kinds = [...new Set(pictures.map((p) => p.kind))];
for (const k of kinds) addCheckbox(document.getElementById("kind-filter"), k, k);
for (const a of data.algorithms)
    addCheckbox(document.getElementById("algorithm-filter"), a.id, a.id, data.methodsShown.includes(a.id));
for (const b of lightnessBands) addCheckbox(document.getElementById("lightness-filter"), b.id, b.label);

// The standard "eye with a slash" icon, meaning hide.
const eyeOffIcon = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94"/><path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19"/><path d="M14.12 14.12a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>';
// Hiding a method from its row is the same as unchecking it under Methods at the top.
function hideMethod(id) {
    document.querySelector("#algorithm-filter input[value='" + id + "']").checked = false;
    applyFilters();
}

const main = document.getElementById("sections");
data.backgrounds.forEach((bg, bgIndex) => {
    const section = el("section", { class: "colour", "data-band": bandOf(bg.lightness), "data-bg": String(bgIndex) });
    section.append(el("h2", {}, el("span", { class: "swatch", style: "background:" + bg.hex }),
        bg.hex + " · " + bg.label + " · L* " + bg.lightness.toFixed(0)));

    const table = el("table");
    const headRow = el("tr", {}, el("th", {}, ""));
    pictures.forEach((p, pi) => {
        const th = el("th", { "data-kind": p.kind, "data-picture": String(pi), title: (p.note || "") }, el("b", {}, p.id));
        const warning = pictureWarning(p);
        if (warning) { th.append(el("span", { class: "flag" }, "see note")); th.title = warning + " " + th.title; }
        headRow.append(th);
    });
    table.append(el("thead", {}, headRow));
    const body = el("tbody");
    for (const rowId of rowIds) {
        const tr = el("tr", { "data-algorithm": rowId });
        const label = el("th", {}, el("b", {}, rowId));
        if (rowId === "original") label.append("as supplied, not transparent");
        else {
            const hide = el("button", { class: "hide-method", type: "button", title: "Hide " + rowId + " everywhere (same as unchecking it under Methods)" });
            hide.innerHTML = eyeOffIcon;
            hide.addEventListener("click", () => hideMethod(rowId));
            label.querySelector("b").append(hide);
            if (algorithmById.get(rowId).isNewCandidate) label.append(el("span", { class: "candidate" }, "new candidate"));
        }
        tr.append(label);
        pictures.forEach((p, pi) => {
            const row = p.rows.find((r) => r.id === rowId);
            if (!row || !row.src) {
                tr.append(el("td", { class: "error", "data-kind": p.kind }, "Failed: " + (row ? row.error : "no output")));
                return;
            }
            const img = el("img", { src: row.src, alt: p.id + " " + rowId + " on " + bg.hex, loading: "lazy" });
            const td = el("td", { class: "cell", "data-kind": p.kind, "data-picture": String(pi) }, el("div", { style: "background:" + bg.hex }, img));
            if (row.stats && row.stats.ms !== undefined)
                td.title = p.id + ", " + rowId + ": see-through " + percent(row.stats.transparent) + ", partly " + percent(row.stats.partial) + ", solid " + percent(row.stats.opaque) + "; " + row.stats.ms + " ms";
            td.addEventListener("click", () => openViewer(bgIndex, rowId, pi));
            tr.append(td);
        });
        body.append(tr);
    }
    table.append(body);
    section.append(el("div", { class: "scroller" }, table));
    main.append(section);
});

function checkedValues(id) {
    return new Set([...document.querySelectorAll("#" + id + " input:checked")].map((i) => i.value));
}
function applyFilters() {
    const kindsOn = checkedValues("kind-filter");
    const algorithmsOn = checkedValues("algorithm-filter");
    const bandsOn = checkedValues("lightness-filter");
    for (const s of document.querySelectorAll("section.colour"))
        s.classList.toggle("hidden", !bandsOn.has(s.dataset.band));
    for (const tr of document.querySelectorAll("tr[data-algorithm]"))
        tr.classList.toggle("hidden", tr.dataset.algorithm !== "original" && !algorithmsOn.has(tr.dataset.algorithm));
    for (const c of document.querySelectorAll("[data-kind]"))
        c.classList.toggle("hidden", !kindsOn.has(c.dataset.kind));
}

// Start with only the methods ticked above showing.
applyFilters();

// Ctrl + mouse wheel changes only the picture size (the --cell variable), not the page zoom.
let cellSize = 160;
const sizeReadout = document.getElementById("size-readout");
function setCellSize(size) {
    cellSize = Math.max(60, Math.min(900, Math.round(size)));
    document.documentElement.style.setProperty("--cell", cellSize + "px");
    sizeReadout.textContent = "(" + cellSize + " px)";
}
setCellSize(cellSize);
window.addEventListener("wheel", (e) => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    // Keep the point under the mouse in place horizontally and vertically as the pictures resize.
    const scroller = e.target.closest ? e.target.closest(".scroller") : null;
    const before = cellSize;
    setCellSize(cellSize * Math.pow(1.0015, -e.deltaY));
    const ratio = cellSize / before;
    if (scroller) {
        const rect = scroller.getBoundingClientRect();
        const x = e.clientX - rect.left;
        scroller.scrollLeft = (scroller.scrollLeft + x) * ratio - x;
    }
    window.scrollTo(window.scrollX, (window.scrollY + e.clientY) * ratio - e.clientY);
}, { passive: false });

const viewer = document.getElementById("viewer");
let current = null;
function showCurrent() {
    const bg = data.backgrounds[current.bgIndex];
    const p = pictures[current.pictureIndex];
    const row = p.rows.find((r) => r.id === current.rowId);
    viewer.querySelector(".frame").style.background = bg.hex;
    const img = viewer.querySelector("img");
    img.src = row.full || row.src;
    // A preview is only 480 wide, so it is enlarged; a full-size result is shown at its own size.
    img.style.width = row.full ? "" : Math.min(960, window.innerWidth - 100) + "px";
    viewer.querySelector(".caption").textContent =
        p.id + " · " + current.rowId + " · on " + bg.hex + " (" + bg.label + ", L* " + bg.lightness.toFixed(0) + ")";
}
function openViewer(bgIndex, rowId, pictureIndex) {
    current = { bgIndex, rowId, pictureIndex };
    viewer.classList.remove("hidden");
    showCurrent();
}
function step(list, value, delta) {
    const next = list[list.indexOf(value) + delta];
    return next === undefined ? value : next;
}
viewer.addEventListener("click", () => viewer.classList.add("hidden"));
document.addEventListener("keydown", (e) => {
    if (viewer.classList.contains("hidden")) return;
    if (e.key === "Escape") { viewer.classList.add("hidden"); return; }
    const kindsOn = checkedValues("kind-filter");
    const algorithmsOn = checkedValues("algorithm-filter");
    const bandsOn = checkedValues("lightness-filter");
    const visiblePictures = pictures.map((p, i) => i).filter((i) => kindsOn.has(pictures[i].kind) && pictures[i].rows.some((r) => r.id === current.rowId && r.src));
    const visibleRows = rowIds.filter((id) => (id === "original" || algorithmsOn.has(id)) && pictures[current.pictureIndex].rows.some((r) => r.id === id && r.src));
    const visibleBgs = data.backgrounds.map((b, i) => i).filter((i) => bandsOn.has(bandOf(data.backgrounds[i].lightness)));
    if (e.key === "ArrowRight" || e.key === "ArrowLeft") current.pictureIndex = step(visiblePictures, current.pictureIndex, e.key === "ArrowRight" ? 1 : -1);
    else if (e.key === "ArrowDown" || e.key === "ArrowUp") current.rowId = step(visibleRows, current.rowId, e.key === "ArrowDown" ? 1 : -1);
    else if (e.key === "PageDown" || e.key === "PageUp") current.bgIndex = step(visibleBgs, current.bgIndex, e.key === "PageDown" ? 1 : -1);
    else return;
    e.preventDefault();
    showCurrent();
});
</script>
${forArtifact ? "" : "</body>\n</html>"}
`;
fs.writeFileSync(path.join(pageDir, "index.html"), html);
console.log(
    `Wrote ${path.join(pageDir, "index.html")}: ${sections.length} pictures, ${algorithms.length} methods, ` +
        `${backgrounds.length} backgrounds, ${(totalBytes / 1e6).toFixed(1)} MB of images` +
        (includeFullSize
            ? " (full-size results included for the large view)."
            : "; the full-size results would pass the size limit, so the large view enlarges the previews."),
);
