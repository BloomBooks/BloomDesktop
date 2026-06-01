import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { createRequire } from "node:module";

const JSDOM = createRequire(
    path.join("d:/bloom", "src", "BloomBrowserUI", "package.json"),
)("jsdom").JSDOM;
const bookPath =
    "C:/Users/hatto/OneDrive/Documents/Bloom/Wildlife Identifier Books/Identification de la faune/Identification de la faune.htm";
const backup =
    "C:/Users/hatto/AppData/Local/Temp/Identification_de_la_faune.htm.bak";

const birds = JSON.parse(
    fs.readFileSync("C:/Users/hatto/AppData/Local/Temp/birds.json", "utf8"),
);
const manifestArr = JSON.parse(
    fs.readFileSync("C:/Users/hatto/AppData/Local/Temp/manifest.json", "utf8"),
);
const manifest = new Map(manifestArr.map((m) => [m.row, m]));

// start from the pristine original (has the placeholder content page)
const source = fs.readFileSync(backup, "utf8");
const dom = new JSDOM(source);
const document = dom.window.document;
const template = document.querySelector(".bloom-page.numberedPage.customPage");
if (!template) throw new Error("template page not found");

const CONTAINER = { top: { w: 348, h: 224 }, bottom: { w: 348, h: 234 } };
const r3 = (n) => Math.round(n * 1000) / 1000;

function licToken(raw) {
    const s = (raw || "").trim().toLowerCase();
    if (s.startsWith("cc by-nc-sa")) return "cc-by-nc-sa";
    if (s.startsWith("cc by-nc-nd")) return "cc-by-nc-nd";
    if (s.startsWith("cc by-nc")) return "cc-by-nc";
    if (s.startsWith("cc by-nd")) return "cc-by-nd";
    if (s.startsWith("cc by-sa")) return "cc-by-sa";
    if (s.startsWith("cc by")) return "cc-by";
    if (s === "cc0" || s.includes("public domain")) return "cc0";
    return ""; // GFDL / blank / unknown -> custom
}

function coverStyles(srcW, srcH, slot) {
    const c = CONTAINER[slot];
    const scale = Math.max(c.w / srcW, c.h / srcH);
    const dispW = srcW * scale,
        dispH = srcH * scale;
    const left = (c.w - dispW) / 2,
        top = (c.h - dispH) / 2;
    return {
        canvas: `width: ${r3(c.w)}px; height: ${r3(c.h)}px; top: 0px; left: 0px;`,
        img: `width: ${r3(dispW)}px; top: ${r3(top)}px; left: ${r3(left)}px;`,
    };
}

function setEditable(el, text) {
    el.innerHTML = "";
    if (text == null || text === "") return;
    const p = el.ownerDocument.createElement("p");
    p.textContent = text;
    el.appendChild(p);
}

function fillCard(card, bird) {
    if (!bird) {
        // empty slot
        for (const ed of card.querySelectorAll(".bloom-editable"))
            setEditable(ed, "");
        return;
    }
    setEditable(card.querySelector('.Name-style[lang="fr"]'), bird.fr);
    setEditable(card.querySelector('.Name-style[lang="en"]'), "");
    const normals = card.querySelectorAll('.normal-style[lang="en"]');
    setEditable(normals[0], bird.en);
    setEditable(card.querySelector('.LatinName-style[lang="en"]'), bird.latin);
    setEditable(normals[1], bird.size);
    setEditable(
        card.querySelector('.abundance-style[lang="en"]'),
        bird.abundance,
    );
    const hab = card.querySelector('.Habitat-style[lang="en"]');
    hab.classList.remove("overflow");
    setEditable(hab, bird.habitat);
    setEditable(card.querySelector('.Food-style[lang="en"]'), bird.food);
}

function setImage(canvasEl, bird, slot) {
    const img = canvasEl.querySelector("img");
    canvasEl.removeAttribute("data-bloom-active");
    const canvas = canvasEl.parentElement; // .bloom-canvas
    canvas.setAttribute(
        "data-imgsizebasedon",
        `${CONTAINER[slot].w},${CONTAINER[slot].h}`,
    );
    canvas.removeAttribute("data-title");
    canvas.removeAttribute("title");

    const man = bird ? manifest.get(bird.row) : null;
    if (bird && man && man.ok) {
        const st = coverStyles(man.w, man.h, slot);
        canvasEl.setAttribute("style", st.canvas);
        img.setAttribute("src", man.file);
        img.setAttribute("alt", "");
        img.setAttribute("style", st.img);
        img.setAttribute("data-copyright", bird.copyright || "");
        img.setAttribute("data-creator", bird.photographer || "");
        img.setAttribute("data-license", licToken(bird.licenseRaw));
    } else {
        // missing image (no url / download failed) or empty slot -> placeholder
        const c = CONTAINER[slot];
        canvasEl.setAttribute(
            "style",
            `width: ${c.w}px; height: ${c.h}px; top: 0px; left: 0px;`,
        );
        img.setAttribute("src", "placeHolder.png");
        img.setAttribute("alt", "");
        img.removeAttribute("style");
        img.setAttribute("data-copyright", "");
        img.setAttribute("data-creator", "");
        img.setAttribute("data-license", "");
    }
}

function buildPage(topBird, bottomBird, pageNumber) {
    const page = template.cloneNode(true);
    page.setAttribute("id", crypto.randomUUID());
    page.setAttribute("data-page-number", String(pageNumber));
    page.classList.remove("pageOverflows", "side-right", "side-left");
    page.classList.add(pageNumber % 2 === 1 ? "side-right" : "side-left");

    const cards = page.querySelectorAll(".template-box");
    const canvasEls = page.querySelectorAll(
        ".bloom-canvas-element.bloom-backgroundImage",
    );
    fillCard(cards[0], topBird);
    fillCard(cards[1], bottomBird);
    setImage(canvasEls[0], topBird, "top");
    setImage(canvasEls[1], bottomBird, "bottom");
    return page;
}

function serialize(page) {
    return page.outerHTML.replace(/<img\b([^>]*?)\s*\/?>/g, "<img$1 />");
}

// pair birds 2 per page
const pages = [];
for (let i = 0; i < birds.length; i += 2) {
    pages.push(buildPage(birds[i], birds[i + 1] || null, pages.length + 1));
}

const html = pages.map(serialize).join("\n  ");

// --- robust textual splice: remove placeholder page, insert generated pages ---
const startIdx = source.indexOf(
    '<div class="bloom-page numberedPage customPage',
);
if (startIdx < 0) throw new Error("placeholder page not found in source");

const voids = new Set([
    "img",
    "br",
    "hr",
    "meta",
    "link",
    "input",
    "source",
    "area",
    "base",
    "col",
    "embed",
    "param",
    "track",
    "wbr",
]);
function elementEnd(src, from) {
    const tagRe = /<!--[\s\S]*?-->|<\/?([a-zA-Z][a-zA-Z0-9]*)\b[^>]*?>/g;
    tagRe.lastIndex = from;
    let depth = 0,
        m;
    while ((m = tagRe.exec(src))) {
        if (m[0].startsWith("<!--")) continue;
        const name = m[1] ? m[1].toLowerCase() : null;
        if (!name) continue;
        const isClose = m[0].startsWith("</");
        const selfClose = /\/>\s*$/.test(m[0]) || voids.has(name);
        if (isClose) {
            depth--;
            if (depth === 0) return tagRe.lastIndex;
        } else if (!selfClose) depth++;
    }
    throw new Error("no matching close tag");
}
const endIdx = elementEnd(source, startIdx);

const before = source.slice(0, startIdx);
const after = source.slice(endIdx);
const out = before + html + "\n  " + after;

fs.writeFileSync(bookPath, out, { encoding: "utf8" });

const okImgs = manifestArr.filter((m) => m.ok).length;
const failed = manifestArr.filter((m) => !m.ok);
console.log(`Built ${pages.length} content pages for ${birds.length} birds.`);
console.log(`Images: ${okImgs} ok, ${failed.length} missing/failed.`);
if (failed.length) {
    console.log("Birds using placeholder (no/failed image):");
    for (const f of failed) {
        const b = birds.find((x) => x.row === f.row);
        console.log(
            `  row ${f.row}: ${b ? b.fr + " / " + b.en : "?"}  (${f.err || "no image"})`,
        );
    }
}
const odd = birds.length % 2 === 1;
if (odd)
    console.log(
        `Note: last page (${pages.length}) has an empty bottom slot (odd bird count).`,
    );
console.log(`File written: ${out.length} bytes (was ${source.length}).`);
