import { describe, expect, it } from "vitest";
import {
    getDisplayNameFromImageUrl,
    normalizeCoverImageDesignation,
} from "./bloomImages";

describe("normalizeCoverImageDesignation", () => {
    it("marks a newly changed real image on a custom outside front cover", () => {
        const page = document.createElement("div");
        page.className = "bloom-page bloom-customLayout outsideFrontCover";
        page.innerHTML = `
            <div class="bloom-canvas">
                <img id="first" src="placeHolder.png" />
            </div>
            <div class="bloom-canvas">
                <img id="second" src="cover.png" />
            </div>`;

        const second = page.querySelector("#second") as HTMLElement;
        normalizeCoverImageDesignation(page);

        expect(second.getAttribute("data-book")).toBe("coverImage");
    });

    it("moves the marker from a placeholder to a real remaining image", () => {
        const page = document.createElement("div");
        page.className = "bloom-page bloom-customLayout outsideFrontCover";
        page.innerHTML = `
            <div class="bloom-canvas">
                <img id="placeholder" src="placeHolder.png" data-book="coverImage" />
            </div>
            <div class="bloom-canvas">
                <img id="real" src="real-cover.png" />
            </div>`;

        normalizeCoverImageDesignation(page);

        const placeholder = page.querySelector("#placeholder") as HTMLElement;
        const real = page.querySelector("#real") as HTMLElement;
        expect(placeholder.hasAttribute("data-book")).toBe(false);
        expect(real.getAttribute("data-book")).toBe("coverImage");
    });

    it("does not create a new placeholder marker when no real images remain", () => {
        const page = document.createElement("div");
        page.className = "bloom-page bloom-customLayout outsideFrontCover";
        page.innerHTML = `
            <div class="bloom-canvas">
                <img id="first" src="placeHolder.png" />
            </div>
            <div class="bloom-canvas">
                <img id="second" src="placeHolder.png" />
            </div>`;

        normalizeCoverImageDesignation(page);

        expect(page.querySelector('[data-book="coverImage"]')).toBeNull();
    });

    it("keeps an existing real cover image", () => {
        const page = document.createElement("div");
        page.className = "bloom-page bloom-customLayout outsideFrontCover";
        page.innerHTML = `
            <div class="bloom-canvas">
                <img id="existing" src="existing.png" data-book="coverImage" />
            </div>
            <div class="bloom-canvas">
                <img id="preferred" src="preferred.png" />
            </div>`;

        const preferred = page.querySelector("#preferred") as HTMLElement;
        normalizeCoverImageDesignation(page);

        const existing = page.querySelector("#existing") as HTMLElement;
        expect(existing.getAttribute("data-book")).toBe("coverImage");
        expect(preferred.hasAttribute("data-book")).toBe(false);
    });

    it("prefers a non-placeholder background image over another designated image", () => {
        const page = document.createElement("div");
        page.className = "bloom-page bloom-customLayout outsideFrontCover";
        page.innerHTML = `
            <div class="bloom-canvas">
                <div class="bloom-backgroundImage">
                    <img id="background" src="background.png" />
                </div>
            </div>
            <div class="bloom-canvas">
                <img id="existing" src="existing.png" data-book="coverImage" />
            </div>`;

        normalizeCoverImageDesignation(page);

        const background = page.querySelector("#background") as HTMLElement;
        const existing = page.querySelector("#existing") as HTMLElement;
        expect(background.getAttribute("data-book")).toBe("coverImage");
        expect(existing.hasAttribute("data-book")).toBe(false);
    });
});

describe("getDisplayNameFromImageUrl", () => {
    it("leaves a plain name alone", () => {
        expect(getDisplayNameFromImageUrl("flower.jpg")).toBe("flower.jpg");
    });

    it("decodes spaces", () => {
        expect(getDisplayNameFromImageUrl("my%20photo.jpg")).toBe(
            "my photo.jpg",
        );
    });

    // The reported name from BL-16658. decodeURI left the reserved characters escaped,
    // so the alt text read "This Image !%40%23%24%^%26()2.jpg".
    it("decodes the reserved characters that decodeURI would have left escaped", () => {
        expect(
            getDisplayNameFromImageUrl(
                "This%20Image%20!%40%23%24%25%5e%26()2.jpg",
            ),
        ).toBe("This Image !@#$%^&()2.jpg");
    });

    it("drops the query string", () => {
        expect(getDisplayNameFromImageUrl("flower.jpg?thumbnail=1")).toBe(
            "flower.jpg",
        );
    });

    it("returns the name unchanged when it has a stray % that isn't an escape", () => {
        // decodeURIComponent would throw on this; we must still produce alt text.
        expect(getDisplayNameFromImageUrl("50%.jpg")).toBe("50%.jpg");
    });

    it("decodes a non-ASCII name", () => {
        expect(
            getDisplayNameFromImageUrl("%E0%B8%A0%E0%B8%B2%E0%B8%9E.jpg"),
        ).toBe("ภาพ.jpg");
    });
});
