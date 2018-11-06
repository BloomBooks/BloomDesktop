import { removeCommentsFromEditableHtml } from "./toolbox";

describe("toolbox tests", () => {
    it("removeCommentsFromEditableHtml removes comments correctly including ones with new lines", () => {
        var p = document.createElement("p");
        var span1 = document.createElement("span");
        span1.appendChild(document.createTextNode("text"));
        p.appendChild(span1);
        p.appendChild(document.createComment("comment"));
        var span2 = document.createElement("span");
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
});
