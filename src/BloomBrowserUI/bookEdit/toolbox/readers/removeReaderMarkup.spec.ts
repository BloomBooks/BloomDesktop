import { describe, it, expect } from "vitest";
import { removeReaderMarkup } from "./removeReaderMarkup";

// The reader tools mark a page that has more text on it than the level allows. That marking is an
// editing aid and must not reach the user's book. Since a save now works from a clone, the cleanup
// has to be something we can point at an element rather than something that reaches into the live
// page frame.

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

    it("leaves the text alone", () => {
        // The tools' word and sentence highlighting is painted with the CSS Custom Highlight API,
        // so there is nothing of theirs inside the text to clean up -- and nothing here may
        // disturb what the user actually wrote.
        const html =
            '<div class="bloom-editable"><p>Just <i>text</i>, with a | in it.</p></div>';
        const page = pageWith(
            html,
            "bloom-page page-too-many-words-or-sentences",
        );

        removeReaderMarkup(page);

        expect(page.innerHTML).toBe(html);
    });

    it("leaves a page that was never marked alone", () => {
        const html = '<div class="bloom-editable"><p>Just text.</p></div>';
        const page = pageWith(html);

        removeReaderMarkup(page);

        expect(page.className).toBe("bloom-page");
        expect(page.innerHTML).toBe(html);
    });
});
