// Shared pieces of the undo live checks (BL-6681). See README.md in this directory.
//
// The one idea worth knowing: every check ATTRIBUTES each gesture to the mechanism(s) that actually
// ran, by wrapping the cross-frame entry points the workspace bundle reaches the mechanisms through
// (page bundle: ckeditorUndo / imageOperationUndo / origamiUndo; toolbox bundle: undo /
// updateMarkupAfterUndoOrRedo; workspace bundle: handleRedo) and by listening to CKEditor's own
// afterCommandExec. "The text changed back" is not evidence of WHICH undo ran; the counters are.
import { connect, mainPage } from "./cdp.mjs";

export const editableSel =
    ".bloom-editable.bloom-visibility-code-on[contenteditable='true']";

export async function setup() {
    const { browser, pages } = await connect();
    const p = mainPage(pages);
    const results = [];
    const record = (name, pass, detail) => {
        results.push({ name, pass, detail });
        console.log(`${pass ? "PASS" : "FAIL"}: ${name} — ${detail}`);
    };
    const h = {
        browser,
        p,
        results,
        record,
        pf: undefined,
        idx: -1,
        pageFrame: () => p.frames().find((f) => f.name() === "page"),
        toolboxFrame: () => p.frames().find((f) => f.name() === "toolbox"),
        pageListFrame: () => p.frames().find((f) => f.name() === "pageList"),
    };
    h.instrument = async () => {
        await p.evaluate(() => {
            const wb = window.workspaceBundle;
            const px = wb.getEditablePageBundleExports();
            const tx = wb.getToolboxBundleExports();
            window.__calls = {
                ck: 0,
                tb: 0,
                img: 0,
                ori: 0,
                markup: 0,
                redo: 0,
            };
            const wrap = (obj, name, key) => {
                const o = obj[name];
                if (o.__wrapped) return;
                const w = function (...a) {
                    window.__calls[key]++;
                    return o.apply(this, a);
                };
                w.__wrapped = true;
                obj[name] = w;
            };
            wrap(px, "ckeditorUndo", "ck");
            wrap(px, "imageOperationUndo", "img");
            wrap(px, "origamiUndo", "ori");
            wrap(tx, "undo", "tb");
            wrap(tx, "updateMarkupAfterUndoOrRedo", "markup");
            wrap(wb, "handleRedo", "redo");
        });
        await h.pf.evaluate(() => {
            window.__ckCmds = [];
            if (typeof CKEDITOR === "undefined") return;
            for (const k in CKEDITOR.instances) {
                const ed = CKEDITOR.instances[k];
                if (ed.__instrumented) continue;
                ed.__instrumented = true;
                ed.on("afterCommandExec", (e) =>
                    window.__ckCmds.push(e.data.name),
                );
            }
        });
    };
    h.calls = async () => {
        const c = await p.evaluate(() => ({ ...window.__calls }));
        c.ckCmds = await h.pf.evaluate(() => (window.__ckCmds || []).splice(0));
        return c;
    };
    h.resetCalls = () =>
        p.evaluate(() => {
            for (const k in window.__calls) window.__calls[k] = 0;
        });
    h.canUndo = () => p.evaluate(() => window.workspaceBundle.canUndo());
    // The top bar's Undo button; C# enables it from canUndo() on a timer.
    h.undoButtonDisabled = () =>
        p.evaluate(() => {
            const b = Array.from(document.querySelectorAll("button")).find(
                (e) =>
                    /undo/i.test(e.innerHTML) ||
                    /undo/i.test(e.getAttribute("aria-label") || ""),
            );
            return b ? b.disabled : "not found";
        });
    h.readText = () =>
        h.pf.evaluate(
            ({ s, idx }) => document.querySelectorAll(s)[idx].textContent,
            { s: editableSel, idx: h.idx },
        );
    h.readHtml = () =>
        h.pf.evaluate(
            ({ s, idx }) => document.querySelectorAll(s)[idx].innerHTML,
            { s: editableSel, idx: h.idx },
        );
    h.writeHtml = (html) =>
        h.pf.evaluate(
            ({ s, idx, html }) => {
                document.querySelectorAll(s)[idx].innerHTML = html;
            },
            { s: editableSel, idx: h.idx, html },
        );
    h.clickEnd = async () => {
        await h.pf.locator(editableSel).nth(h.idx).click();
        await p.waitForTimeout(300);
        await p.keyboard.press("End");
    };
    // Which tool the toolbox considers current. Ask the ToolBox itself: the jQuery-UI accordion's
    // header classes lag behind (and once mislabelled the Canvas tool as active while the Talking
    // Book panel was plainly open).
    h.activeTool = () =>
        h.toolboxFrame()?.evaluate(() => {
            const tool = window.toolboxBundle
                .getTheOneToolbox()
                .getCurrentTool();
            return tool ? tool.id() : undefined;
        });
    // What the top bar's Undo button does, minus the click itself: C# runs
    // getEditablePageBundleExports().topBarButtonClick({command:"undo"}) -- in the PAGE frame --
    // which must reach the workspace frame's one stack. Going through the page frame here, rather
    // than calling workspaceBundle.handleUndo() directly, is what caught the two-stacks bug.
    h.pressUndoButton = async () => {
        await p.evaluate(() =>
            window.workspaceBundle
                .getEditablePageBundleExports()
                .topBarButtonClick({ command: "undo" }),
        );
        await p.waitForTimeout(800);
    };
    // Find a content page (not xmatter) with a CKEditor'd content editable, starting at thumb 1.
    h.gotoContentPage = async () => {
        const thumbs = h.pageListFrame().locator(".gridItem");
        const n = await thumbs.count();
        for (let t = 1; t < n; t++) {
            await thumbs.nth(t).click();
            await p.waitForTimeout(3000);
            h.pf = h.pageFrame();
            const info = await h.pf.evaluate((s) => {
                const page = document.querySelector(".bloom-page");
                return {
                    xmatter: /bloom-frontMatter|bloom-backMatter/.test(
                        page?.className || "",
                    ),
                    eds: Array.from(document.querySelectorAll(s)).map((e) => ({
                        ck: !!e.bloomCkEditor,
                        c1: e.classList.contains("bloom-content1"),
                        text: e.textContent.trim().slice(0, 30),
                    })),
                };
            }, editableSel);
            if (!info.xmatter) {
                h.idx = info.eds.findIndex((e) => e.ck && e.c1);
                if (h.idx >= 0) {
                    console.log(
                        `using thumb ${t}, editable ${h.idx}:`,
                        JSON.stringify(info.eds[h.idx]),
                    );
                    return;
                }
            }
        }
        throw new Error("no content page with a CKEditor'd content editable");
    };
    h.summary = () => {
        const failed = results.filter((r) => !r.pass);
        console.log(
            "\nSUMMARY:",
            results.length - failed.length,
            "passed,",
            failed.length,
            "failed",
        );
        for (const r of failed) console.log("  FAILED:", r.name, "—", r.detail);
        process.exitCode = failed.length ? 1 : 0;
    };
    return h;
}
