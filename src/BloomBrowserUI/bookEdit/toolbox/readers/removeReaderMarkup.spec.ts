import { describe, it, expect } from "vitest";
import { removeReaderMarkup } from "./removeReaderMarkup";

// The reader tools mark the page up while you edit. None of that belongs in the user's book, and
// since a save now works from a clone, the cleanup has to be something we can point at an element
// rather than something that reaches into the live page frame.

const pageWith = (inner: string, pageClasses = "bloom-page") => {
    const page = document.createElement("div");
    page.className = pageClasses;
    page.innerHTML = inner;
    return page;
};

describe("removeReaderMarkup", () => {
    it("takes the too-much-text marking off the page itself", () => {
        const page = pageWith(
            "<p>text</p>",
            "bloom-page page-too-many-words-or-sentences",
        );
        expect(
            page.classList.contains("page-too-many-words-or-sentences"),
        ).toBe(true); // sanity

        removeReaderMarkup(page);

        expect(
            page.classList.contains("page-too-many-words-or-sentences"),
        ).toBe(false);
        expect(page.classList.contains("bloom-page")).toBe(true);
    });

    it("takes it off a page nested inside what it is given", () => {
        // The save path hands us a clone of the body, so the marked div is a descendant.
        const body = document.createElement("div");
        body.appendChild(
            pageWith("<p>x</p>", "bloom-page page-too-many-words-or-sentences"),
        );

        removeReaderMarkup(body);

        expect(
            body.querySelectorAll(".page-too-many-words-or-sentences").length,
        ).toBe(0);
    });

    it("unwraps the segment spans, keeping the text and its formatting", () => {
        const page = pageWith(
            '<div class="bloom-editable"><p>' +
                '<span data-segment="sentence">Hello <b>big</b> world.</span>' +
                '<span data-segment="sentence"> Again.</span>' +
                "</p></div>",
        );

        removeReaderMarkup(page);

        expect(page.querySelectorAll("span[data-segment]").length).toBe(0);
        expect(page.querySelector("p")?.textContent).toBe(
            "Hello big world. Again.",
        );
        expect(page.querySelector("b")?.textContent).toBe("big");
    });

    it("removes an empty segment span rather than leaving it behind", () => {
        const page = pageWith(
            '<div class="bloom-editable"><p><span data-segment="word"></span>kept</p></div>',
        );

        removeReaderMarkup(page);

        expect(page.querySelectorAll("span[data-segment]").length).toBe(0);
        expect(page.querySelector("p")?.textContent).toBe("kept");
    });

    it("leaves a page that was never marked up alone", () => {
        const html =
            '<div class="bloom-editable"><p>Just <i>text</i>.</p></div>';
        const page = pageWith(html);

        removeReaderMarkup(page);

        expect(page.innerHTML).toBe(html);
    });
});
