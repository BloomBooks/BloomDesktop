// Stage 0 capture for inventory rows C1-C7 (BL-6681, PLAN.md 4.8): what does TODAY's paste/drop
// filtering actually let through? Writes docs/retire-ckeditor/PASTE-DROP-BASELINE.md.
//
// Why synthetic events: the point is the filter, not the OS clipboard. A `paste` ClipboardEvent
// carrying a DataTransfer with text/html goes through exactly the code a real paste does -
// CKEditor's clipboard plugin reads evt.data.$.clipboardData, applies pasteFilter, fires its own
// `paste` event that BloomField's transforms hook - and a `drop` DragEvent carrying the same
// DataTransfer goes through the plugin's drop handler (C7). What it does NOT cover is anything that
// reads the real clipboard from C# (the Paste button), which is a separate entry point.
//
// Needs Bloom on the Edit tab of a book with a content text box (a Basic Book). Restores the box.
import { writeFileSync } from "node:fs";
import path from "node:path";
import { setup, editableSel } from "./verifyCommon.mjs";
import { repoRoot } from "./cdp.mjs";

const rows = [
    {
        id: "C1",
        name: "a table",
        html: `<table border="1" style="border-collapse:collapse"><thead><tr><th>Name</th><th>Age</th></tr></thead><tbody><tr><td>Ann</td><td>3</td></tr><tr><td>Bob</td><td>5</td></tr></tbody></table>`,
        text: "Name\tAge\nAnn\t3\nBob\t5",
    },
    {
        id: "C2",
        name: "nested divs with an id (as copied from another Bloom book)",
        html: `<div id="i7a3b2c1" class="bloom-editable bloom-content1" lang="en" style="color:red"><div class="inner"><p>Nested <b>bold</b> text</p><p>Second para</p></div></div>`,
        text: "Nested bold text\nSecond para",
    },
    {
        id: "C3",
        name: "iframe, script, style, object, embed",
        html: `<p>Before</p><iframe src="https://example.com/"></iframe><script>window.__pwned=1</script><style>p{color:red}</style><object data="movie.swf"></object><embed src="movie.mp4"><p>After</p>`,
        text: "Before\nAfter",
    },
    {
        id: "C4",
        name: "an inline image",
        html: `<p>Picture: <img src="https://example.com/a.png" alt="alt text" width="40" height="40"> end</p>`,
        text: "Picture:  end",
    },
    {
        id: "C5",
        name: "styled span soup from a web page",
        html: `<p><span style="font-family:Arial,sans-serif;font-size:14pt;color:#ff0000;font-variant:small-caps;background:yellow;font-weight:bold;letter-spacing:2px">Soup</span> <span class="x" data-foo="1" title="t">plain span</span> <span style="text-decoration:underline">underlined</span></p>`,
        text: "Soup plain span underlined",
    },
    {
        id: "C6",
        name: "a link with extra attributes",
        html: `<p>See <a href="https://example.com/page" target="_blank" rel="noopener" class="lnk" id="l1" title="t" style="color:blue" onclick="alert(1)">this link</a>.</p>`,
        text: "See this link.",
    },
    {
        id: "C-mixed",
        name: "a realistic web-page fragment (heading, list, bold/italic, sup)",
        html: `<h2 class="title">Heading</h2><ul><li>One <em>two</em></li><li><strong>Three</strong></li></ul><p>H<sub>2</sub>O and E=mc<sup>2</sup>, <u>under</u>, <s>struck</s>, <code>code</code>.</p>`,
        text: "Heading\nOne two\nThree\nH2O and E=mc2, under, struck, code.",
    },
];

const h = await setup();
await h.gotoContentPage();
const originalHtml = await h.readHtml();

async function applyEvent(kind, row) {
    await h.writeHtml("<p>Start end</p>");
    await h.p.waitForTimeout(200);
    await h.pf.locator(editableSel).nth(h.idx).click();
    await h.p.waitForTimeout(300);
    // Put the caret after "Start " so the pasted content lands mid-paragraph.
    await h.pf.evaluate(
        ({ s, idx }) => {
            const ed = document.querySelectorAll(s)[idx];
            const t = ed.querySelector("p").firstChild;
            const r = document.createRange();
            r.setStart(t, 6);
            r.collapse(true);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(r);
        },
        { s: editableSel, idx: h.idx },
    );
    const dispatched = await h.pf.evaluate(
        ({ s, idx, kind, html, text }) => {
            const ed = document.querySelectorAll(s)[idx];
            const dt = new DataTransfer();
            dt.setData("text/html", html);
            dt.setData("text/plain", text);
            let evt;
            if (kind === "paste") {
                evt = new ClipboardEvent("paste", {
                    clipboardData: dt,
                    bubbles: true,
                    cancelable: true,
                });
            } else {
                const rect = ed.getBoundingClientRect();
                evt = new DragEvent("drop", {
                    dataTransfer: dt,
                    bubbles: true,
                    cancelable: true,
                    clientX: rect.left + 20,
                    clientY: rect.top + 10,
                });
            }
            const target = kind === "paste" ? document.activeElement || ed : ed;
            const notCancelled = target.dispatchEvent(evt);
            return {
                targetIsEditable: ed.contains(target) || target === ed,
                defaultPrevented: evt.defaultPrevented,
                notCancelled,
            };
        },
        { s: editableSel, idx: h.idx, kind, html: row.html, text: row.text },
    );
    await h.p.waitForTimeout(1200);
    const result = await h.readHtml();
    return { dispatched, result };
}

const out = [];
out.push("# Paste / drop baseline — what today's filter lets through\n");
out.push(
    `Captured ${new Date().toISOString().slice(0, 10)} by \`liveChecks/pasteDropBaseline.mjs\` against Bloom on branch \`BL-6681-stage1-undostack\` (CKEditor 4 with Bloom's \`config.js\` pasteFilter). This is the behaviour the replacement sanitizer (PLAN.md 4.8) must reproduce; rows are inventory C1–C7.\n`,
);
out.push(
    'Each row: the box started as `<p>Start end</p>` with the caret after "Start "; the event carried both `text/html` and `text/plain`.\n',
);
for (const row of rows) {
    out.push(`## ${row.id} — ${row.name}\n`);
    out.push("**Input HTML**\n\n```html\n" + row.html + "\n```\n");
    for (const kind of ["paste", "drop"]) {
        const { dispatched, result } = await applyEvent(kind, row);
        console.log(
            row.id,
            kind,
            JSON.stringify(dispatched),
            "=>",
            result.slice(0, 160),
        );
        const unchanged = result === "<p>Start end</p>";
        out.push(
            `**After ${kind}** (event ${dispatched.defaultPrevented ? "was handled (defaultPrevented)" : "was NOT defaultPrevented"}${unchanged ? "; box unchanged — nothing was inserted" : ""})\n\n\`\`\`html\n${result}\n\`\`\`\n`,
        );
    }
}
await h.writeHtml(originalHtml);
const outPath = path.join(
    repoRoot,
    "docs/retire-ckeditor/PASTE-DROP-BASELINE.md",
);
writeFileSync(outPath, out.join("\n"), "utf8");
console.log("wrote", outPath);
await h.browser.close();
