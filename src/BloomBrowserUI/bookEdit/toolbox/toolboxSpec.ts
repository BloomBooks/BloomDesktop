import { describe, it, expect } from "vitest";
import {
    cleanUpNbsps,
    editableMightBeRewritten,
    removeCommentsFromEditableHtml,
} from "./toolbox";

describe("toolbox tests", () => {
    // This gate decides whether we take a ckeditor bookmark, which splits the text node the
    // user is typing in. Saying "no" when one of the clean-ups would in fact rewrite the box
    // would lose the user's insertion point, so it must catch everything they act on.
    it("editableMightBeRewritten says no for ordinary text", () => {
        const div = document.createElement("div");
        div.innerHTML = "<p>overflow</p><p>plain text, nothing to clean up</p>";

        expect(editableMightBeRewritten(div)).toBe(false);
    });

    it("editableMightBeRewritten says yes for anything the clean-ups act on", () => {
        const withComment = document.createElement("div");
        withComment.innerHTML = "<p>text</p>";
        withComment.firstChild!.appendChild(document.createComment("x"));
        // sanity check: this is the case removeCommentsFromEditableHtml rewrites
        expect(withComment.innerHTML).toContain("<!--");
        expect(editableMightBeRewritten(withComment)).toBe(true);

        const withNbsp = document.createElement("div");
        withNbsp.innerHTML = "<p>a&nbsp;b</p>";
        // sanity check: the nbsp really did survive into the html cleanUpNbsps scans
        expect(withNbsp.innerHTML).toContain("&nbsp;");
        expect(editableMightBeRewritten(withNbsp)).toBe(true);
    });

    it("removeCommentsFromEditableHtml removes comments correctly including ones with new lines", () => {
        const p = document.createElement("p");
        const span1 = document.createElement("span");
        span1.appendChild(document.createTextNode("text"));
        p.appendChild(span1);
        p.appendChild(document.createComment("comment"));
        const span2 = document.createElement("span");
        span2.appendChild(document.createComment("another \r\ncomment"));
        span2.appendChild(document.createTextNode("more "));
        span2.appendChild(document.createComment(""));
        span2.appendChild(document.createTextNode("text"));
        p.appendChild(span2);

        // validate our setup
        expect(p.innerHTML).toBe(
            "<span>text</span><!--comment--><span><!--another \r\ncomment-->more <!---->text</span>",
        );

        removeCommentsFromEditableHtml(p);
        expect(p.innerHTML).toBe("<span>text</span><span>more text</span>");
    });

    function runNbspTest(input: string, expectedOutput: string | null = null) {
        if (expectedOutput === null) expectedOutput = input;

        const div = document.createElement("div");
        div.innerHTML = `<p>${input}</p>`;
        cleanUpNbsps(div);
        expect(div.innerHTML).toBe(`<p>${expectedOutput}</p>`);
    }

    it("cleanUpNbsps does not modify when no errant nbsp", () => {
        runNbspTest("text and more"); //vanilla

        runNbspTest("&nbsp;A b"); //leading
        runNbspTest("A b&nbsp;"); //trailing
        runNbspTest("A b &nbsp;C"); //space before
        runNbspTest("A b&nbsp; C"); //space after
        runNbspTest("A b &nbsp; "); //between spaces
        runNbspTest("A b &nbsp;&nbsp; C"); //2 nbsp between spaces

        runNbspTest("<span>A&nbsp; b</span>"); //in span

        // Real, complex talking book scenario; bookmark ok
        runNbspTest(
            '<span class="bloom-ui-current-audio-marker bloom-ui"></span><span id="i98d2ec63-7472-4f10-964a-e6ed9c7daf45" class="audio-sentence ui-audioCurrent" recordingmd5="undefined">A.</span> <span id="i686c601d-f814-4fdd-9a90-280e2d426437" class="audio-sentence" recordingmd5="undefined">B. dd <span id="cke_bm_49C" style="display: none;">&nbsp;</span><strong>C</strong></span>',
        );

        //hidden span in middle, keep preceding nbsp
        runNbspTest(
            'A&nbsp;<span data-cke-bookmark="1" id="cke_bm_67C" style="display: none;">&nbsp;</span> B',
        );
        //hidden span at beginning, keep following nbsp
        runNbspTest(
            '<span data-cke-bookmark="1" id="cke_bm_68C" style="display: none;">&nbsp;</span>&nbsp;A',
        );
        //hidden span at end, after ending nbsp; keep ending nbsp
        runNbspTest(
            'A&nbsp;<span data-cke-bookmark="1" id="cke_bm_68D" style="display: none;">&nbsp;</span>',
        );
    });

    it("cleanUpNbsps keeps nbsp for French scenarios", () => {
        runNbspTest("Comment vas-tu&nbsp;?");
        runNbspTest("Salut Jeanne&nbsp;!");
        runNbspTest("«&nbsp;Ah, salut Pierre.&nbsp;»");
        runNbspTest("—&nbsp;Ah, salut Pierre.");
        runNbspTest("Il a dit&nbsp;: «&nbsp;Hi.&nbsp;»");
        runNbspTest("Go&nbsp;; do likewise.");
    });

    it("cleanUpNbsps replaces nbsp when not next to a regular space", () => {
        // nbsp surrounded by non-space characters
        runNbspTest("A&nbsp;B", "A B");
        runNbspTest("<span>A&nbsp;B</span>", "<span>A B</span>");

        // 2 nbsp together; could remove either one, but this mimics typing
        runNbspTest("A b&nbsp;&nbsp;C", "A b &nbsp;C");

        // 2 nbsp together at the end
        runNbspTest("A b&nbsp;&nbsp;", "A b &nbsp;");

        // 3 nbsp together; could remove 1st or 2nd, but this mimics typing
        runNbspTest("A b&nbsp;&nbsp;&nbsp;", "A b &nbsp;&nbsp;");

        // 3 nbsp together after a space, at the end
        runNbspTest("A b &nbsp;&nbsp;&nbsp;", "A b &nbsp; &nbsp;");

        // 3 nbsp together before a space
        // We don't think this scenario will happen in production.
        runNbspTest("A b&nbsp;&nbsp;&nbsp; ", "A b &nbsp;&nbsp; ");

        // Verifying non-French punctuation does not trigger nbsp retention
        runNbspTest("“&nbsp;Hi Jean!&nbsp;”", "“ Hi Jean! ”");
        runNbspTest("¿&nbsp;Huh?", "¿ Huh?");
        runNbspTest("Uh&nbsp;, yeah&nbsp;.", "Uh , yeah .");

        // bookmark doesn't cause problem
        runNbspTest(
            'A<span data-cke-bookmark="1" id="cke_bm_70E" style="display: none;">&nbsp;</span>&nbsp;B',
            'A<span data-cke-bookmark="1" id="cke_bm_70E" style="display: none;">&nbsp;</span> B',
        );

        // Real, complex talking book scenario; bookmark doesn't cause problem
        runNbspTest(
            '<span class="bloom-ui-current-audio-marker bloom-ui"></span><span id="i98d2ec63-7472-4f10-964a-e6ed9c7daf45" class="audio-sentence ui-audioCurrent" recordingmd5="undefined">A.</span> <span id="i686c601d-f814-4fdd-9a90-280e2d426437" class="audio-sentence" recordingmd5="undefined">B. dd&nbsp;<span id="cke_bm_49C" style="display: none;">&nbsp;</span><strong>C</strong></span>',
            '<span class="bloom-ui-current-audio-marker bloom-ui"></span><span id="i98d2ec63-7472-4f10-964a-e6ed9c7daf45" class="audio-sentence ui-audioCurrent" recordingmd5="undefined">A.</span> <span id="i686c601d-f814-4fdd-9a90-280e2d426437" class="audio-sentence" recordingmd5="undefined">B. dd <span id="cke_bm_49C" style="display: none;">&nbsp;</span><strong>C</strong></span>',
        );
    });

    it("cleanUpNbsps doesn't crash, makes no change in pathological case", () => {
        // In theory, someone could do something really nasty... put a nbsp inside an attribute.
        runNbspTest('<span data-attr="&nbsp;yuck!">A B</span>');
        runNbspTest('<span data-attr="&nbsp;yuck!">A&nbsp; B</span>');
        runNbspTest(
            '<span data-attr="&nbsp;yuck!">A&nbsp; <span id="cke_bm_99F" style="display: none;">&nbsp;</span>B</span>',
        );

        // Ideally, we would remove the nbsp here, but in this corner case, the most
        // important thing is to do no harm.
        runNbspTest('<span data-attr="&nbsp;yuck!">A&nbsp;B</span>');
    });

    // runNbspTest only compares serialized html, which cannot tell "we left the DOM alone" apart
    // from "we rebuilt it into something that serializes the same". These two check the actual
    // nodes, because rebuilding matters: the reader tools' violation highlights and the Talking
    // Book tool's audio highlights are live Ranges, and replacing a text node collapses any Range
    // pointing into it. See the note where handlePageEditing() calls cleanUpNbsps.
    it("cleanUpNbsps keeps the same text node when there is nothing to convert", () => {
        const div = document.createElement("div");
        div.innerHTML = "<p>A b&nbsp;</p>"; // trailing nbsp, so nothing to convert
        const textNodeBefore = div.querySelector("p")!.firstChild;
        // Sanity check the setup: we mean to be watching a text node.
        expect(textNodeBefore?.nodeType).toBe(Node.TEXT_NODE);

        cleanUpNbsps(div);

        expect(div.innerHTML).toBe("<p>A b&nbsp;</p>");
        expect(div.querySelector("p")!.firstChild).toBe(textNodeBefore);
    });

    it("cleanUpNbsps does rebuild the content when there is something to convert", () => {
        const div = document.createElement("div");
        div.innerHTML = "<p>A&nbsp;b c</p>";
        const textNodeBefore = div.querySelector("p")!.firstChild;
        // Sanity check the setup: this nbsp is one we expect to be converted.
        expect(textNodeBefore?.textContent).toBe("A b c"); // the character after the A is U+00A0, a non-breaking space

        cleanUpNbsps(div);

        expect(div.innerHTML).toBe("<p>A b c</p>");
        expect(div.querySelector("p")!.firstChild).not.toBe(textNodeBefore);
    });
});
