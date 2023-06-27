import { cleanUpNbsps, removeCommentsFromEditableHtml } from "./toolbox";

describe("toolbox tests", () => {
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
            "<span>text</span><!--comment--><span><!--another \r\ncomment-->more <!---->text</span>"
        );

        removeCommentsFromEditableHtml(p);
        expect(p.innerHTML).toBe("<span>text</span><span>more text</span>");
    });

    function runNbspTest(input: string, expectedOutput: string | null = null) {
        if (expectedOutput === null) expectedOutput = input;

        const div = document.createElement("div");
        div.innerHTML = input;
        cleanUpNbsps(div);
        expect(div.innerHTML).toBe(expectedOutput);
    }

    it("cleanUpNbsps does not modify when no errant nbsp", () => {
        runNbspTest("<p>text and more</p>"); //vanilla

        runNbspTest("<p>&nbsp;A b</p>"); //leading
        runNbspTest("<p>A b&nbsp;</p>"); //trailing
        runNbspTest("<p>A b &nbsp;C</p>"); //space before
        runNbspTest("<p>A b&nbsp; C</p>"); //space after
        runNbspTest("<p>A b &nbsp; </p>"); //between spaces
        runNbspTest("<p>A b &nbsp;&nbsp; C</p>"); //2 nbsp between spaces
    });

    it("cleanUpNbsps keeps nbsp for French scenarios", () => {
        runNbspTest("<p>Comment vas-tu&nbsp;?</p>");
        runNbspTest("<p>Salut Jeanne&nbsp;!</p>");
        runNbspTest("<p>«&nbsp;Ah, salut Pierre.&nbsp;»</p>");
        runNbspTest("<p>—&nbsp;Ah, salut Pierre.</p>");
        runNbspTest("<p>Il a dit&nbsp;: «&nbsp;Hi.&nbsp;»</p>");
        runNbspTest("<p>Go&nbsp;; do likewise.</p>");
    });

    it("cleanUpNbsps replaces nbsp when not next to a space", () => {
        runNbspTest("<p>A&nbsp;B</p>", "<p>A B</p>");

        // 2 nbsp together
        runNbspTest("<p>A b&nbsp;&nbsp;C</p>", "<p>A b &nbsp;C</p>");

        // 2 nbsp together at the end; could go either way, but this mimics typing
        runNbspTest("<p>A b&nbsp;&nbsp;</p>", "<p>A b &nbsp;</p>");

        // 3 nbsp together; this could go either way; but this mimics typing
        runNbspTest("<p>A b&nbsp;&nbsp;&nbsp;</p>", "<p>A b &nbsp;&nbsp;</p>");

        // 3 nbsp together after a space, at the end
        runNbspTest(
            "<p>A b &nbsp;&nbsp;&nbsp;</p>",
            "<p>A b &nbsp; &nbsp;</p>"
        );

        // 3 nbsp together before a space
        // We don't think this scenario will happen in production.
        runNbspTest(
            "<p>A b&nbsp;&nbsp;&nbsp; </p>",
            "<p>A b &nbsp;&nbsp; </p>"
        );

        // Verifying non-French punctuation does not trigger nbsp retention
        runNbspTest("“&nbsp;Hi Jean!&nbsp;”", "“ Hi Jean! ”");
        runNbspTest("¿&nbsp;Huh?", "¿ Huh?");
        runNbspTest("Uh&nbsp;, yeah&nbsp;.", "Uh , yeah .");
    });
});
