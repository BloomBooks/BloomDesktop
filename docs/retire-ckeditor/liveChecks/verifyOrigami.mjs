// Live check C (BL-6681 Stage 1): Change Layout mode. Origami's own Ctrl+Z/Ctrl+Y keep working and
// fire once; the Undo button reaches origamiUndo through the stack.
//
// Needs Bloom on the Edit tab of a book whose content pages are customPages (a Basic Book). Leaves
// the layout as it found it; leaving layout mode saves and reloads the page.
import { setup } from "./verifyCommon.mjs";
const h = await setup();
await h.gotoContentPage();
await h.instrument();
const splitCount = () =>
    h.pf.evaluate(
        () => document.querySelectorAll(".marginBox .split-pane").length,
    );
const inLayoutMode = () =>
    h.pf.evaluate(
        () => !!document.querySelector(".marginBox.origami-layout-mode"),
    );
const toggleLabel = () =>
    h.pf.locator("label[for=changeLayoutToggle], .onoffswitch-label").first();
console.log(
    "toggle present:",
    await h.pf.locator("#changeLayoutToggle").count(),
    "layout mode:",
    await inLayoutMode(),
    "splits:",
    await splitCount(),
);
if ((await h.pf.locator("#changeLayoutToggle").count()) === 0)
    throw new Error("no Change Layout toggle on this page");
if (!(await inLayoutMode())) {
    await toggleLabel().click();
    await h.p.waitForTimeout(1200);
}
h.record(
    "C1 entered Change Layout mode",
    await inLayoutMode(),
    `layout=${await inLayoutMode()}`,
);
const splits0 = await splitCount();
console.log("canUndo in layout mode before any change:", await h.canUndo());
// Make a layout change. The split buttons only show on hover, so click at the DOM level (origami
// binds them with jQuery click).
await h.pf.evaluate(() =>
    document
        .querySelector(
            ".split-pane-component-inner .button.add-bottom, .split-pane-component-inner .button.add-right",
        )
        .click(),
);
await h.p.waitForTimeout(800);
const splits1 = await splitCount();
h.record(
    "C2 a split was made",
    splits1 > splits0,
    `splits ${splits0} -> ${splits1}`,
);
h.record(
    "C3 canUndo=yes via origami",
    (await h.canUndo()) === "yes",
    `button disabled=${await h.undoButtonDisabled()}`,
);
// Keyboard: Ctrl+Z (origami's handler on html), then Ctrl+Y. Ours must not double it.
await h.pf.locator("html").click({ position: { x: 5, y: 5 }, force: true });
await h.resetCalls();
await h.calls();
await h.p.keyboard.press("Control+z");
await h.p.waitForTimeout(800);
const splitsZ = await splitCount();
const cz = await h.calls();
await h.p.keyboard.press("Control+y");
await h.p.waitForTimeout(800);
const splitsY = await splitCount();
const cy = await h.calls();
console.log(`Ctrl+Z: splits=${splitsZ} calls=${JSON.stringify(cz)}`);
console.log(`Ctrl+Y: splits=${splitsY} calls=${JSON.stringify(cy)}`);
h.record(
    "C4 Ctrl+Z undid the split once (origami), no CKEditor command",
    splitsZ === splits0 && cz.ckCmds.length === 0,
    `splits=${splitsZ}`,
);
h.record(
    "C5 Ctrl+Y redid the split exactly once (origami); our binding declined",
    splitsY === splits1 && cy.redo === 0,
    `splits=${splitsY} redo=${cy.redo}`,
);
// Button path: Undo button -> stack -> origami provider.
await h.resetCalls();
await h.calls();
await h.pressUndoButton();
const cb = await h.calls();
const splitsB = await splitCount();
h.record(
    "C6 Undo button -> origamiUndo through the stack (ori=1, others 0)",
    cb.ori === 1 &&
        cb.tb === 0 &&
        cb.ck === 0 &&
        cb.img === 0 &&
        splitsB === splits0,
    `calls=${JSON.stringify(cb)} splits=${splitsB}`,
);
// Leave layout mode (this saves and reloads the page).
await toggleLabel().click();
await h.p.waitForTimeout(3500);
h.pf = h.pageFrame();
console.log(
    "layout mode after leaving:",
    await inLayoutMode(),
    "splits:",
    await splitCount(),
    "canUndo:",
    await h.canUndo(),
);
h.summary();
await h.browser.close();
