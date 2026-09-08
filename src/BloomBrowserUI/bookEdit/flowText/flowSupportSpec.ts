import { beforeEach, describe, expect, it } from "vitest";
import { getRefusalReason, kRefusalReasons, markRefusals } from "./flowSupport";

type BoxOptions = {
    groupClasses?: string;
    editableClasses?: string;
    innerHtml?: string;
};

/** A chained group holding one visible box, in the document so it has a computed style. */
function makeBox(options: BoxOptions = {}): HTMLElement {
    const group = document.createElement("div");
    group.className = `bloom-translationGroup ${
        options.groupClasses ?? "normal-style"
    }`;
    group.setAttribute("data-flow-chain", "chain-1");

    const editable = document.createElement("div");
    editable.className = `bloom-editable bloom-visibility-code-on ${
        options.editableClasses ?? ""
    }`;
    editable.setAttribute("lang", "xkal");
    editable.innerHTML = options.innerHtml ?? "<p>Some text</p>";
    group.appendChild(editable);

    document.body.appendChild(group);
    return editable;
}

describe("getRefusalReason", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("accepts an ordinary paragraph box of a normal-style group", () => {
        expect(getRefusalReason(makeBox())).toBeUndefined();
    });

    it("refuses a box that is in no translation group", () => {
        const orphan = document.createElement("div");
        orphan.className = "bloom-editable";
        document.body.appendChild(orphan);

        expect(getRefusalReason(orphan)).toBe(kRefusalReasons.noGroup);
    });

    it("refuses a box that is not normal-style", () => {
        const editable = makeBox({ groupClasses: "Heading1-style" });

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.notNormalStyle);
    });

    it("accepts normal-style on the editable when the group has no style class", () => {
        const editable = makeBox({
            groupClasses: "",
            editableClasses: "normal-style",
        });

        expect(getRefusalReason(editable)).toBeUndefined();
    });

    it("refuses a box with dir=rtl on the box", () => {
        const editable = makeBox();
        editable.setAttribute("dir", "rtl");

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.rightToLeft);
    });

    it("refuses a box with dir=rtl on the group", () => {
        const editable = makeBox();
        editable.parentElement?.setAttribute("dir", "RTL");

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.rightToLeft);
    });

    it("refuses a box whose computed direction is rtl", () => {
        const editable = makeBox();
        editable.style.direction = "rtl";

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.rightToLeft);
    });

    it("refuses a box holding recorded sentences", () => {
        const editable = makeBox({
            innerHtml:
                '<p><span class="audio-sentence" id="a1">One.</span></p>',
        });

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.talkingBook);
    });

    it("refuses a box that is itself a recording unit", () => {
        const editable = makeBox();
        editable.setAttribute("data-audiorecordingmode", "TextBox");

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.talkingBook);
    });

    it("refuses a box whose audio has been split", () => {
        const editable = makeBox({
            innerHtml: '<p><span class="bloom-postAudioSplit">One.</span></p>',
        });

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.talkingBook);
    });

    it("refuses a box with no paragraphs, marked on the box or on the group", () => {
        expect(
            getRefusalReason(
                makeBox({ editableClasses: "bloom-noParagraphs" }),
            ),
        ).toBe(kRefusalReasons.noParagraphs);
        expect(
            getRefusalReason(
                makeBox({ groupClasses: "normal-style bloom-noParagraphs" }),
            ),
        ).toBe(kRefusalReasons.noParagraphs);
    });

    it("refuses a word-find style", () => {
        expect(
            getRefusalReason(
                makeBox({ editableClasses: "QuizWordFind-style" }),
            ),
        ).toBe(kRefusalReasons.wordFind);
    });

    it("refuses a box that hyphenates automatically", () => {
        const editable = makeBox();
        editable.style.setProperty("hyphens", "auto");

        expect(getRefusalReason(editable)).toBe(kRefusalReasons.hyphenated);
    });

    it("refuses content the move primitives cannot reason about", () => {
        const editable = makeBox({
            innerHtml: '<p>A picture <img src="x.png"></p>',
        });

        expect(getRefusalReason(editable)).toBe(
            kRefusalReasons.unsupportedContent,
        );
    });
});

describe("markRefusals", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("marks the group of a refused box and reports that the chain cannot flow", () => {
        const good = makeBox();
        const bad = makeBox({ innerHtml: '<p><img src="x.png"></p>' });

        expect(markRefusals([good, bad])).toBe(true);
        expect(
            bad.parentElement?.classList.contains("bloom-flow-refused"),
        ).toBe(true);
        expect(
            bad.parentElement?.getAttribute("data-flow-refused-reason"),
        ).toBe(kRefusalReasons.unsupportedContent);
        expect(
            good.parentElement?.classList.contains("bloom-flow-refused"),
        ).toBe(false);
    });

    it("takes the marks off a group that is no longer refused", () => {
        const editable = makeBox({ innerHtml: '<p><img src="x.png"></p>' });
        markRefusals([editable]);

        editable.innerHTML = "<p>Plain text now</p>";

        expect(markRefusals([editable])).toBe(false);
        expect(
            editable.parentElement?.classList.contains("bloom-flow-refused"),
        ).toBe(false);
        expect(
            editable.parentElement?.hasAttribute("data-flow-refused-reason"),
        ).toBe(false);
    });
});
