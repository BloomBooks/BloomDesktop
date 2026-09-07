// Stage 0 repro for PLAN.md 4.10 / inventory X4: do edit key handlers accumulate when
// SetupElements runs again on a page that already has them?
//
// SetupElements(container) is already called re-entrantly on subtrees (CanvasElementManager's
// refreshCanvasElementEditing, imageDescription.tsx), and it calls AddEditKeyHandlers(container),
// which attaches per-editable jQuery keydown handlers AND two handlers on `document` (Ctrl+Space
// clear-formatting, Ctrl+R/L/E justify). jQuery multiplexes many handlers behind one native
// listener, so counting native listeners over CDP shows nothing; instead this counts what the
// handlers DO: it stubs document.execCommand in the page frame and dispatches the keystrokes.
//
// Needs Bloom on the Edit tab of any book with a content text box. Changes nothing in the book
// (execCommand is stubbed for the duration and restored afterwards).
import { setup, editableSel } from "./verifyCommon.mjs";
const h = await setup();
await h.gotoContentPage();

const countCommands = async (label) =>
    h.pf.evaluate(
        ({ s, idx, label }) => {
            const ed = document.querySelectorAll(s)[idx];
            const real = document.execCommand;
            const calls = [];
            document.execCommand = (cmd, ui, value) => {
                calls.push(cmd);
                return true;
            };
            try {
                // Ctrl+R -> justifyright, from the handler on `document`.
                ed.dispatchEvent(
                    new KeyboardEvent("keydown", {
                        key: "r",
                        ctrlKey: true,
                        bubbles: true,
                        cancelable: true,
                    }),
                );
                const justify = calls.filter(
                    (c) => c === "justifyright",
                ).length;
                calls.length = 0;
                // F7 -> formatBlock H1, from the per-editable handler.
                ed.dispatchEvent(
                    new KeyboardEvent("keydown", {
                        key: "F7",
                        bubbles: true,
                        cancelable: true,
                    }),
                );
                const formatBlock = calls.filter(
                    (c) => c === "formatBlock",
                ).length;
                return {
                    label,
                    justifyRightHandlersFired: justify,
                    f7HandlersFired: formatBlock,
                };
            } finally {
                document.execCommand = real;
            }
        },
        { s: editableSel, idx: h.idx, label },
    );

const before = await countCommands("as loaded");
console.log(JSON.stringify(before));
// Re-run SetupElements on the page, as the canvas-element refresh paths do on a subtree.
await h.pf.evaluate(() => {
    window.editablePageBundle.SetupElements(
        document.querySelector(".bloom-page"),
    );
});
await h.p.waitForTimeout(500);
const after1 = await countCommands("after 1 extra SetupElements");
console.log(JSON.stringify(after1));
await h.pf.evaluate(() => {
    window.editablePageBundle.SetupElements(
        document.querySelector(".bloom-page"),
    );
});
await h.p.waitForTimeout(500);
const after2 = await countCommands("after 2 extra SetupElements");
console.log(JSON.stringify(after2));

h.record(
    "X4 as loaded, each keystroke runs its handler exactly once",
    before.justifyRightHandlersFired === 1 && before.f7HandlersFired === 1,
    JSON.stringify(before),
);
h.record(
    "X4 handlers do NOT accumulate when SetupElements runs again (known to fail: PLAN.md 4.10)",
    after2.justifyRightHandlersFired === 1 && after2.f7HandlersFired === 1,
    `document-level Ctrl+R handlers: ${before.justifyRightHandlersFired} -> ${after1.justifyRightHandlersFired} -> ${after2.justifyRightHandlersFired}; per-editable F7 handlers: ${before.f7HandlersFired} -> ${after1.f7HandlersFired} -> ${after2.f7HandlersFired}`,
);
h.summary();
await h.browser.close();
