// Live check B (BL-6681 Stage 1): with no reader tool, the Undo button reaches CKEditor's undo;
// Ctrl+Y redoes exactly once (CKEditor's) and our binding declines.
//
// Needs Bloom on the Edit tab of a book whose toolbox has no reader tool (a Basic Book). Restores
// the text it typed into.
import { setup } from "./verifyCommon.mjs";
const h = await setup();
await h.gotoContentPage();
await h.instrument();
console.log("active tool:", await h.activeTool());
const originalHtml = await h.readHtml();
const before = await h.readText();
const norm = (s) => s.replace(/​/g, "").replace(/ /g, " ");
console.log(
    "button disabled before typing?",
    await h.undoButtonDisabled(),
    "canUndo:",
    await h.canUndo(),
);

// --- button path ---
await h.clickEnd();
await h.p.keyboard.type(" dog");
await h.p.waitForTimeout(1500);
const typed = await h.readText();
h.record(
    "B1 typing landed",
    norm(typed) === norm(before) + " dog",
    `before='${before}' after=${JSON.stringify(typed)}`,
);
h.record(
    "B2 canUndo=yes after typing",
    (await h.canUndo()) === "yes",
    `button disabled=${await h.undoButtonDisabled()}`,
);
await h.resetCalls();
await h.calls();
await h.pressUndoButton();
const c = await h.calls();
h.record(
    "B3 Undo button -> CKEditor undo only (ck=1, tb=0, markup=1)",
    c.ck === 1 && c.tb === 0 && c.markup === 1,
    JSON.stringify(c),
);
const afterUndo = await h.readText();
h.record(
    "B4 CKEditor undo changed the text back",
    afterUndo !== typed,
    `text now ${JSON.stringify(afterUndo)}`,
);
console.log(
    "canUndo after undo:",
    await h.canUndo(),
    "button disabled:",
    await h.undoButtonDisabled(),
);

// --- keyboard path ---
await h.writeHtml(originalHtml);
await h.p.waitForTimeout(300);
await h.clickEnd();
await h.p.keyboard.type(" cat");
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
    "B5 Ctrl+Z ran CKEditor's undo (its command), not ours",
    cz.ckCmds.includes("undo") && cz.ck === 0,
    JSON.stringify(cz),
);
h.record(
    "B6 Ctrl+Y ran CKEditor's redo exactly once and our binding declined",
    cy.ckCmds.filter((n) => n === "redo").length === 1 && cy.redo === 0,
    JSON.stringify(cy),
);
h.record(
    "B7 Ctrl+Z/Ctrl+Y round trip restores the typed text",
    norm(afterY) === norm(typed2),
    `typed=${JSON.stringify(typed2)} y=${JSON.stringify(afterY)}`,
);

// --- restore ---
await h.writeHtml(originalHtml);
await h.p.waitForTimeout(500);
console.log("restored text:", JSON.stringify(await h.readText()));
h.summary();
await h.browser.close();
