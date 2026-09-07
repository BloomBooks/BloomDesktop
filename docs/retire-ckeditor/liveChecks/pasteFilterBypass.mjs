// Repro for the paste-filter bypass found while capturing the C1-C7 baseline (BL-6681, 2026-09-07).
//
// Bloom's pasteFilter (config.js) reduces external HTML to a small vocabulary. But
// BloomField.restoreHtmlMarkupIfNecessary (BL-12357) decides "this paste came from inside CKEditor,
// so put the spans back" by testing dataTransfer.getData("cke/id") -- and CKEditor's dataTransfer
// wrapper assigns an id to EVERY transfer, external ones included (the ids logged here start
// "cke-", CKEditor's generated form). So whenever the pasted HTML contains "<span style=", the
// handler replaces the FILTERED html with the FULL clipboard html, and tables, iframes, images and
// divs with ids all reach the book. The control payload (no styled span) is filtered correctly.
//
// Logs the pasted HTML after CKEditor's filter (priority 9) and after Bloom's handler (priority 11),
// so the point of failure is visible. Needs Bloom on the Edit tab of a book with a content text box.
// Restores the box. Exits non-zero while the bug is present.
import { setup, editableSel } from "./verifyCommon.mjs";
const h = await setup();
await h.gotoContentPage();
const orig = await h.readHtml();

const run = async (label, html) => {
    await h.writeHtml("<p>Start end</p>");
    await h.pf.locator(editableSel).nth(h.idx).click();
    await h.p.waitForTimeout(300);
    await h.pf.evaluate(
        ({ s, idx, html }) => {
            const ed = document.querySelectorAll(s)[idx];
            const editor = ed.bloomCkEditor;
            window.__log = [];
            if (!editor.__diag) {
                editor.__diag = true;
                editor.on(
                    "paste",
                    (e) =>
                        window.__log.push({
                            at: "after CKEditor's pasteFilter (priority 9)",
                            ckeId: e.data.dataTransfer.getData("cke/id"),
                            transferType:
                                e.data.dataTransfer.getTransferType(editor),
                            dataValue: String(e.data.dataValue),
                        }),
                    null,
                    null,
                    9,
                );
                editor.on(
                    "paste",
                    (e) =>
                        window.__log.push({
                            at: "after Bloom's paste handler (priority 11)",
                            dataValue: String(e.data.dataValue),
                        }),
                    null,
                    null,
                    11,
                );
            }
            const dt = new DataTransfer();
            dt.setData("text/html", html);
            dt.setData("text/plain", "x");
            ed.dispatchEvent(
                new ClipboardEvent("paste", {
                    clipboardData: dt,
                    bubbles: true,
                    cancelable: true,
                }),
            );
        },
        { s: editableSel, idx: h.idx, html },
    );
    await h.p.waitForTimeout(1200);
    console.log("=== " + label);
    for (const l of await h.pf.evaluate(() => window.__log))
        console.log("  ", JSON.stringify(l));
    const final = await h.readHtml();
    console.log("   final:", JSON.stringify(final));
    return final;
};

const hostile = `<table border="1"><tr><td>cell <span style="color:red">red</span></td></tr></table><iframe src="https://example.com/"></iframe><img src="https://example.com/a.png"><div id="dup-id"><p>div text</p></div>`;
const withSpan = await run(
    "table + iframe + img + div#id, WITH a styled span",
    hostile,
);
const control = await run(
    "the same, WITHOUT the styled span",
    hostile.replace(`<span style="color:red">red</span>`, "red"),
);
h.record(
    "control: without a styled span the filter holds (no table/iframe/img/div)",
    !/<(table|iframe|img|div)/.test(control),
    control,
);
h.record(
    "with a styled span the filter still holds (FAILS today: BL-12357's cke/id test admits everything)",
    !/<(table|iframe|img|div)/.test(withSpan),
    withSpan.slice(0, 200),
);
await h.writeHtml(orig);
h.summary();
await h.browser.close();
