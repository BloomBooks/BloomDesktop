import { describe, it, expect } from "vitest";
import { EditableDivUtils } from "./editableDivUtils";

describe("EditableDivUtils Tests", () => {
    it("fixUpEmptyishParagraphs does not modify paragraphs with content", () => {
        const testCases = [
            "<p>A</p>",
            "<p>A&nbsp;</p>",
            "<p>&nbsp;A</p>",
            "<p>&nbsp;<span>A</span></p>",
        ];

        for (const testCase of testCases) {
            const div = document.createElement("div");
            div.innerHTML = testCase;

            EditableDivUtils.fixUpEmptyishParagraphs(div);

            expect(div.innerHTML).toEqual(testCase);
        }
    });

    it("fixUpEmptyishParagraphs corrects paragraphs with only &nbsp; to have only <br>", () => {
        // [0] is the input, [1] is the expected output
        const testCases = [
            ["<p>&nbsp;</p>", "<p><br></p>"],
            ["<p>&nbsp;</p><p>&nbsp;</p>", "<p><br></p><p><br></p>"],
            [
                '<p>&nbsp;<span id="cke_bm_49C" style="display: none;">&nbsp;</span></p>',
                '<p><br><span id="cke_bm_49C" style="display: none;">&nbsp;</span></p>',
            ],
            [
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>&nbsp;</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span><br></p>',
            ],
        ];

        for (const testCase of testCases) {
            const div = document.createElement("div");
            div.innerHTML = testCase[0];

            EditableDivUtils.fixUpEmptyishParagraphs(div);

            expect(div.innerHTML).toEqual(testCase[1]);
        }
    });

    it("fixUpEmptyishParagraphs handles empty text node", () => {
        const div = document.createElement("div");
        const p = document.createElement("p");
        div.appendChild(p);
        const emptyTextNode = document.createTextNode("");
        p.appendChild(emptyTextNode);
        const nbspTextNode = document.createTextNode("\u00A0");
        p.appendChild(nbspTextNode);

        // Verify setup
        expect(div.innerHTML).toEqual("<p>&nbsp;</p>");

        EditableDivUtils.fixUpEmptyishParagraphs(div);

        // As far as we know, ckeditor's getData() only replaces
        // a single <br> with a single &nbsp; (which is what we are trying to reverse).
        // So we think we want to leave the p alone in this case.
        expect(div.innerHTML).toEqual("<p>&nbsp;</p>");
    });

    it("safelyReplaceContentWithCkEditorData ensures no initial blank paragraph", () => {
        // [0]:input div html, [1]:input ckeditor data, [2]:expected output
        const testCases = [
            // The main scenario we are trying to fix: ckeditor wraps lone initial bookmark in a paragraph; don't let it.
            [
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>&nbsp;</p><p>A</p>',
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
            ],
            // Ensures we leave well enough alone
            [
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
            ],
            // If ckeditor wants to wrap a non bookmark for some reason, leave it alone
            [
                "<span>&nbsp;</span><p>A</p>",
                "<p><span>&nbsp;</span></p><p>A</p>",
                "<p><span>&nbsp;</span></p><p>A</p>",
            ],
            // Not sure this can really happen, but prove we leave paragraph wrapping alone if there is content besides just nbsp
            [
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span>Z<p>A</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>Z</p><p>A</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>Z</p><p>A</p>',
            ],
        ];

        for (const testCase of testCases) {
            const div = document.createElement("div");
            div.innerHTML = testCase[0];

            EditableDivUtils.safelyReplaceContentWithCkEditorData(
                div,
                testCase[1],
            );

            expect(div.innerHTML).toEqual(testCase[2]);
        }
    });
});
