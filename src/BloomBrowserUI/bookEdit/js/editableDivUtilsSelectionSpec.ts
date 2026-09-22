import { describe, it, expect, beforeEach, afterAll } from "vitest";
import { EditableDivUtils } from "./editableDivUtils";

// CHARACTERIZATION TESTS — see docs/retire-ckeditor/BEHAVIOR-INVENTORY.md rows G4/G5.
//
// These pin the behaviour of EditableDivUtils.makeSelectionIn / getElementSelectionIndex, which
// predate CKEditor and which the CKEditor-retirement project intends to build its bookmark-free
// selection anchors on (PLAN.md 4.3). They are pinned *before* that work starts because:
//
//  - makeSelectionIn's `divBrCount` parameter has no in-tree caller that passes anything but -1
//    (readerToolsModel.ts and toolbox.ts both pass -1), so the <br>-stepping behaviour the new
//    anchors will depend on is currently exercised by nothing at all; and
//  - getElementSelectionIndex only ever produces a plain character offset, so the round-trip these
//    tests establish is the contract the new capture side has to preserve.
//
// NOTE on the harness: both functions hard-code `parent.window.document.getElementById("page")` and
// operate on that iframe's window, because in the real app the edited page lives in an iframe with
// that id. So these tests must build such an iframe rather than using the ambient document. That
// hard-coding is itself worth recording: the replacement selection API should take a document/root
// instead, which is both testable and reusable outside the page frame.

let pageIframe: HTMLIFrameElement;

/** The document inside our stand-in for the app's page iframe. */
function pageDoc(): Document {
    return pageIframe.contentWindow!.document;
}

/**
 * Put the given HTML inside a .bloom-editable in the page iframe and return that div.
 * Returns the div from the *iframe's* document, so ranges built on it are valid there.
 */
function makeEditable(innerHtml: string): HTMLElement {
    const doc = pageDoc();
    doc.body.innerHTML = `<div class="bloom-editable" contenteditable="true">${innerHtml}</div>`;
    const div = doc.body.firstElementChild as HTMLElement;
    if (!div || !div.classList.contains("bloom-editable")) {
        throw new Error(
            "Test setup failed: could not create the .bloom-editable in the page iframe",
        );
    }
    return div;
}

/** Where the selection actually ended up, as (nodeText, offset), for asserting precisely. */
function currentSelection(): { text: string | null; offset: number } | null {
    const sel = pageIframe.contentWindow!.getSelection();
    if (!sel || !sel.anchorNode) return null;
    return { text: sel.anchorNode.textContent, offset: sel.anchorOffset };
}

describe("EditableDivUtils selection round-trip (characterization)", () => {
    beforeEach(() => {
        // A fresh iframe per test: a stale selection in a reused document is a classic source of
        // tests that pass only in a particular order.
        document.getElementById("page")?.remove();
        pageIframe = document.createElement("iframe");
        pageIframe.id = "page";
        document.body.appendChild(pageIframe);

        // Sanity-check the harness itself before any test relies on it. Without this, a jsdom
        // limitation would show up as a confusing assertion failure inside the function under test.
        if (!pageIframe.contentWindow) {
            throw new Error(
                "Test setup failed: the #page iframe has no contentWindow",
            );
        }
        if (typeof pageIframe.contentWindow.getSelection !== "function") {
            throw new Error(
                "Test setup failed: this DOM implementation has no getSelection() in the iframe",
            );
        }
    });

    afterAll(() => {
        document.getElementById("page")?.remove();
    });

    it("makeSelectionIn puts the caret at the requested character offset in a simple paragraph", () => {
        const div = makeEditable("<p>Hello world</p>");
        const para = div.firstElementChild as HTMLElement;
        expect(para.textContent).toBe("Hello world"); // sanity check the fixture

        const made = EditableDivUtils.makeSelectionIn(para, 5, -1, true);

        expect(made).toBe(true);
        const sel = currentSelection();
        expect(sel).not.toBeNull();
        // It should have drilled down to the text node, not stopped at the paragraph.
        expect(sel!.text).toBe("Hello world");
        expect(sel!.offset).toBe(5);
    });

    it("getElementSelectionIndex reads back the offset that makeSelectionIn set", () => {
        const div = makeEditable("<p>Hello world</p>");
        const para = div.firstElementChild as HTMLElement;

        // Sanity check: with no selection in the div yet, the index must not already be what we
        // are about to assert, or the test would pass without makeSelectionIn doing anything.
        expect(EditableDivUtils.getElementSelectionIndex(div)).not.toBe(7);

        EditableDivUtils.makeSelectionIn(para, 7, -1, true);

        expect(EditableDivUtils.getElementSelectionIndex(div)).toBe(7);
    });

    it("getElementSelectionIndex counts characters across inline markup, ignoring the tags", () => {
        // This is the property that makes an offset-based anchor better than a DOM bookmark:
        // the offset is over the *text*, so wrapping spans don't shift it.
        const div = makeEditable("<p>abc<strong>def</strong>ghi</p>");
        const para = div.firstElementChild as HTMLElement;
        expect(para.textContent).toBe("abcdefghi"); // sanity check

        EditableDivUtils.makeSelectionIn(para, 5, -1, true);

        // Offset 5 is inside the <strong>; the index is still counted over the whole text.
        expect(EditableDivUtils.getElementSelectionIndex(div)).toBe(5);
    });

    it("makeSelectionIn returns false when there is no text node to land in", () => {
        const div = makeEditable("<p></p>");
        const para = div.firstElementChild as HTMLElement;
        expect(para.textContent).toBe(""); // sanity check

        expect(EditableDivUtils.makeSelectionIn(para, 0, -1, true)).toBe(false);
    });

    describe("divBrCount — the <br>-stepping nothing in the app currently exercises", () => {
        it("with divBrCount 0, the caret lands in the container before the following node", () => {
            // Note this is the documented meaning: when offset equals the length of a child, and
            // divBrCount >= 0, the caret goes in `node` itself at a child index rather than
            // drilling into a text node.
            const div = makeEditable("<p>ab<br>cd</p>");
            const para = div.firstElementChild as HTMLElement;
            expect(para.childNodes.length).toBe(3); // text, br, text — sanity check

            const made = EditableDivUtils.makeSelectionIn(para, 2, 0, true);

            expect(made).toBe(true);
            const sel = currentSelection();
            expect(sel).not.toBeNull();
            // The anchor is the paragraph itself, positioned by child index.
            expect(sel!.text).toBe("abcd");
            expect(sel!.offset).toBe(1);
        });

        it("with divBrCount 1, the caret steps past one <br>", () => {
            const div = makeEditable("<p>ab<br>cd</p>");
            const para = div.firstElementChild as HTMLElement;

            const made = EditableDivUtils.makeSelectionIn(para, 2, 1, true);

            expect(made).toBe(true);
            const sel = currentSelection();
            expect(sel).not.toBeNull();
            expect(sel!.text).toBe("abcd");
            // Stepped over the <br>, so one child index further along than the divBrCount 0 case.
            expect(sel!.offset).toBe(2);
        });

        it("with divBrCount 2 but only one <br> present, it stops at the <br> it has", () => {
            // Pins the "ask for more <br>s than exist" case, which a restored anchor can hit if the
            // DOM changed between capture and restore.
            const div = makeEditable("<p>ab<br>cd</p>");
            const para = div.firstElementChild as HTMLElement;

            const made = EditableDivUtils.makeSelectionIn(para, 2, 2, true);

            expect(made).toBe(true);
            const sel = currentSelection();
            expect(sel).not.toBeNull();
            expect(sel!.offset).toBe(2);
        });

        it("divBrCount is ignored when the offset is not at a child boundary", () => {
            const div = makeEditable("<p>ab<br>cd</p>");
            const para = div.firstElementChild as HTMLElement;

            EditableDivUtils.makeSelectionIn(para, 1, 1, true);

            const sel = currentSelection();
            expect(sel).not.toBeNull();
            // Landed inside the first text node, so it drilled down and ignored the <br> request.
            expect(sel!.text).toBe("ab");
            expect(sel!.offset).toBe(1);
        });
    });

    describe("atStart — which side of a node boundary the caret prefers", () => {
        it("atStart true puts the caret at the start of the following node", () => {
            const div = makeEditable("<p><em>ab</em><strong>cd</strong></p>");
            const para = div.firstElementChild as HTMLElement;
            expect(para.textContent).toBe("abcd"); // sanity check

            EditableDivUtils.makeSelectionIn(para, 2, -1, true);

            const sel = currentSelection();
            expect(sel).not.toBeNull();
            // Offset 2 is the boundary; atStart true prefers the *following* node's text.
            expect(sel!.text).toBe("cd");
            expect(sel!.offset).toBe(0);
        });

        it("atStart false puts the caret at the end of the preceding node", () => {
            const div = makeEditable("<p><em>ab</em><strong>cd</strong></p>");
            const para = div.firstElementChild as HTMLElement;

            EditableDivUtils.makeSelectionIn(para, 2, -1, false);

            const sel = currentSelection();
            expect(sel).not.toBeNull();
            expect(sel!.text).toBe("ab");
            expect(sel!.offset).toBe(2);
        });
    });
});
