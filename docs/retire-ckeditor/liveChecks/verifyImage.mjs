// Live check D (BL-6681 Stage 1): an undoable image operation -- changeImageByElement with
// undoable="true", the same entry point the copyright dialog's completion uses -- is undone by the
// Undo button through the stack's image provider.
//
// Needs Bloom on the Edit tab; finds the first page with an image. Restores the copyright.
import { setup } from "./verifyCommon.mjs";
const h = await setup();
const thumbs = h.pageListFrame().locator(".gridItem");
const n = await thumbs.count();
let found = false;
for (let t = 1; t < n && !found; t++) {
    await thumbs.nth(t).click();
    await h.p.waitForTimeout(3000);
    h.pf = h.pageFrame();
    found = await h.pf.evaluate(
        () =>
            !!document.querySelector(
                ".bloom-canvas img, .bloom-imageContainer img",
            ),
    );
    console.log(`thumb ${t}: image? ${found}`);
}
if (!found) throw new Error("no page with an image");
h.idx = 0;
await h.instrument();
const sel = ".bloom-canvas img, .bloom-imageContainer img";
const readCopyright = () =>
    h.pf.evaluate((s) => {
        const img = document.querySelector(s);
        const c = img.closest(".bloom-canvas, .bloom-imageContainer");
        return {
            imgCopyright: img.getAttribute("data-copyright"),
            containerCopyright: c.getAttribute("data-copyright"),
        };
    }, sel);
const before = await readCopyright();
console.log("before:", JSON.stringify(before), "canUndo:", await h.canUndo());
// The image undo is gated on the active canvas element containing an image, so make it active
// first. The Comical canvas sits over the image and intercepts pointer events exactly as it does
// for a real user's click; a forced click lands at the image's position the same way.
await h.pf.locator(sel).first().click({ force: true });
await h.p.waitForTimeout(700);
await h.pf.evaluate((s) => {
    const img = document.querySelector(s);
    const target = img.closest(".bloom-canvas")
        ? img
        : img.closest(".bloom-imageContainer");
    window.editablePageBundle.changeImageByElement(target, {
        src: img.getAttribute("src"),
        copyright: "Copyright © 2026, Stage 1 verification",
        creator: "verifyImage.mjs",
        license: "cc-by",
        undoable: "true",
    });
}, sel);
await h.p.waitForTimeout(800);
const changed = await readCopyright();
h.record(
    "D1 the copyright change applied",
    (changed.imgCopyright || changed.containerCopyright || "").includes(
        "Stage 1 verification",
    ),
    JSON.stringify(changed),
);
h.record(
    "D2 canUndo=yes via the image undo",
    (await h.canUndo()) === "yes",
    `button disabled=${await h.undoButtonDisabled()}`,
);
await h.resetCalls();
await h.calls();
await h.pressUndoButton();
const c = await h.calls();
const after = await readCopyright();
h.record(
    "D3 Undo button -> imageOperationUndo through the stack (img=1, others 0)",
    c.img === 1 && c.ck === 0 && c.tb === 0 && c.ori === 0,
    JSON.stringify(c),
);
h.record(
    "D4 the copyright was restored",
    after.imgCopyright === before.imgCopyright &&
        after.containerCopyright === before.containerCopyright,
    `after=${JSON.stringify(after)}`,
);
console.log("canUndo after:", await h.canUndo());
h.summary();
await h.browser.close();
