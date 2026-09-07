// Live check E (BL-6681 Stage 2b): deleting a canvas element is undoable from the Undo button and
// redoable with Ctrl+Y, and the restored element is the same element in the same place with the
// family's bubble specs intact.
//
// Needs Bloom on the Edit tab of a book whose content pages have canvas elements (e.g. "The Moon
// and the Cap"). Leaves the book as it found it (delete, undo, redo, undo).
import { setup } from "./verifyCommon.mjs";
const h = await setup();

// Find a content page with at least two canvas elements in one canvas (so the family renumbering
// is exercised).
const thumbs = h.pageListFrame().locator(".gridItem");
const n = await thumbs.count();
let found = false;
for (let t = 1; t < n && !found; t++) {
    await thumbs.nth(t).click();
    await h.p.waitForTimeout(3000);
    h.pf = h.pageFrame();
    found = await h.pf.evaluate(() =>
        Array.from(document.querySelectorAll(".bloom-canvas")).some(
            (c) =>
                c.querySelectorAll(":scope > .bloom-canvas-element").length >=
                2,
        ),
    );
    console.log(`thumb ${t}: canvas with 2+ elements? ${found}`);
}
if (!found)
    throw new Error(
        "no page with a canvas holding two or more canvas elements",
    );
h.idx = 0;
await h.instrument();

const snapshot = () =>
    h.pf.evaluate(() => {
        const canvas = Array.from(
            document.querySelectorAll(".bloom-canvas"),
        ).find(
            (c) =>
                c.querySelectorAll(":scope > .bloom-canvas-element").length >=
                2,
        );
        const els = Array.from(
            canvas.querySelectorAll(":scope > .bloom-canvas-element"),
        );
        return {
            count: els.length,
            texts: els.map((e) => (e.textContent || "").trim().slice(0, 25)),
            specs: els.map((e) => e.getAttribute("data-bubble")),
            focused: !!canvas.querySelector(
                ".bloom-focusedCanvasElement, .bloom-canvas-element.ui-selected",
            ),
        };
    });
const before = await snapshot();
console.log("before:", JSON.stringify(before));
console.log("canUndo before:", await h.canUndo());

// Select the second canvas element and delete it the way the toolbox / Delete key does.
await h.pf.evaluate(() => {
    const canvas = Array.from(document.querySelectorAll(".bloom-canvas")).find(
        (c) => c.querySelectorAll(":scope > .bloom-canvas-element").length >= 2,
    );
    const el = canvas.querySelectorAll(":scope > .bloom-canvas-element")[1];
    const manager = window.editablePageBundle.getTheOneCanvasElementManager();
    manager.setActiveElement(el);
    manager.deleteCurrentCanvasElement();
});
await h.p.waitForTimeout(800);
const afterDelete = await snapshot();
h.record(
    "E1 the element was deleted",
    afterDelete.count === before.count - 1,
    JSON.stringify(afterDelete.texts),
);
h.record(
    "E2 canUndo=yes after the deletion (our entry, no legacy mechanism)",
    (await h.canUndo()) === "yes",
    `button disabled=${await h.undoButtonDisabled()}`,
);

// Undo through the real button entry point.
await h.resetCalls();
await h.calls();
await h.pressUndoButton();
const c = await h.calls();
const afterUndo = await snapshot();
h.record(
    "E3 Undo button restored the element at its place (no legacy mechanism ran)",
    afterUndo.count === before.count &&
        JSON.stringify(afterUndo.texts) === JSON.stringify(before.texts) &&
        c.ck === 0 &&
        c.tb === 0 &&
        c.img === 0 &&
        c.ori === 0,
    `texts=${JSON.stringify(afterUndo.texts)} calls=${JSON.stringify(c)}`,
);
h.record(
    "E4 the family's bubble specs are exactly what they were",
    JSON.stringify(afterUndo.specs) === JSON.stringify(before.specs),
    `before=${JSON.stringify(before.specs)} after=${JSON.stringify(afterUndo.specs)}`,
);
h.record(
    "E5 the restored element is editable (a CKEditor is attached)",
    await h.pf.evaluate(() => {
        const canvas = Array.from(
            document.querySelectorAll(".bloom-canvas"),
        ).find(
            (c) =>
                c.querySelectorAll(":scope > .bloom-canvas-element").length >=
                2,
        );
        const el = canvas.querySelectorAll(":scope > .bloom-canvas-element")[1];
        const ed = el.querySelector(".bloom-editable.bloom-visibility-code-on");
        return !ed || !!ed.bloomCkEditor;
    }),
    "checked bloomCkEditor on the restored editable, if it has one",
);
console.log(
    "canRedo after undo:",
    await h.p.evaluate(() => window.workspaceBundle.canRedo()),
);

// Redo with Ctrl+Y in the page frame (focus in the page, not in a text box).
await h.pf
    .locator(".bloom-page")
    .click({ position: { x: 5, y: 5 }, force: true });
await h.resetCalls();
await h.p.keyboard.press("Control+y");
await h.p.waitForTimeout(800);
const c2 = await h.calls();
const afterRedo = await snapshot();
h.record(
    "E6 Ctrl+Y redid the deletion exactly once, through our binding",
    afterRedo.count === before.count - 1 && c2.redo === 1,
    `count=${afterRedo.count} calls=${JSON.stringify(c2)}`,
);
h.record(
    "E7 no second undo entry was recorded by the redo",
    (await h.canUndo()) === "yes" &&
        !(await h.p.evaluate(() => window.workspaceBundle.canRedo())),
    "canUndo yes, canRedo false",
);

// Put it back for the book's sake.
await h.pressUndoButton();
const final = await snapshot();
h.record(
    "E8 undo after redo restores it again",
    JSON.stringify(final.texts) === JSON.stringify(before.texts),
    JSON.stringify(final.texts),
);
h.summary();
await h.browser.close();
