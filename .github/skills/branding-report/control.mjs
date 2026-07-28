// branding-report / control.mjs
//
// Interactive control panel + capture server. Unlike survey.mjs (batch) this is a
// live tool: check a branding in the browser and it renders NOW; the results stream
// back into a matrix as they finish. Layouts and pages are chosen globally (not per
// branding) — turning a new one ON re-runs the already-selected brandings to fill the
// gaps; turning one OFF just hides it (no re-render).
//
// It keeps ONE headless Chrome + the selected book alive (via CaptureSession) and
// runs a single serial job queue against the one running Bloom.
//
// Usage:  node control.mjs [--out DIR] [--port 8798] [--book PATH] [--base URL]
// Then open the printed http://localhost:PORT/ .
//
// Requires Bloom running from source with the DEBUG set-state handler (see SKILL.md).
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { CaptureSession, PAGES, renderUrlToPdf } from "./capture-core.mjs";

// Page display order for the matrix and the PDF (front-matter to back-matter).
const PAGE_ORDER = [
    "front",
    "insideFront",
    "title",
    "credits",
    "content",
    "insideBack",
    "back",
];
const esc = (s) =>
    String(s).replace(
        /[&<>"]/g,
        (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c],
    );

const REPO = path.resolve(import.meta.dirname, "..", "..", "..");
const SRC_BRANDING = path.join(REPO, "src", "content", "branding");

// All Bloom page sizes offered in the Layouts dropdown. Mirrors the default set in
// SizeAndOrientation.GetSizeAndOrientationChoices (src/BloomExe/Book/SizeAndOrientation.cs).
// The book's actual current layout is detected at startup and selected by default.
const COMMON_LAYOUTS = [
    "A5Portrait",
    "A5Landscape",
    "A6Portrait",
    "A6Landscape",
    "A4Portrait",
    "A4Landscape",
    "A3Portrait",
    "A3Landscape",
    "B5Portrait",
    "LetterPortrait",
    "LetterLandscape",
    "LegalPortrait",
    "LegalLandscape",
    "HalfLetterPortrait",
    "HalfLetterLandscape",
    "HalfFolioPortrait",
    "QuarterLetterPortrait",
    "QuarterLetterLandscape",
    "Device16x9Portrait",
    "Device16x9Landscape",
    "Ebook2x3Portrait",
    "Ebook7x5Landscape",
    "Cm13Landscape",
    "USComicPortrait",
    "Size6x9Portrait",
    "Size6x9Landscape",
];

function parseArgs() {
    const a = process.argv.slice(2);
    const o = {
        base: "http://localhost:8089/bloom",
        out: path.resolve("branding-report-out"),
        port: 8798,
        book: null,
    };
    for (let i = 0; i < a.length; i++) {
        const [k, inlineV] = a[i].split(/=(.*)/);
        const val = () => (inlineV !== undefined ? inlineV : a[++i]);
        if (k === "--base") o.base = val();
        else if (k === "--out") o.out = path.resolve(val());
        else if (k === "--port") o.port = Number(val());
        else if (k === "--book") o.book = val();
    }
    return o;
}

const opts = parseArgs();
fs.mkdirSync(opts.out, { recursive: true });

// ---------- model & state ----------
const allBrandings = fs
    .readdirSync(SRC_BRANDING)
    .filter((f) => fs.statSync(path.join(SRC_BRANDING, f)).isDirectory())
    .filter((f) => f !== "shared")
    .sort();

const model = {
    brandings: allBrandings,
    layouts: [...COMMON_LAYOUTS], // current layout is spliced in once detected
    pages: Object.keys(PAGES).map((k) => ({ key: k, label: PAGES[k].label })),
    currentLayout: null,
    book: null,
};

const selection = {
    brandings: new Set(), // checked by the user
    layouts: new Set(), // defaults to the current layout once detected
    // default: the four pages branding usually affects (not the inside covers)
    pages: new Set(["front", "title", "credits", "back"]),
};

const cells = []; // captured records (survey-compatible)
const cellIndex = new Map(); // "branding|layout|page" -> record
const cellKey = (b, l, p) => `${b}|${l}|${p}`;
const jobKey = (b, l) => `${b}|${l}`;

const queueOrder = []; // array of {branding, layout}
const queued = new Set(); // jobKey's currently queued
let running = null; // {branding, layout} or null
let version = 0; // bumps on any state change; client polls it
let lastError = null;

// Load an existing manifest so prior captures show up immediately (and aren't redone).
(function loadExisting() {
    const mf = path.join(opts.out, "manifest.json");
    if (!fs.existsSync(mf)) return;
    try {
        const data = JSON.parse(fs.readFileSync(mf, "utf8"));
        for (const c of data.cells || []) {
            if (!c.branding || !c.page) continue;
            const l = c.layout || "current";
            upsertCell({ ...c, layout: l });
        }
        console.log(`Loaded ${cells.length} existing captures from ${mf}`);
    } catch (e) {
        console.log("Could not read existing manifest:", e.message);
    }
})();

function upsertCell(rec) {
    const k = cellKey(rec.branding, rec.layout, rec.page);
    const existing = cellIndex.get(k);
    if (existing) Object.assign(existing, rec);
    else {
        cells.push(rec);
        cellIndex.set(k, rec);
    }
}

const CONTENT_RE = /^content(-\d+)?$/;
const contentNum = (p) => {
    const m = /^content-(\d+)$/.exec(p);
    return m ? +m[1] : 0;
};
// A selected page is "done" for (b,l) if we have a cell for it. The pseudo-page "content"
// is done if ANY content-* cell exists (it expands to one cell per interior page).
function pageDone(b, l, p) {
    if (p === "content")
        return cells.some(
            (c) =>
                c.branding === b &&
                (c.layout || "current") === (l || "current") &&
                CONTENT_RE.test(c.page),
        );
    return cellIndex.has(cellKey(b, l, p));
}

function persistManifest() {
    fs.writeFileSync(
        path.join(opts.out, "manifest.json"),
        JSON.stringify(
            {
                generatedAxes: { book: model.book },
                baseline: session ? session.baseline : {},
                cells,
            },
            null,
            2,
        ),
    );
}

// ---------- capture session (lazy, kept warm) ----------
let session = null;
let startPromise = null;

function ensureStarted() {
    if (startPromise) return startPromise;
    session = new CaptureSession({
        base: opts.base,
        out: opts.out,
        book: opts.book,
    });
    startPromise = (async () => {
        const target = await session.start();
        model.book = target.folderPath;
        const detected = await session.detectLayout();
        model.currentLayout = detected || "A5Portrait";
        session.baseline.layout = detected; // may be null -> restore leaves layout as-is
        if (!model.layouts.includes(model.currentLayout))
            model.layouts.unshift(model.currentLayout);
        if (selection.layouts.size === 0)
            selection.layouts.add(model.currentLayout);
        version++;
        console.log(
            `Session ready. Book: ${model.book}  currentLayout: ${model.currentLayout}`,
        );
    })();
    return startPromise;
}

// ---------- queue ----------
// A job = capture one (branding, layout) for the CURRENTLY-selected pages. Adding a
// branding/layout/page enqueues the (branding,layout) pairs that still lack a
// requested page; deselecting only prunes not-yet-run jobs and hides in the viewer.
function recompute() {
    // prune queued jobs whose branding/layout is no longer selected
    for (let i = queueOrder.length - 1; i >= 0; i--) {
        const j = queueOrder[i];
        if (
            !selection.brandings.has(j.branding) ||
            !selection.layouts.has(j.layout)
        ) {
            queueOrder.splice(i, 1);
            queued.delete(jobKey(j.branding, j.layout));
        }
    }
    // enqueue (branding,layout) pairs that are missing a selected page
    for (const branding of selection.brandings) {
        for (const layout of selection.layouts) {
            const jk = jobKey(branding, layout);
            if (queued.has(jk)) continue;
            if (
                running &&
                running.branding === branding &&
                running.layout === layout
            )
                continue;
            const missing = [...selection.pages].some(
                (p) => !pageDone(branding, layout, p),
            );
            if (missing) {
                queueOrder.push({ branding, layout });
                queued.add(jk);
            }
        }
    }
    version++;
    pump();
}

async function pump() {
    if (running) return;
    const job = queueOrder.shift();
    if (!job) return;
    queued.delete(jobKey(job.branding, job.layout));
    running = job;
    version++;
    try {
        await ensureStarted();
        const pages = [...selection.pages]; // snapshot current selection at run time
        const recs = await session.captureCombo(
            job.branding,
            job.layout,
            null,
            pages,
        );
        for (const r of recs) upsertCell(r);
        persistManifest();
        lastError = null;
    } catch (e) {
        lastError = `${job.branding}/${job.layout}: ${e.message || e}`;
        console.error("Capture failed:", lastError);
        // Record failed page cells so the UI stops showing them as perpetually pending.
        for (const p of selection.pages)
            upsertCell({
                branding: job.branding,
                layout: job.layout,
                xmatter: null,
                page: p,
                pageLabel: PAGES[p]?.label || p,
                ok: false,
                error: String(e.message || e),
            });
    }
    running = null;
    version++;
    setImmediate(pump);
}

// ---------- HTTP ----------
function snapshot() {
    return {
        version,
        sessionReady: !!(startPromise && model.book),
        model,
        selection: {
            brandings: [...selection.brandings],
            layouts: [...selection.layouts],
            pages: [...selection.pages],
        },
        cells,
        running,
        queue: queueOrder,
        queueLength: queueOrder.length,
        lastError,
    };
}

function sendJson(res, obj, code = 200) {
    const body = JSON.stringify(obj);
    res.writeHead(code, { "Content-Type": "application/json; charset=utf-8" });
    res.end(body);
}

function readBody(req) {
    return new Promise((resolve) => {
        let b = "";
        req.on("data", (c) => (b += c));
        req.on("end", () => resolve(b));
    });
}

const TYPES = {
    ".html": "text/html; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".png": "image/png",
    ".svg": "image/svg+xml",
    ".css": "text/css",
    ".js": "text/javascript",
    ".pdf": "application/pdf",
};
const controlHtml = path.join(import.meta.dirname, "control", "index.html");

// Build a print-optimized, standalone HTML of the CURRENT selection (what the content
// area is showing). Formatted independently of the on-screen matrix: a grid whose cells
// have a large minimum size (in inches), so every screenshot stays legible on A4/Letter.
// Grouped by layout, page-broken between layouts. Images are served from this server.
function buildPrintHtml(paperName) {
    const layouts = [...selection.layouts];
    const brandings = [...selection.brandings];
    const pages = PAGE_ORDER.filter((p) => selection.pages.has(p));
    let total = 0;
    const sections = layouts
        .map((l) => {
            const figs = [];
            const fig = (b, c, label) => {
                total++;
                figs.push(
                    `<figure><div class="frame"><img src="/${esc(c.file)}"></div>` +
                        `<figcaption><b>${esc(b)}</b> · ${esc(label)}</figcaption></figure>`,
                );
            };
            for (const b of brandings)
                for (const p of pages) {
                    if (p === "content") {
                        // expand to every captured content-* page for this (branding,layout)
                        cells
                            .filter(
                                (c) =>
                                    c.branding === b &&
                                    (c.layout || "current") ===
                                        (l || "current") &&
                                    CONTENT_RE.test(c.page) &&
                                    c.ok &&
                                    c.file,
                            )
                            .sort(
                                (a, b2) =>
                                    contentNum(a.page) - contentNum(b2.page),
                            )
                            .forEach((c) => fig(b, c, c.pageLabel || c.page));
                    } else {
                        const c = cellIndex.get(cellKey(b, l, p));
                        if (c && c.ok && c.file)
                            fig(b, c, PAGES[p]?.label || p);
                    }
                }
            if (!figs.length) return "";
            return `<section><h2>${esc(l)}</h2><div class="grid">${figs.join("")}</div></section>`;
        })
        .filter(Boolean)
        .join("\n");
    const when = new Date().toLocaleString();
    return `<!doctype html><html><head><meta charset="utf-8"><title>Bloom branding report</title>
<style>
  @page { margin: 0; }
  html,body{margin:0}
  body{font-family:"Segoe UI",system-ui,sans-serif;color:#141414;-webkit-print-color-adjust:exact;print-color-adjust:exact}
  h1{font-size:16pt;margin:0 0 2pt}
  .meta{color:#666;font-size:9pt;margin:0 0 6pt}
  section{break-before:page;padding-top:4pt}
  section:first-of-type{break-before:auto}
  h2{font-size:13pt;margin:8pt 0 10pt;border-bottom:1px solid #ccc;padding-bottom:4pt}
  /* One image per row, each as large as fits A4 (content ~7.1in wide, ~9.4in tall after
     margins). Landscape pages fill the width; portrait pages fill the height. */
  .grid{display:flex;flex-direction:column;gap:20pt;align-items:center}
  figure{margin:0;break-inside:avoid;display:flex;flex-direction:column;align-items:center}
  .frame{border:1px solid #ccc;background:#efece8;border-radius:3px;overflow:hidden}
  .frame img{max-width:7.1in;max-height:9.4in;width:auto;height:auto;display:block}
  figcaption{font-size:11pt;color:#222;margin-top:6pt;line-height:1.3;text-align:center}
  figcaption b{color:#000}
</style></head><body>
  <h1>Bloom branding report</h1>
  <div class="meta">book: ${esc((model.book || "").split(/[\\/]/).pop())} &middot; ${total} pages &middot; paper: ${esc(paperName)} &middot; ${esc(when)}</div>
  ${sections || '<p class="meta">No captured pages match the current selection.</p>'}
</body></html>`;
}

const server = http.createServer(async (req, res) => {
    const urlPath = decodeURIComponent(req.url.split("?")[0]);

    if (urlPath === "/api/state") return sendJson(res, snapshot());
    if (urlPath === "/api/pdf") {
        const paper = "A4"; // always A4
        try {
            const html = buildPrintHtml(paper);
            fs.writeFileSync(path.join(opts.out, "report-print.html"), html);
            const pdfName = "branding-report.pdf";
            await renderUrlToPdf(
                `http://localhost:${opts.port}/report-print.html`,
                path.join(opts.out, pdfName),
                { paper },
            );
            return sendJson(res, { file: pdfName });
        } catch (e) {
            console.error("PDF export failed:", e.message || e);
            return sendJson(res, { error: String(e.message || e) }, 500);
        }
    }
    if (urlPath === "/api/refresh" && req.method === "POST") {
        // Throw away all accumulated screenshots and rebuild the current selection.
        cells.length = 0;
        cellIndex.clear();
        queueOrder.length = 0;
        queued.clear();
        version++;
        persistManifest();
        recompute(); // re-enqueue every currently-selected (branding, layout)
        return sendJson(res, snapshot());
    }
    if (urlPath === "/api/selection" && req.method === "POST") {
        const body = JSON.parse((await readBody(req)) || "{}");
        if (Array.isArray(body.brandings))
            selection.brandings = new Set(body.brandings);
        if (Array.isArray(body.layouts))
            selection.layouts = new Set(
                body.layouts.length
                    ? body.layouts
                    : [model.currentLayout].filter(Boolean),
            );
        if (Array.isArray(body.pages)) selection.pages = new Set(body.pages);
        recompute();
        return sendJson(res, snapshot());
    }

    // static: control panel at "/", everything else from the output dir (PNGs, manifest)
    let filePath;
    if (urlPath === "/" || urlPath === "/index.html") filePath = controlHtml;
    else {
        const rel = path.normalize(urlPath).replace(/^(\.\.[/\\])+/, "");
        filePath = path.join(opts.out, rel);
    }
    fs.readFile(filePath, (err, buf) => {
        if (err) {
            res.writeHead(404);
            res.end("Not found: " + urlPath);
            return;
        }
        res.writeHead(200, {
            "Content-Type":
                TYPES[path.extname(filePath)] || "application/octet-stream",
        });
        res.end(buf);
    });
});

server.listen(opts.port, () => {
    console.log(
        `branding-report control panel: http://localhost:${opts.port}/  (out: ${opts.out})`,
    );
    console.log("Warming up capture session…");
    ensureStarted().catch((e) => {
        lastError = "session start failed: " + (e.message || e);
        console.error(lastError);
    });
});

// Restore the book on shutdown.
let shuttingDown = false;
async function shutdown() {
    if (shuttingDown) return;
    shuttingDown = true;
    console.log("\nShutting down — restoring book…");
    try {
        if (session && startPromise) {
            await session.restore({ layout: true });
            session.close();
        }
    } catch (e) {
        console.error("restore failed:", e.message || e);
    }
    process.exit(0);
}
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
