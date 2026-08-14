// Decompose the save round trip: how much of it is real work (gathering the page content in
// the browser, and C# writing it) versus the overhead of C# having to ASK the browser and
// wait for an HTTP callback -- which is the only part removing the round trip can save.
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

const b = await chromium.connectOverCDP("http://127.0.0.1:8091");
const shell = b
    .contexts()
    .flatMap((c) => c.pages())
    .find(
        (p) =>
            p.url().includes("/bloom/") && !p.url().startsWith("devtools://"),
    );
const frame = () => shell.frames().find((f) => f.name() === "page");

const res = await frame().evaluate(async () => {
    const ex = window.editablePageBundle;
    const gather = [];
    const save = [];
    let size = 0;
    // warm up
    await ex.getPageContentForSaveWhenReady();
    for (let i = 0; i < 15; i++) {
        const t = performance.now();
        // Includes the (normally zero) wait for in-flight page changes to settle, because that
        // is what a real save pays: see whenNoActiveDelays in bookEdit/js/pageContentDelays.ts.
        const s = await ex.getPageContentForSaveWhenReady();
        gather.push(performance.now() - t);
        size = s.length;
    }
    for (let i = 0; i < 8; i++) {
        const t = performance.now();
        await ex.savePageWithoutReloading();
        save.push(performance.now() - t);
    }
    const med = (a) => {
        const v = [...a].sort((x, y) => x - y);
        return (
            Math.round(
                (v.length % 2
                    ? v[(v.length - 1) / 2]
                    : (v[v.length / 2 - 1] + v[v.length / 2]) / 2) * 10,
            ) / 10
        );
    };
    return {
        pageId: document.querySelector(".bloom-page")?.getAttribute("id"),
        contentBytes: size,
        gatherMedianMs: med(gather),
        gatherAllMs: gather.map((x) => Math.round(x * 10) / 10),
        saveRoundTripMedianMs: med(save),
        saveAllMs: save.map((x) => Math.round(x)),
    };
});
console.log(JSON.stringify(res, null, 1));
await b.close();
