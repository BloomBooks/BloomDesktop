// Benchmark a real page change end to end, from outside Bloom, so the same harness can be
// run before and after the pageClicked change and the numbers compared.
//
// Phases (all observed over CDP; no instrumentation added to Bloom):
//   click        -> we dispatch a real click on the page-list thumbnail
//   pageClicked  -> POST pageList/pageClicked completes
//   pageContent  -> POST editView/pageContent completes  (the browser has handed C# the
//                   outgoing page's content: this is the round trip the change removes)
//   domLoaded    -> POST editView/pageDomLoaded fires    (the NEW page's DOM is up)
//   editable     -> the new page reports its id with CKEditor attached (usable)
import { createRequire } from "node:module";
import path from "node:path";
const r = createRequire(
    path.join(
        "C:/github/BloomDesktop",
        "src/BloomBrowserUI/react_components/component-tester/package.json",
    ),
);
const { chromium } = r("playwright");
const sleep = (ms) => new Promise((x) => setTimeout(x, ms));

const PAGES = [
    { id: "e9f55da7-b76d-4178-aa66-b062d744c6c0", label: "Basic Text & Image" },
    { id: "6799f146-e29d-4521-89d3-c1192ab606b4", label: "Title Page" },
];
const ITERATIONS = Number(process.argv[2] ?? 8);

const b = await chromium.connectOverCDP("http://127.0.0.1:8091");
const shell = b
    .contexts()
    .flatMap((c) => c.pages())
    .find(
        (p) =>
            p.url().includes("/bloom/") && !p.url().startsWith("devtools://"),
    );
const listFrame = () => shell.frames().find((f) => f.name() === "pageList");
const pageFrame = () => shell.frames().find((f) => f.name() === "page");

let marks = {};
const stamp = (name) => {
    if (marks[name] === undefined) marks[name] = Date.now();
};
shell.on("response", (res) => {
    const u = res.url();
    if (!u.includes("/bloom/api/")) return;
    if (u.includes("pageList/pageClicked")) stamp("pageClicked");
    else if (u.includes("editView/pageContent")) stamp("pageContent");
    else if (u.includes("editView/savePageInPlace")) stamp("savePageInPlace");
    else if (u.includes("editView/pageDomLoaded")) stamp("domLoaded");
});

const waitForPage = async (wantId, deadlineMs = 25000) => {
    const start = Date.now();
    while (Date.now() - start < deadlineMs) {
        const f = pageFrame();
        if (f) {
            try {
                const ok = await f.evaluate((wantId) => {
                    const p = document.querySelector(".bloom-page");
                    if (!p || p.getAttribute("id") !== wantId) return false;
                    const eds = Array.from(
                        document.querySelectorAll("div.bloom-editable"),
                    );
                    // "usable" = at least one editor attached (every page here has editable text)
                    return eds.some((d) => !!d.bloomCkEditor);
                }, wantId);
                if (ok) return Date.now();
            } catch {
                /* frame swapping */
            }
        }
        await sleep(20);
    }
    return null;
};

const clickPage = async (id) => {
    return listFrame().evaluate((id) => {
        const item = document.querySelector(`.gridItem[id="${id}"]`);
        if (!item) return "no gridItem " + id;
        const cover = item.querySelector(".invisibleThumbnailCover") || item;
        cover.dispatchEvent(
            new MouseEvent("click", {
                bubbles: true,
                cancelable: true,
                view: window,
            }),
        );
        return "ok";
    }, id);
};

const currentPageId = async () => {
    const f = pageFrame();
    if (!f) return null;
    try {
        return await f.evaluate(
            () =>
                document.querySelector(".bloom-page")?.getAttribute("id") ??
                null,
        );
    } catch {
        return null;
    }
};

// Settle before timing anything. Never assume which page Bloom starts on: each iteration below
// targets whichever of the two we are NOT currently on, so every timed click is a real change.
// (Clicking the page we are already on is not a no-op we can wait for -- and a click that lands
// while a navigation is still in flight is silently dropped, because SaveThen's "not in a state
// to save" fallback for pageClicked does nothing at all.)
await sleep(1500);

const rows = [];
for (let i = 0; i < ITERATIONS; i++) {
    const from = await currentPageId();
    const target = PAGES.find((p) => p.id !== from) ?? PAGES[0];
    marks = {};
    const t0 = Date.now();
    const clicked = await clickPage(target.id);
    if (clicked !== "ok") {
        console.log("CLICK FAILED:", clicked);
        break;
    }
    const tEditable = await waitForPage(target.id);
    if (!tEditable) {
        console.log("TIMED OUT waiting for", target.label);
        break;
    }
    rows.push({
        to: target.label,
        pageClicked: marks.pageClicked ? marks.pageClicked - t0 : null,
        pageContent: marks.pageContent ? marks.pageContent - t0 : null,
        savePageInPlace: marks.savePageInPlace
            ? marks.savePageInPlace - t0
            : null,
        domLoaded: marks.domLoaded ? marks.domLoaded - t0 : null,
        editable: tEditable - t0,
    });
    await sleep(1200); // let things quiesce between runs
}

const median = (xs) => {
    const v = xs
        .filter((x) => x !== null && x !== undefined)
        .sort((a, b) => a - b);
    if (!v.length) return null;
    return v.length % 2
        ? v[(v.length - 1) / 2]
        : Math.round((v[v.length / 2 - 1] + v[v.length / 2]) / 2);
};

console.log("\nper-change timings (ms from click):");
for (const row of rows) console.log("  " + JSON.stringify(row));
console.log("\nMEDIANS over " + rows.length + " changes:");
for (const k of [
    "pageClicked",
    "pageContent",
    "savePageInPlace",
    "domLoaded",
    "editable",
]) {
    const m = median(rows.map((x) => x[k]));
    console.log(`  ${k.padEnd(16)} ${m === null ? "(never seen)" : m + " ms"}`);
}
await b.close();
