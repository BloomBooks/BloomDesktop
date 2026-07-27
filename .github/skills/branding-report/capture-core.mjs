// branding-report / capture-core.mjs
//
// The hard-won primitives for driving the RUNNING Bloom to render one book and
// screenshot its pages, plus a CaptureSession that keeps Chrome + the selected
// book alive across many captures. Shared by survey.mjs (batch CLI) and
// control.mjs (interactive on-demand server). See README-gotchas.md for the "why".
//
// Requires: Bloom running from source (go.sh) with the DEBUG-only multi-axis
// set-state handler on POST /bloom/api/settings/branding, plus the Book.cs
// update-guard try/finally fix. See SKILL.md.
import { execFile } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

export const CHROME_DEFAULT =
    "C:/Program Files/Google/Chrome/Application/chrome.exe";

// Friendly page name -> how to find that PAGE in the whole-book preview DOM.
// Always prefer the unambiguous data-xmatter-page attribute (only page divs carry it);
// scope the class fallback to `.bloom-page` so it can't match an inner element. Bare
// `.credits`/`.titlePage` also match hidden 0-size bloom-editable fields that some
// xmatters/brandings (e.g. Kyrgyzstan2020's Content-On-Title-Page credits field) place
// EARLIER in the DOM — querySelector would then return that hidden field and the page
// looked "not present".
export const PAGES = {
    front: {
        selector:
            '[data-xmatter-page="frontCover"], .bloom-page.frontCover, .bloom-page.outsideFrontCover',
        label: "Front cover",
    },
    title: {
        selector: '[data-xmatter-page="titlePage"], .bloom-page.titlePage',
        label: "Title page",
    },
    credits: {
        selector: '[data-xmatter-page="credits"], .bloom-page.credits',
        label: "Credits",
    },
    // ALL interior pages (not front/back matter). Expands to one screenshot per content
    // page (content-1, content-2, …). Branding rarely touches these, so default off.
    content: {
        selector: ".bloom-page:not(.bloom-frontMatter):not(.bloom-backMatter)",
        label: "Content pages",
        multi: true,
    },
    insideFront: {
        selector:
            '[data-xmatter-page="insideFrontCover"], .bloom-page.insideFrontCover',
        label: "Inside front cover",
    },
    insideBack: {
        selector:
            '[data-xmatter-page="insideBackCover"], .bloom-page.insideBackCover',
        label: "Inside back cover",
    },
    back: {
        selector:
            '[data-xmatter-page="outsideBackCover"], .bloom-page.outsideBackCover',
        label: "Back cover",
    },
};

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export const safe = (s) => String(s).replace(/[^A-Za-z0-9._-]/g, "_");

// Local-Community's back cover uses {personalization}, parsed from the descriptor
// (the part before "-LC"); a bare "Local-Community" descriptor throws. Give it one.
export function descriptorFor(brandingKey) {
    if (brandingKey === "Local-Community") return "Sample-Community-LC";
    return brandingKey;
}

// ---------- Bloom API ----------
export async function apiGet(base, urlPart) {
    const r = await fetch(`${base}/api/${urlPart}`);
    return r.ok ? await r.text() : null;
}

// POST the DEBUG-only multi-axis set-state handler. Content-Type MUST be text/plain
// (RequiredPostString asserts it; a wrong type trips a Debug.Assert that terminates Bloom).
export async function setState(base, state) {
    const body = JSON.stringify({
        branding: state.branding ? descriptorFor(state.branding) : undefined,
        layout: state.layout || undefined,
        xmatter: state.xmatter || undefined,
    });
    const r = await fetch(`${base}/api/settings/branding`, {
        method: "POST",
        headers: { "Content-Type": "text/plain" },
        body,
    });
    if (!r.ok)
        throw new Error(
            `setState ${body} -> HTTP ${r.status} ${await r.text()}`,
        );
}

function robustJson(text) {
    let v = JSON.parse(text);
    if (typeof v === "string") v = JSON.parse(v);
    return v;
}

export async function getBookTarget(base, explicitPath) {
    if (explicitPath)
        return {
            folderPath: explicitPath,
            collectionId: path.dirname(explicitPath),
        };
    const si = await fetch(`${base}/api/collections/selected-book-info`);
    if (si.ok) {
        const info = await si.json();
        if (info && info.folderPath)
            return {
                folderPath: info.folderPath,
                collectionId: path.dirname(info.folderPath),
            };
    }
    const cols = robustJson(await apiGet(base, "collections/list"));
    for (const c of cols) {
        const books = robustJson(
            await apiGet(
                base,
                `collections/books?collection-id=${encodeURIComponent(c.id)}`,
            ),
        );
        const book = (books || []).find((b) => !b.isFactory && b.folderPath);
        if (book)
            return {
                folderPath: book.folderPath,
                collectionId: book.collectionId,
            };
    }
    throw new Error("Could not find a book in the running collection");
}

export async function selectBook(base, target) {
    const url =
        `${base}/api/collections/selected-book` +
        `?path=${encodeURIComponent(target.folderPath)}` +
        `&collection-id=${encodeURIComponent(target.collectionId)}`;
    const r = await fetch(url, { method: "POST" });
    if (!r.ok) throw new Error(`selectBook -> HTTP ${r.status}`);
}

export async function getCurrentXmatter(base) {
    try {
        // GET settings/xmatter -> { currentXmatter, xmatterOfferings:[{internalName,...}] }
        const data = robustJson(await apiGet(base, "settings/xmatter"));
        return data.currentXmatter || null;
    } catch {
        return null;
    }
}

// ---------- headless Chrome over CDP ----------
async function launchChrome(chromePath, cdpPort, udd) {
    fs.rmSync(udd, { recursive: true, force: true });
    const child = execFile(chromePath, [
        "--headless=new",
        "--no-sandbox",
        "--disable-gpu",
        "--hide-scrollbars",
        `--remote-debugging-port=${cdpPort}`,
        `--user-data-dir=${udd}`,
        "--remote-allow-origins=*",
        "about:blank",
    ]);
    for (let i = 0; i < 60; i++) {
        try {
            const list = await (
                await fetch(`http://localhost:${cdpPort}/json/list`)
            ).json();
            const page = list.find(
                (t) => t.type === "page" && t.webSocketDebuggerUrl,
            );
            if (page) return { child, wsUrl: page.webSocketDebuggerUrl };
        } catch {
            /* not up yet */
        }
        await sleep(250);
    }
    throw new Error("Chrome CDP endpoint never came up");
}

function cdpClient(wsUrl) {
    const ws = new WebSocket(wsUrl);
    let nextId = 1;
    const pending = new Map();
    const ready = new Promise((res, rej) => {
        ws.addEventListener("open", () => res());
        ws.addEventListener("error", (e) => rej(e));
    });
    ws.addEventListener("message", (ev) => {
        const msg = JSON.parse(ev.data);
        if (msg.id && pending.has(msg.id)) {
            const { resolve, reject } = pending.get(msg.id);
            pending.delete(msg.id);
            msg.error
                ? reject(new Error(JSON.stringify(msg.error)))
                : resolve(msg.result);
        }
    });
    const send = (method, params = {}) =>
        new Promise((resolve, reject) => {
            const id = nextId++;
            pending.set(id, { resolve, reject });
            ws.send(JSON.stringify({ id, method, params }));
        });
    return { ready, send, close: () => ws.close() };
}

// Isolate one page in the whole-book preview so a viewport-sized screenshot frames
// exactly that page. `index` null => querySelector (first match); a number => the
// index-th element of querySelectorAll (used to walk every content page).
// Returns {width,height,sizeClass} or null.
const isolateExpr = (selector, index = null) => `(() => {
    const pages = [...document.querySelectorAll('.bloom-page')];
    // Reset any prior isolation first, so capturing several pages from one preview
    // load works (isolating page A hides the others; page B must be shown again).
    pages.forEach(p => { p.style.display=''; p.style.transform=''; p.style.position=''; p.style.left=''; p.style.top=''; p.style.margin=''; });
    const sel = ${JSON.stringify(selector)};
    const page = ${index == null ? "document.querySelector(sel)" : `document.querySelectorAll(sel)[${index}]`};
    if (!page || !page.offsetWidth) return null;
    pages.forEach(p => { if (p !== page) p.style.display = 'none'; });
    for (let el = page.parentElement; el && el !== document.documentElement; el = el.parentElement) {
        el.style.transform = 'none'; el.style.margin = '0'; el.style.padding = '0';
    }
    document.documentElement.style.margin = '0'; document.body.style.margin = '0';
    page.style.transform = 'none'; page.style.position = 'absolute';
    page.style.left = '0'; page.style.top = '0'; page.style.margin = '0';
    const sizeClass = (page.className.match(/[A-Za-z0-9]+(Portrait|Landscape)/) || [])[0] || '';
    return { width: Math.ceil(page.offsetWidth), height: Math.ceil(page.offsetHeight), sizeClass };
})()`;

// ---------- PDF rendering ----------
export const PAPER_SIZES = {
    Letter: { w: 8.5, h: 11 },
    A4: { w: 8.27, h: 11.69 },
};

// Render a URL to a PDF file using a short-lived, isolated headless Chrome (its own
// CDP port + profile), so it never disturbs a running CaptureSession. Waits for all
// images to finish loading before printing. paper is "Letter" | "A4".
export async function renderUrlToPdf(url, outPath, opts = {}) {
    const paper = PAPER_SIZES[opts.paper] || PAPER_SIZES.Letter;
    const cdpPort = opts.cdpPort ?? 9334;
    const udd =
        opts.udd || path.join(path.dirname(outPath), ".chrome-pdf-profile");
    const margin = opts.marginInches ?? 0.5;
    const { child, wsUrl } = await launchChrome(
        opts.chromePath || CHROME_DEFAULT,
        cdpPort,
        udd,
    );
    const cdp = cdpClient(wsUrl);
    await cdp.ready;
    await cdp.send("Page.enable");
    await cdp.send("Runtime.enable");
    try {
        await cdp.send("Page.navigate", { url });
        // Poll until the document is loaded and every <img> is complete (or give up).
        for (let i = 0; i < 60; i++) {
            await sleep(150);
            const done = (
                await cdp.send("Runtime.evaluate", {
                    expression: `document.readyState==="complete" && [...document.images].every(im=>im.complete && im.naturalWidth>0)`,
                    returnByValue: true,
                })
            ).result.value;
            if (done) break;
        }
        const pdf = await cdp.send("Page.printToPDF", {
            paperWidth: paper.w,
            paperHeight: paper.h,
            marginTop: margin,
            marginBottom: margin,
            marginLeft: margin,
            marginRight: margin,
            printBackground: true,
            scale: 1,
        });
        fs.writeFileSync(outPath, Buffer.from(pdf.data, "base64"));
    } finally {
        try {
            cdp.close();
        } catch {
            /* ignore */
        }
        try {
            child.kill();
        } catch {
            /* ignore */
        }
    }
    return outPath;
}

// ---------- CaptureSession ----------
// Owns one headless Chrome + the selected book, so many captures reuse one browser
// and one book selection. Call start(), then captureCombo(...) repeatedly, then
// restore()/close(). Every captureCombo re-hydrates the book in place via setState.
export class CaptureSession {
    constructor(opts) {
        this.base = opts.base || "http://localhost:8089/bloom";
        this.out = opts.out;
        this.settleMs = opts.settleMs ?? 400;
        this.loadMs = opts.loadMs ?? 1500;
        this.chromePath = opts.chromePath || CHROME_DEFAULT;
        this.cdpPort = opts.cdpPort ?? 9333;
        this.book = opts.book || null;
        this.udd = opts.udd || path.join(opts.out, ".chrome-profile");
        this.baseline = { branding: null, xmatter: null, layout: null };
        this._nav = 0; // cache-buster counter (avoids Date.now churn)
    }

    // Launch Chrome, connect CDP, select the target book, and record baseline state.
    async start() {
        this.baseline.branding = await apiGet(this.base, "settings/branding");
        this.bookTarget = await getBookTarget(this.base, this.book);
        await selectBook(this.base, this.bookTarget);
        const { child, wsUrl } = await launchChrome(
            this.chromePath,
            this.cdpPort,
            this.udd,
        );
        this.child = child;
        this.cdp = cdpClient(wsUrl);
        await this.cdp.ready;
        await this.cdp.send("Page.enable");
        await this.cdp.send("Runtime.enable");
        // One generous, fixed viewport for the whole session so page measurements are
        // stable and crisp (a small/variable viewport makes the preview constrain width).
        await this.cdp.send("Emulation.setDeviceMetricsOverride", {
            width: 1800,
            height: 2800,
            deviceScaleFactor: 2,
            mobile: false,
        });
        return this.bookTarget;
    }

    // Read the book's current layout size-class from the live preview (e.g. "A5Portrait").
    async detectLayout() {
        await this.cdp.send("Page.navigate", {
            url: `${this.base}/book-preview/index.htm?t=${this._nav++}_base`,
        });
        await sleep(this.loadMs);
        const v = (
            await this.cdp.send("Runtime.evaluate", {
                expression: `(document.querySelector('.bloom-page')?.className.match(/[A-Za-z0-9]+(Portrait|Landscape)/)||[])[0]||''`,
                returnByValue: true,
            })
        ).result.value;
        return v || null;
    }

    // Render one (branding, layout, xmatter) combo and screenshot each requested page.
    // Returns an array of cell records (also writes the PNGs under this.out).
    // layout/xmatter may be null to leave the book's current value.
    async captureCombo(branding, layout, xmatter, pages) {
        await setState(this.base, { branding, layout, xmatter });
        await sleep(this.settleMs);
        // Warn if a requested xmatter isn't offered by the collection and silently fell
        // back (project-specific packs aren't selectable this way).
        let xmatterWarn = null;
        if (xmatter) {
            const applied = await getCurrentXmatter(this.base);
            if (applied && applied !== xmatter)
                xmatterWarn = `requested xmatter '${xmatter}' not applied (using '${applied}')`;
        }
        await this.cdp.send("Page.navigate", {
            url: `${this.base}/book-preview/index.htm?t=${this._nav++}_cell`,
        });
        await sleep(this.loadMs);

        const records = [];
        // Isolate one page (by selector + optional index), screenshot it, write the PNG,
        // and push a record. pageValue is the manifest page key; pageLabel is the caption.
        const shoot = async (selector, index, pageValue, pageLabel) => {
            const box = (
                await this.cdp.send("Runtime.evaluate", {
                    expression: isolateExpr(selector, index),
                    returnByValue: true,
                })
            ).result.value;
            const rec = {
                branding,
                layout: layout || null,
                xmatter: xmatter || null,
                page: pageValue,
                pageLabel,
            };
            if (!box) {
                rec.ok = false;
                rec.error = "page not present";
                records.push(rec);
                return;
            }
            const shot = await this.cdp.send("Page.captureScreenshot", {
                format: "png",
                captureBeyondViewport: true,
                clip: {
                    x: 0,
                    y: 0,
                    width: box.width,
                    height: box.height,
                    scale: 2,
                },
            });
            const rel = path.join(
                safe(layout || "current"),
                safe(xmatter || "current"),
                safe(branding),
                `${safe(pageValue)}.png`,
            );
            const abs = path.join(this.out, rel);
            fs.mkdirSync(path.dirname(abs), { recursive: true });
            fs.writeFileSync(abs, Buffer.from(shot.data, "base64"));
            rec.ok = true;
            rec.file = rel.split(path.sep).join("/");
            rec.width = box.width;
            rec.height = box.height;
            rec.sizeClass = box.sizeClass;
            if (xmatterWarn) rec.note = xmatterWarn;
            records.push(rec);
        };

        for (const pageName of pages) {
            const spec = PAGES[pageName];
            if (!spec) {
                records.push({
                    branding,
                    layout: layout || null,
                    xmatter: xmatter || null,
                    page: pageName,
                    pageLabel: pageName,
                    ok: false,
                    error: "unknown page",
                });
                continue;
            }
            if (spec.multi) {
                // Expand to every matching page: content-1, content-2, …
                const count =
                    (
                        await this.cdp.send("Runtime.evaluate", {
                            expression: `document.querySelectorAll(${JSON.stringify(spec.selector)}).length`,
                            returnByValue: true,
                        })
                    ).result.value || 0;
                if (!count) {
                    records.push({
                        branding,
                        layout: layout || null,
                        xmatter: xmatter || null,
                        page: pageName,
                        pageLabel: spec.label,
                        ok: false,
                        error: "no content pages",
                    });
                    continue;
                }
                for (let i = 0; i < count; i++)
                    await shoot(
                        spec.selector,
                        i,
                        `${pageName}-${i + 1}`,
                        `Content ${i + 1}`,
                    );
            } else {
                await shoot(spec.selector, null, pageName, spec.label);
            }
        }
        return records;
    }

    // Put the book back the way we found it (branding always; layout/xmatter only if given).
    async restore(opts = {}) {
        await setState(this.base, {
            branding: this.baseline.branding,
            layout: opts.layout ? this.baseline.layout : null,
            xmatter: opts.xmatter ? this.baseline.xmatter : null,
        });
    }

    close() {
        try {
            this.cdp?.close();
        } catch {
            /* ignore */
        }
        try {
            this.child?.kill();
        } catch {
            /* ignore */
        }
    }
}
