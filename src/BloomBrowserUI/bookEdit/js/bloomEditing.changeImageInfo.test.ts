import { describe, expect, it } from "vitest";
import { changeImageInfo } from "./bloomEditing";

describe("changeImageInfo", () => {
    it("updates the src that CKEditor saves the page with", () => {
        document.body.innerHTML = `<div class="bloom-page"><div class="bloom-editable"><img src="old.png" data-cke-saved-src="old.png"></div></div>`;
        const img = document.querySelector("img")!;
        expect(img.getAttribute("data-cke-saved-src")).toBe("old.png");

        changeImageInfo(img, {
            src: "new.png",
            copyright: "",
            creator: "",
            license: "",
        });

        expect(img.getAttribute("src")).toContain("new.png");
        expect(img.getAttribute("data-cke-saved-src")).toBe(
            img.getAttribute("src"),
        );
    });

    it("does not add CKEditor's saved src to an img that lacks it", () => {
        document.body.innerHTML = `<div class="bloom-page"><img src="old.png"></div>`;
        const img = document.querySelector("img")!;

        changeImageInfo(img, {
            src: "new.png",
            copyright: "",
            creator: "",
            license: "",
        });

        expect(img.hasAttribute("data-cke-saved-src")).toBe(false);
    });
});
