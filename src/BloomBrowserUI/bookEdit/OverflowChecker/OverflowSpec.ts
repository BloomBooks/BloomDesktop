"use strict";
import { describe, it, expect, beforeEach } from "vitest";
import OverflowChecker from "../../bookEdit/OverflowChecker/OverflowChecker";

// The padding-bottom that bloom-padForOverflow boxes measure is kept in the style attribute,
// which Bloom syncs between all copies of a data-book field. So the title, which appears on
// several xmatter pages, must only be measured on the front cover (BL-16811).
describe("OverflowChecker.shouldMeasurePaddingForOverflow", () => {
    const makeEditable = (
        pageClasses: string | null,
        dataBook: string,
    ): HTMLElement => {
        const editable = document.createElement("div");
        editable.className = "bloom-editable bloom-padForOverflow";
        editable.setAttribute("data-book", dataBook);
        if (pageClasses !== null) {
            const page = document.createElement("div");
            page.className = pageClasses;
            const group = document.createElement("div");
            group.className = "bloom-translationGroup";
            page.appendChild(group);
            group.appendChild(editable);
            document.body.appendChild(page);
        } else {
            document.body.appendChild(editable);
        }
        return editable;
    };

    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("measures the title on the front cover", () => {
        const editable = makeEditable(
            "bloom-page bloom-frontMatter frontCover outsideFrontCover",
            "bookTitle",
        );
        expect(editable.closest(".bloom-page")).not.toBeNull(); // sanity check on the setup
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            true,
        );
    });

    it("does not measure the title on the title page", () => {
        const editable = makeEditable(
            "bloom-page bloom-frontMatter titlePage",
            "bookTitle",
        );
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            false,
        );
    });

    it("does not measure a title that is on no page at all", () => {
        const editable = makeEditable(null, "bookTitle");
        expect(editable.closest(".bloom-page")).toBeNull(); // sanity check on the setup
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            false,
        );
    });

    it("still measures other padded fields wherever they are", () => {
        const editable = makeEditable(
            "bloom-page bloom-frontMatter credits",
            "author",
        );
        expect(OverflowChecker.shouldMeasurePaddingForOverflow(editable)).toBe(
            true,
        );
    });
});
