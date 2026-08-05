// BL-6681 Stage 0 live-verification harness: does the caret survive the markup pipeline?
// Inventory rows G1/G2 (docs/retire-ckeditor/BEHAVIOR-INVENTORY.md).
//
// USAGE (needs a running Bloom on the Edit tab with a text box on the current page):
//   node .github/skills/bloom-automation/launcherControl.mjs --ensure-running --wait-ready --json
//   node docs/retire-ckeditor/verifyCaretPreservation.mjs <cdpPort>
//
// WHAT IT COVERS. In toolbox.ts's handleKeyboardInput, the
// saveSelectionForMarkup / restoreSelectionAfterMarkup bracket runs on EVERY keystroke in an
// editable (after the 500ms debounce), regardless of whether a tool is active — the tool check
// only gates the updateMarkup call between them. So typing mid-word and checking the caret
// afterwards exercises the wiring of the seam extracted in commit 2707d98a8, and confirms the
// bookmark spans are consumed rather than left in the DOM.
//
// WHAT IT DOES *NOT* COVER, and how to extend it. With no tool active, updateMarkup never runs,
// so the DOM is unchanged between save and restore — which is the easy half. The case bookmarks
// actually exist for is markup rewriting the DOM around the caret. To reach it you need a book
// whose toolbox offers a reader tool: a **Decodable Reader** or **Leveled Reader** book, not a
// Basic Book. In a Basic Book the toolbox offers only Talking Book and "More...", both of which
// stayed invisible even after toggling #pure-toggle-right, and no audio-sentence spans appeared,
// so updateMarkup did not run. Also remember toolbox.toolboxIsShowing() gates markup, so the
// toolbox pane has to be genuinely open, not merely toggled.
import { createRequire } from "node:module";
import path from "node:path";

const repoRoot = "C:/github/BloomDesktop";
const ctDir = path.join(
    repoRoot,
    "src/BloomBrowserUI/react_components/component-tester",
);
const req = createRequire(path.join(ctDir, "package.json"));
const { chromium } = req("playwright");

const cdpPort = process.argv[2] || "8091";
const browser = await chromium.connectOverCDP(`http://localhost:${cdpPort}`);

const mainPage = (() => {
    for (const c of browser.contexts())
        for (const p of c.pages())
            if (
                p.url().includes("/bloom/") &&
                !p.url().includes("toolboxcontent")
            )
                return p;
    return undefined;
})();
if (!mainPage) throw new Error("no Bloom main page");

const pageFrame = mainPage.frames().find((f) => f.name() === "page");
if (!pageFrame) {
    console.log(
        "FRAMES:",
        mainPage.frames().map((f) => `${f.name()}|${f.url().slice(0, 80)}`),
    );
    throw new Error("no 'page' frame");
}
console.log("page frame url:", pageFrame.url().slice(0, 90));

// Pick the first visible bloom-editable we can type into.
const info = await pageFrame.evaluate(() => {
    const eds = Array.from(
        document.querySelectorAll(
            ".bloom-editable.bloom-visibility-code-on[contenteditable='true']",
        ),
    );
    return eds.map((e, i) => ({
        i,
        lang: e.getAttribute("lang"),
        cls: (e.className || "")
            .split(" ")
            .filter((c) => c.startsWith("bloom-content"))
            .join(","),
        text: (e.textContent || "").trim().slice(0, 40),
        hasCkEditor: !!e.bloomCkEditor,
    }));
});
console.log("editables:", JSON.stringify(info, null, 1));

const target = info.find((e) => e.hasCkEditor);
if (!target) {
    console.log(
        "RESULT: no editable has a CKEditor attached — cannot exercise the pipeline here",
    );
    await browser.close();
    process.exit(3);
}
console.log(`using editable index ${target.i} (lang=${target.lang})`);

const sel = `.bloom-editable.bloom-visibility-code-on[contenteditable='true']`;

// --- Step 1: put a known word in the box -------------------------------------------------
await pageFrame.evaluate(
    ({ s, idx }) => {
        const ed = document.querySelectorAll(s)[idx];
        const p =
            ed.querySelector("p") ||
            ed.appendChild(document.createElement("p"));
        p.textContent = "house";
    },
    { s: sel, idx: target.i },
);
await mainPage.waitForTimeout(300);

// --- Step 2: click in, then place the caret between "hous" and "e" (offset 4) ------------
await pageFrame.locator(sel).nth(target.i).click();
await mainPage.waitForTimeout(400);

const placed = await pageFrame.evaluate(
    ({ s, idx }) => {
        const ed = document.querySelectorAll(s)[idx];
        const textNode = ed.querySelector("p").firstChild;
        const r = document.createRange();
        r.setStart(textNode, 4);
        r.setEnd(textNode, 4);
        const g = window.getSelection();
        g.removeAllRanges();
        g.addRange(r);
        return { text: ed.textContent, anchorOffset: g.anchorOffset };
    },
    { s: sel, idx: target.i },
);
console.log("caret placed:", JSON.stringify(placed));
if (placed.anchorOffset !== 4) {
    console.log("RESULT: FAILED SETUP — could not place caret at offset 4");
    await browser.close();
    process.exit(4);
}

// --- Step 3: type a character mid-word, then wait out the 500ms markup debounce ----------
await mainPage.keyboard.type("z");
console.log("typed 'z' at offset 4; waiting 1500ms for the markup pass...");
await mainPage.waitForTimeout(1500);

// --- Step 4: observe where the caret ended up and what the text is -----------------------
const after = await pageFrame.evaluate(
    ({ s, idx }) => {
        const ed = document.querySelectorAll(s)[idx];
        const g = window.getSelection();
        // Character offset of the caret within the editable, counting text only.
        let offset = -1;
        if (g && g.anchorNode && ed.contains(g.anchorNode)) {
            const r = g.getRangeAt(0).cloneRange();
            r.setStart(ed, 0);
            offset = r.toString().length;
        }
        return {
            text: ed.textContent,
            html: ed.innerHTML,
            caretCharOffset: offset,
            anchorIsInsideEditable: !!(
                g &&
                g.anchorNode &&
                ed.contains(g.anchorNode)
            ),
            leftoverBookmarkSpans:
                ed.querySelectorAll("[id^='cke_bm_']").length,
            zeroWidthSpaces: (ed.innerHTML.match(/\u200B/g) || []).length,
        };
    },
    { s: sel, idx: target.i },
);
console.log("AFTER:", JSON.stringify(after, null, 1));

// Caret at offset 4 in "house" sits between "hous" and "e", so typing "z" gives "housze"
// and the caret should end up at offset 5, immediately after the character just typed.
const pass =
    after.text === "housze" &&
    after.caretCharOffset === 5 &&
    after.leftoverBookmarkSpans === 0 &&
    after.zeroWidthSpaces === 0;
console.log(
    pass
        ? "RESULT: PASS — text 'housze', caret at offset 5, no leftover bookmark spans, no ZWSP"
        : `RESULT: FAIL — expected text 'housze' caret 5 bookmarks 0 zwsp 0; got text '${after.text}' caret ${after.caretCharOffset} bookmarks ${after.leftoverBookmarkSpans} zwsp ${after.zeroWidthSpaces}`,
);

await browser.close();
