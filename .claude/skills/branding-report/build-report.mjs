// branding-report / build-report.mjs
// "Bake" a self-contained HTML report from a survey output folder: embeds the (optionally
// filtered) PNGs as data URIs so the single file can be shared or published as a Claude
// Artifact. For interactive exploration of a large matrix use the viewer (serve.mjs) instead
// — a fully self-contained page can't hold thousands of images.
//
// Usage:
//   node build-report.mjs --out branding-report-out
//   node build-report.mjs --out br-out --brandings Default,WorldVision --pages back --group-by branding
import fs from "node:fs";
import path from "node:path";

const AXES = ["branding", "page", "layout", "xmatter"];
// survey.mjs uses plural flags; map them to the singular axis keys used in the manifest.
const PLURAL = {
    brandings: "branding",
    pages: "page",
    layouts: "layout",
    xmatters: "xmatter",
};

function parseArgs() {
    const a = process.argv.slice(2);
    const o = {
        out: path.resolve("branding-report-out"),
        groupBy: "branding",
        title: "Bloom branding-report",
    };
    for (let i = 0; i < a.length; i++) {
        const [k, inlineV] = a[i].split(/=(.*)/);
        const val = () => (inlineV !== undefined ? inlineV : a[++i]);
        if (k === "--out") o.out = path.resolve(val());
        else if (k === "--report") o.report = path.resolve(val());
        else if (k === "--group-by") o.groupBy = val();
        else if (k === "--title") o.title = val();
        else if (k.startsWith("--") && PLURAL[k.slice(2)])
            (o.filters ??= {})[PLURAL[k.slice(2)]] = new Set(val().split(","));
    }
    o.report ??= path.join(o.out, "report.html");
    return o;
}

const esc = (s) =>
    String(s).replace(
        /[&<>"]/g,
        (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c],
    );

function main() {
    const o = parseArgs();
    const manifest = JSON.parse(
        fs.readFileSync(path.join(o.out, "manifest.json"), "utf8"),
    );
    let cells = manifest.cells.filter((c) => c.ok && c.file);
    if (o.filters)
        cells = cells.filter((c) =>
            AXES.every(
                (a) => !o.filters[a] || o.filters[a].has(c[a] ?? "current"),
            ),
        );
    if (!cells.length) {
        console.error("No matching captures to bake.");
        process.exit(1);
    }
    const dataUri = (rel) =>
        "data:image/png;base64," +
        fs.readFileSync(path.join(o.out, rel)).toString("base64");

    // group
    const groups = new Map();
    for (const c of cells) {
        const key = c[o.groupBy] ?? "current";
        (groups.get(key) ?? groups.set(key, []).get(key)).push(c);
    }
    const otherAxes = AXES.filter((a) => a !== o.groupBy);
    const caption = (c) =>
        otherAxes
            .filter((a) => (c[a] ?? "current") !== "current" || a === "page")
            .map((a) => `${a}: <b>${esc(c[a] ?? "current")}</b>`)
            .join(" · ");

    const sections = [...groups.entries()]
        .map(
            ([g, list]) => `
  <section>
    <h2>${esc(g)}</h2>
    <div class="grid">
      ${list.map((c) => `<figure><div class="frame"><img loading="lazy" src="${dataUri(c.file)}" alt="${esc(caption(c))}"/></div><figcaption>${caption(c)}</figcaption></figure>`).join("\n      ")}
    </div>
  </section>`,
        )
        .join("\n");

    const html = `<style>
:root{color-scheme:light dark;--ground:#faf8f6;--panel:#fff;--ink:#23201e;--muted:#6b645f;--hairline:#e7e0d9;--accent:#d65649;--surround:#ece8e3}
@media (prefers-color-scheme:dark){:root{--ground:#1a1817;--panel:#232019;--ink:#ece7e2;--muted:#a59d95;--hairline:#38332e;--accent:#e8776b;--surround:#0f0e0d}}
*{box-sizing:border-box}html{background:var(--ground)}
.wrap{max-width:1200px;margin:0 auto;padding:36px 24px 64px;font-family:"Segoe UI",system-ui,sans-serif;color:var(--ink)}
.eyebrow{text-transform:uppercase;letter-spacing:.14em;font-size:.72rem;font-weight:600;color:var(--accent);margin:0 0 .5em}
h1{font-size:1.7rem;margin:0 0 .3em;font-weight:700}
.lead{color:var(--muted);max-width:70ch;line-height:1.5;margin:0}
section{margin-top:34px;padding-top:16px;border-top:1px solid var(--hairline)}
h2{margin:0 0 14px;font-size:1.15rem}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:22px}
figure{margin:0;display:flex;flex-direction:column;gap:8px}
.frame{background:var(--surround);border:1px solid var(--hairline);border-radius:6px;overflow:hidden;box-shadow:0 6px 20px -8px rgba(0,0,0,.28)}
.frame img{width:100%;height:auto;display:block}
figcaption{font-size:.8rem;color:var(--muted)}figcaption b{color:var(--ink)}
footer{margin-top:40px;padding-top:16px;border-top:1px solid var(--hairline);color:var(--muted);font-size:.8rem}
</style>
<div class="wrap">
  <p class="eyebrow">Bloom · BL-16370 · branding-report</p>
  <h1>${esc(o.title)}</h1>
  <p class="lead">Real pages rendered by Bloom, grouped by <b>${esc(o.groupBy)}</b>. ${cells.length} captures · book <code>${esc(path.basename(manifest.generatedAxes?.book || ""))}</code>.</p>
  ${sections}
  <footer>Baked from <code>${esc(path.basename(o.out))}/</code> by build-report.mjs. Interactive matrix/filter view: <code>node serve.mjs ${esc(path.basename(o.out))}</code>.</footer>
</div>`;
    fs.writeFileSync(o.report, html);
    console.log(
        `Wrote ${o.report} (${cells.length} captures, grouped by ${o.groupBy})`,
    );
}
main();
