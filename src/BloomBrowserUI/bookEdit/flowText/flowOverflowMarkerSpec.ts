import { beforeEach, describe, expect, it } from "vitest";
import { LineMeasurer } from "./flowFit";
import {
    getOverflowMarkerOffset,
    hasOverflowMarker,
    MarkerFitProbe,
    placeOverflowMarker,
    removeOverflowMarker,
    setFitRangeEnd,
} from "./flowOverflowMarker";
import { linearizeEditable } from "./flowLinearize";

const kMarkerSelector = "span.bloom-overflowStart";
const kZeroWidthNonJoiner = String.fromCharCode(0x200c);

function makeEditable(innerHtml: string): HTMLElement {
    const editable = document.createElement("div");
    editable.className = "bloom-editable normal-style";
    editable.setAttribute("contenteditable", "true");
    editable.innerHTML = innerHtml;
    document.body.appendChild(editable);
    return editable;
}

/** A measurer that always proposes the same offset, whatever the text says. */
function measurerAt(offset: number): LineMeasurer {
    return { measureFit: () => offset };
}

/** Nothing beyond fitLength fits. This is the layout the tests pretend to have. */
function probeUpTo(fitLength: number): MarkerFitProbe {
    return (_editable, offset) => offset <= fitLength;
}

const alwaysOverflows = () => 1;
const neverOverflows = () => 0;

describe("flowOverflowMarker", () => {
    beforeEach(() => {
        document.body.replaceChildren();
    });

    it("puts the marker at the offset the measurer proposes", () => {
        const editable = makeEditable("<p>One two three four five</p>");

        const offset = placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        expect(offset).toBe(8);
        expect(getOverflowMarkerOffset(editable)).toBe(8);
        expect(editable.innerHTML).toBe(
            '<p>One two <span class="bloom-overflowStart">' +
                kZeroWidthNonJoiner +
                "</span>three four five</p>",
        );
    });

    it("carries a zero-width character so nothing prunes the empty span", () => {
        const editable = makeEditable("<p>One two three four five</p>");

        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        const marker = editable.querySelector(kMarkerSelector);
        expect(marker?.textContent).toBe(kZeroWidthNonJoiner);
    });

    it("contributes no characters to the linearized text", () => {
        const withoutMarker = makeEditable("<p>One two three four five</p>");
        const before = linearizeEditable(withoutMarker).text;

        placeOverflowMarker(
            withoutMarker,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        expect(linearizeEditable(withoutMarker).text).toBe(before);
    });

    it("backs the offset off a word at a time when the real layout says it does not fit", () => {
        const editable = makeEditable("<p>One two three four five</p>");

        // The measurer says 18 characters fit; the layout only has room for 13.
        const offset = placeOverflowMarker(
            editable,
            measurerAt(18),
            alwaysOverflows,
            probeUpTo(13),
        );

        expect(offset).toBe(8);
        expect(editable.textContent).toContain("One two ");
    });

    it("lets another word in when the measurer was too cautious", () => {
        const editable = makeEditable("<p>One two three four five</p>");

        const offset = placeOverflowMarker(
            editable,
            measurerAt(4),
            alwaysOverflows,
            probeUpTo(17),
        );

        expect(offset).toBe(14);
    });

    it("finds the fit when the prediction is many words short of it", () => {
        const editable = makeEditable(
            "<p>" + "word ".repeat(40).trim() + "</p>",
        );

        // "word " five times over is 25 characters, so 30 words fit and the prediction
        // names the start of the ninth word.
        const offset = placeOverflowMarker(
            editable,
            measurerAt(40),
            alwaysOverflows,
            probeUpTo(150),
        );

        expect(offset).toBe(150);
    });

    it("finds the fit when the prediction is many words past it", () => {
        const editable = makeEditable(
            "<p>" + "word ".repeat(40).trim() + "</p>",
        );

        const offset = placeOverflowMarker(
            editable,
            measurerAt(180),
            alwaysOverflows,
            probeUpTo(52),
        );

        expect(offset).toBe(50);
    });

    it("moves the marker when the text changes", () => {
        const editable = makeEditable("<p>One two three four five</p>");
        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        placeOverflowMarker(
            editable,
            measurerAt(14),
            alwaysOverflows,
            probeUpTo(14),
        );

        expect(getOverflowMarkerOffset(editable)).toBe(14);
        expect(editable.querySelectorAll(kMarkerSelector)).toHaveLength(1);
    });

    it("leaves the marker alone when the offset has not moved", () => {
        const editable = makeEditable("<p>One two three four five</p>");
        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );
        const firstMarker = editable.querySelector(kMarkerSelector);

        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        expect(editable.querySelector(kMarkerSelector)).toBe(firstMarker);
    });

    it("never leaves two markers behind", () => {
        const editable = makeEditable("<p>One two three four five</p>");

        placeOverflowMarker(
            editable,
            measurerAt(4),
            alwaysOverflows,
            probeUpTo(4),
        );
        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );
        placeOverflowMarker(
            editable,
            measurerAt(14),
            alwaysOverflows,
            probeUpTo(14),
        );

        expect(editable.querySelectorAll(kMarkerSelector)).toHaveLength(1);
    });

    it("takes the marker out when the box fits again", () => {
        const editable = makeEditable("<p>One two three four five</p>");
        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        const offset = placeOverflowMarker(
            editable,
            measurerAt(8),
            neverOverflows,
            probeUpTo(500),
        );

        expect(offset).toBeUndefined();
        expect(hasOverflowMarker(editable)).toBe(false);
        expect(editable.innerHTML).toBe("<p>One two three four five</p>");
    });

    it("marks nothing when the whole text fits", () => {
        const editable = makeEditable("<p>One two three</p>");

        const offset = placeOverflowMarker(
            editable,
            measurerAt(500),
            undefined,
            probeUpTo(500),
        );

        expect(offset).toBeUndefined();
        expect(hasOverflowMarker(editable)).toBe(false);
    });

    it("marks nothing when not even the first word fits", () => {
        const editable = makeEditable("<p>One two three</p>");

        const offset = placeOverflowMarker(
            editable,
            measurerAt(0),
            alwaysOverflows,
            probeUpTo(0),
        );

        expect(offset).toBeUndefined();
        expect(hasOverflowMarker(editable)).toBe(false);
    });

    it("never puts the marker inside a soft line break span", () => {
        const editable = makeEditable(
            '<p>One<span class="bloom-linebreak"></span>two three</p>',
        );

        // Offset 4 is the character after the line break, whose DOM position the linearizer
        // records inside the span's parent; a marker must land after the span, never in it.
        placeOverflowMarker(
            editable,
            measurerAt(4),
            alwaysOverflows,
            probeUpTo(4),
        );

        expect(
            editable.querySelector(
                "span.bloom-linebreak span.bloom-overflowStart",
            ),
        ).toBeNull();
        expect(editable.querySelectorAll(kMarkerSelector)).toHaveLength(1);
    });

    it("never puts the marker inside a CKEditor bookmark span", () => {
        const editable = makeEditable(
            '<p>One <span id="cke_bm_1S" style="display:none"> </span>two three</p>',
        );

        placeOverflowMarker(
            editable,
            measurerAt(4),
            alwaysOverflows,
            probeUpTo(4),
        );

        expect(
            editable.querySelector(
                "span[id^='cke_bm_'] span.bloom-overflowStart",
            ),
        ).toBeNull();
        expect(editable.querySelectorAll(kMarkerSelector)).toHaveLength(1);
    });

    it("puts the caret back where it was after inserting the marker", () => {
        const editable = makeEditable("<p>One two three four five</p>");
        const textNode = editable.querySelector("p")?.firstChild as Text;
        const selection = window.getSelection();
        const range = document.createRange();
        range.setStart(textNode, textNode.data.length);
        range.collapse(true);
        selection?.removeAllRanges();
        selection?.addRange(range);

        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        const after = window.getSelection();
        const probe = document.createRange();
        probe.setStart(editable, 0);
        probe.setEnd(after!.anchorNode!, after!.anchorOffset);
        expect(probe.toString().replace(kZeroWidthNonJoiner, "")).toBe(
            "One two three four five",
        );
    });

    it("puts the caret back where it was after removing the marker", () => {
        const editable = makeEditable("<p>One two three four five</p>");
        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );
        const lastTextNode = editable.querySelector("p")?.lastChild as Text;
        const selection = window.getSelection();
        const range = document.createRange();
        range.setStart(lastTextNode, lastTextNode.data.length);
        range.collapse(true);
        selection?.removeAllRanges();
        selection?.addRange(range);

        removeOverflowMarker(editable);

        const after = window.getSelection();
        const probe = document.createRange();
        probe.setStart(editable, 0);
        probe.setEnd(after!.anchorNode!, after!.anchorOffset);
        expect(probe.toString()).toBe("One two three four five");
    });

    it("splits only the text node it lands in and rewrites no other markup", () => {
        const editable = makeEditable(
            "<p>One <strong>two</strong> three four</p>",
        );

        placeOverflowMarker(
            editable,
            measurerAt(8),
            alwaysOverflows,
            probeUpTo(8),
        );

        expect(editable.innerHTML).toBe(
            "<p>One <strong>two</strong> " +
                '<span class="bloom-overflowStart">' +
                kZeroWidthNonJoiner +
                "</span>three four</p>",
        );
        expect(editable.querySelector("strong")?.textContent).toBe("two");
    });

    it("measures up to the near side of a marker that is already there", () => {
        const editable = makeEditable(
            '<p>One two <span class="bloom-overflowStart">' +
                kZeroWidthNonJoiner +
                "</span>three four</p>",
        );
        const marker = editable.querySelector(kMarkerSelector) as HTMLElement;
        const paragraph = editable.querySelector("p") as HTMLElement;
        const range = document.createRange();

        // The linearizer records the offset of the marker as the position of the text node
        // that follows it, which is the far side of the marker.
        setFitRangeEnd(range, {
            container: marker.nextSibling as Node,
            offset: 0,
        });

        expect(range.endContainer).toBe(paragraph);
        expect(range.endOffset).toBe(
            Array.from(paragraph.childNodes).indexOf(marker),
        );
    });

    it("finds the offset of a marker that a saved page already carried", () => {
        const editable = makeEditable(
            '<p>One two <span class="bloom-overflowStart">' +
                kZeroWidthNonJoiner +
                "</span>three four</p>",
        );

        expect(hasOverflowMarker(editable)).toBe(true);
        expect(getOverflowMarkerOffset(editable)).toBe(8);
    });
});
