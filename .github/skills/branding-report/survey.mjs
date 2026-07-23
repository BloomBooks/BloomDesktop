// branding-report / survey.mjs
//
// Batch capture stage: drive the RUNNING Bloom to render one book across a matrix
// of axes — branding × layout × xmatter × page — and screenshot each page into an
// output folder, with a manifest the viewer reads. Pure Node (no AI tokens).
//
// For on-demand, interactive capture (check a branding, it renders now) use
// control.mjs instead. Both share the engine in capture-core.mjs.
//
// Usage examples:
//   node survey.mjs --brandings Default,WorldVision --pages back,title
//   node survey.mjs --brandings all --pages back
//   node survey.mjs --brandings Default,Kyrgyzstan2020 --pages back --layouts A5Portrait,Device16x9Landscape --xmatters Factory,Kyrgyzstan2020
import fs from "node:fs";
import path from "node:path";
import { CaptureSession, PAGES, getCurrentXmatter } from "./capture-core.mjs";

const REPO = path.resolve(import.meta.dirname, "..", "..", "..");
const SRC_BRANDING = path.join(REPO, "src", "content", "branding");

function parseArgs() {
    const a = process.argv.slice(2);
    const opts = {
        base: "http://localhost:8089/bloom",
        out: path.resolve("branding-report-out"),
        brandings: ["Default"],
        pages: ["front", "title", "credits", "back"],
        layouts: [null], // null = leave book's current layout
        xmatters: [null], // null = leave collection's current xmatter
        book: null, // null = use currently-selected / first non-factory book
        settleMs: 400,
        loadMs: 1500,
    };
    for (let i = 0; i < a.length; i++) {
        const [k, inlineV] = a[i].split(/=(.*)/);
        const val = () => (inlineV !== undefined ? inlineV : a[++i]);
        if (k === "--base") opts.base = val();
        else if (k === "--out") opts.out = path.resolve(val());
        else if (k === "--book") opts.book = val();
        else if (k === "--brandings") opts.brandings = val().split(",");
        else if (k === "--pages") opts.pages = val().split(",");
        else if (k === "--layouts") opts.layouts = val().split(",");
        else if (k === "--xmatters") opts.xmatters = val().split(",");
        else if (k === "--settle-ms") opts.settleMs = Number(val());
        else if (k === "--load-ms") opts.loadMs = Number(val());
    }
    if (opts.brandings.length === 1 && opts.brandings[0] === "all") {
        opts.brandings = fs
            .readdirSync(SRC_BRANDING)
            .filter((f) =>
                fs.statSync(path.join(SRC_BRANDING, f)).isDirectory(),
            )
            .filter((f) => f !== "shared");
    }
    if (opts.pages.length === 1 && opts.pages[0] === "all")
        opts.pages = Object.keys(PAGES);
    return opts;
}

async function main() {
    const opts = parseArgs();
    fs.mkdirSync(opts.out, { recursive: true });

    const varyLayout = opts.layouts.some((l) => l);
    const varyXmatter = opts.xmatters.some((x) => x);

    const session = new CaptureSession({
        base: opts.base,
        out: opts.out,
        settleMs: opts.settleMs,
        loadMs: opts.loadMs,
        book: opts.book,
    });
    if (varyXmatter)
        session.baseline.xmatter = await getCurrentXmatter(opts.base);
    const bookTarget = await session.start();
    console.log(`Book: ${bookTarget.folderPath}`);
    console.log(
        `Axes: ${opts.brandings.length} branding × ${opts.pages.length} page × ` +
            `${opts.layouts.length} layout × ${opts.xmatters.length} xmatter`,
    );
    // Capture the book's true current layout BEFORE the loop changes anything.
    if (varyLayout) session.baseline.layout = await session.detectLayout();

    const manifest = [];
    let n = 0;
    const total =
        opts.layouts.length * opts.xmatters.length * opts.brandings.length;
    try {
        for (const layout of opts.layouts) {
            for (const xmatter of opts.xmatters) {
                for (const branding of opts.brandings) {
                    n++;
                    const tag = `${branding}${layout ? "/" + layout : ""}${xmatter ? "/" + xmatter : ""}`;
                    process.stdout.write(`[${n}/${total}] ${tag} ... `);
                    try {
                        const recs = await session.captureCombo(
                            branding,
                            layout,
                            xmatter,
                            opts.pages,
                        );
                        for (const r of recs) manifest.push(r);
                        const warn = recs.find((r) => r.note)?.note;
                        console.log(
                            recs
                                .map(
                                    (r) =>
                                        `${r.page}${r.ok ? "✓" : ":" + (r.error || "?")}`,
                                )
                                .join(" ") + (warn ? `  ⚠ ${warn}` : ""),
                        );
                    } catch (e) {
                        manifest.push({
                            branding,
                            layout: layout || null,
                            xmatter: xmatter || null,
                            page: "*",
                            ok: false,
                            error: String(e.message || e),
                        });
                        console.log("FAILED:", e.message || e);
                    }
                }
            }
        }
    } finally {
        console.log(`Restoring baseline: ${JSON.stringify(session.baseline)}`);
        try {
            await session.restore({ layout: varyLayout, xmatter: varyXmatter });
        } catch (e) {
            console.log("WARN: could not restore baseline:", e.message || e);
        }
        session.close();
    }

    fs.writeFileSync(
        path.join(opts.out, "manifest.json"),
        JSON.stringify(
            {
                generatedAxes: { book: bookTarget.folderPath },
                baseline: session.baseline,
                cells: manifest,
            },
            null,
            2,
        ),
    );
    const ok = manifest.filter((m) => m.ok).length;
    console.log(
        `Done. ${ok}/${manifest.length} page-captures. Manifest: ${opts.out}/manifest.json`,
    );
}

main().catch((e) => {
    console.error(e);
    process.exit(1);
});
