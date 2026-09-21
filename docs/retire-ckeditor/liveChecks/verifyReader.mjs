// Live check A (BL-6681 Stage 1): with a reader tool active, the Undo button reaches the
// reader-tools undo, not CKEditor's, and the keyboard path is unchanged.
//
// Needs Bloom on the Edit tab of a Decodable (or Leveled) Reader book with that tool active.
// Restores the text it typed into, but does leave the page "edited" as far as Bloom is concerned.
//
// A4, A6 and A7 FAIL today, and are expected to: they observe two pre-existing problems, recorded
// in PROGRESS.md (2026-09-07), that this project exists to remove. Re-run after Stage 3 expecting
// them to pass.
import { setup } from "./verifyCommon.mjs";
const h = await setup();
await h.gotoContentPage();
await h.instrument();
console.log("active tool:", await h.activeTool());
const originalHtml = await h.readHtml();
const before = await h.readText();
console.log(
    "button disabled before typing?",
    await h.undoButtonDisabled(),
    "canUndo:",
    await h.canUndo(),
);

// --- button path ---
await h.clickEnd();
await h.p.keyboard.type(" sun");
await h.p.waitForTimeout(1500); // the 500 ms markup debounce, with margin
const typed = await h.readText();
h.record(
    "A1 typing landed",
    typed === before + " sun",
    `before='${before}' after='${typed}'`,
);
h.record(
    "A2 canUndo=yes after typing",
    (await h.canUndo()) === "yes",
    `button disabled=${await h.undoButtonDisabled()}`,
);
await h.resetCalls();
await h.calls();
await h.pressUndoButton();
const c = await h.calls();
h.record(
    "A3 Undo button -> reader-tools undo only (tb=1, ck=0, markup=1, no CKEditor command)",
    c.tb === 1 && c.ck === 0 && c.markup === 1 && c.ckCmds.length === 0,
    JSON.stringify(c),
);
const afterUndo = await h.readText();
const afterUndoHtml = await h.readHtml();
h.record(
    "A4 (pre-existing) reader-tools undo reverted the text and left no bookmark span",
    afterUndo === before && !/cke_bm_/.test(afterUndoHtml),
    `text now ${JSON.stringify(afterUndo)}; bookmark spans in html: ${(afterUndoHtml.match(/cke_bm_/g) || []).length}`,
);

// --- keyboard path: Ctrl+Z, Ctrl+Y in the box; which mechanisms run? ---
await h.writeHtml(originalHtml);
await h.clickEnd();
await h.p.keyboard.type(" pot");
await h.p.waitForTimeout(1500);
const typed2 = await h.readText();
await h.resetCalls();
await h.calls();
await h.p.keyboard.press("Control+z");
await h.p.waitForTimeout(700);
const afterZ = await h.readText();
const cz = await h.calls();
await h.p.keyboard.press("Control+y");
await h.p.waitForTimeout(700);
const afterY = await h.readText();
const cy = await h.calls();
console.log(
    `Ctrl+Z: text=${JSON.stringify(afterZ)} calls=${JSON.stringify(cz)}`,
);
console.log(
    `Ctrl+Y: text=${JSON.stringify(afterY)} calls=${JSON.stringify(cy)}`,
);
h.record(
    "A5 our Redo binding never fires while the reader tool claims Ctrl+Y",
    cy.redo === 0,
    `redo calls=${cy.redo}`,
);
h.record(
    "A6 (pre-existing) CKEditor's own undo/redo commands did NOT also run on Ctrl+Z/Y",
    cz.ckCmds.length === 0 && cy.ckCmds.length === 0,
    `ckCmds z=${JSON.stringify(cz.ckCmds)} y=${JSON.stringify(cy.ckCmds)}`,
);
const strip = (s) => s.replace(/[​  ]+$/, "");
h.record(
    "A7 (pre-existing) Ctrl+Z/Ctrl+Y round trip restores the typed text",
    strip(afterY) === strip(typed2),
    `typed='${typed2}' y='${afterY}'`,
);

// --- restore the book text ---
await h.writeHtml(originalHtml);
await h.p.waitForTimeout(500);
console.log("restored text:", JSON.stringify(await h.readText()));
h.summary();
await h.browser.close();
