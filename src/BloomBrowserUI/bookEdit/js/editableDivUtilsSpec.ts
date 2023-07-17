import { EditableDivUtils } from "./editableDivUtils";

describe("EditableDivUtils Tests", () => {
    it("fixUpEmptyishParagraphs does not modify paragraphs with content", () => {
        const testCases = [
            "<p>A</p>",
            "<p>A&nbsp;</p>",
            "<p>&nbsp;A</p>",
            "<p>&nbsp;<span>A</span></p>"
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
                '<p><br><span id="cke_bm_49C" style="display: none;">&nbsp;</span></p>'
            ],
            [
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>&nbsp;</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span><br></p>'
            ]
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
});
