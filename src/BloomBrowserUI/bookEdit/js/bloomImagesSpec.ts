import { describe, expect, it } from "vitest";
import {
    getDisplayNameFromImageUrl,
    normalizeCoverImageDesignation,
} from "./bloomImages";

// The markup here mirrors what a real custom cover has: every picture that belongs to the book
// sits in a bloom-imageContainer inside a canvas element, while branding markup is dropped into
// a canvas element of its own without a container. That difference is what tells the book's own
// pictures apart from the branding (BL-16776), so the tests have to carry it.
function makeCustomCover(innerHtml: string): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page bloom-customLayout outsideFrontCover";
    page.innerHTML = `<div class="bloom-canvas">${innerHtml}</div>`;
    return page;
}

function picture(id: string, src: string, extraClasses = ""): string {
    return `
        <div class="bloom-canvas-element ${extraClasses}">
            <div class="bloom-imageContainer"><img id="${id}" src="${src}" /></div>
        </div>`;
}

describe("normalizeCoverImageDesignation", () => {
    it("marks a newly changed real image on a custom outside front cover", () => {
        const page = makeCustomCover(
            picture("first", "placeHolder.png") +
                picture("second", "cover.png"),
        );

        normalizeCoverImageDesignation(page);

        const second = page.querySelector("#second") as HTMLElement;
        expect(second.getAttribute("data-book")).toBe("coverImage");
    });

    it("moves the marker from a placeholder to a real remaining image", () => {
        const page = makeCustomCover(
            picture("placeholder", "placeHolder.png") +
                picture("real", "real-cover.png"),
        );
        (page.querySelector("#placeholder") as HTMLElement).setAttribute(
            "data-book",
            "coverImage",
        );

        normalizeCoverImageDesignation(page);

        const placeholder = page.querySelector("#placeholder") as HTMLElement;
        const real = page.querySelector("#real") as HTMLElement;
        expect(placeholder.hasAttribute("data-book")).toBe(false);
        expect(real.getAttribute("data-book")).toBe("coverImage");
    });

    it("does not create a new placeholder marker when no real images remain", () => {
        const page = makeCustomCover(
            picture("first", "placeHolder.png") +
                picture("second", "placeHolder.png"),
        );

        normalizeCoverImageDesignation(page);

        expect(page.querySelector('[data-book="coverImage"]')).toBeNull();
    });

    it("keeps an existing real cover image", () => {
        const page = makeCustomCover(
            picture("existing", "existing.png") +
                picture("preferred", "preferred.png"),
        );
        (page.querySelector("#existing") as HTMLElement).setAttribute(
            "data-book",
            "coverImage",
        );

        normalizeCoverImageDesignation(page);

        const existing = page.querySelector("#existing") as HTMLElement;
        const preferred = page.querySelector("#preferred") as HTMLElement;
        expect(existing.getAttribute("data-book")).toBe("coverImage");
        expect(preferred.hasAttribute("data-book")).toBe(false);
    });

    it("prefers a non-placeholder background image over another designated image", () => {
        const page = makeCustomCover(
            picture("background", "background.png", "bloom-backgroundImage") +
                picture("existing", "existing.png"),
        );
        (page.querySelector("#existing") as HTMLElement).setAttribute(
            "data-book",
            "coverImage",
        );

        normalizeCoverImageDesignation(page);

        const background = page.querySelector("#background") as HTMLElement;
        const existing = page.querySelector("#existing") as HTMLElement;
        expect(background.getAttribute("data-book")).toBe("coverImage");
        expect(existing.hasAttribute("data-book")).toBe(false);
    });

    // The old shape: a background image sitting straight inside the bloom-canvas, not yet
    // converted to a canvas element with an image container. It is one of the book's own pictures,
    // so it must still be eligible -- otherwise normalizing would strip the mark off a legacy
    // cover instead of keeping it.
    it("counts a background image sitting directly in the bloom-canvas", () => {
        const page = document.createElement("div");
        page.className = "bloom-page bloom-customLayout outsideFrontCover";
        page.innerHTML = `
            <div class="bloom-canvas">
                <img id="old-style" src="the-cover.jpg" data-book="coverImage" />
            </div>`;

        normalizeCoverImageDesignation(page);

        const old = page.querySelector("#old-style") as HTMLElement;
        expect(old.getAttribute("data-book")).toBe("coverImage");
    });

    // The reported case: a branded cover whose own picture is still the placeholder. The branding
    // logo is a real image and comes first on the page, so before BL-16776 it was designated as
    // the book's cover image and saved that way.
    it("never designates a branding image as the cover image", () => {
        const page = makeCustomCover(
            picture("cover", "placeHolder.png", "bloom-backgroundImage") +
                `<div class="bloom-canvas-element">
                    <div data-book="cover-branding-bottom-html">
                        <img id="logo" class="branding" src="Little-Zebra.png" />
                    </div>
                </div>`,
        );

        normalizeCoverImageDesignation(page);

        const logo = page.querySelector("#logo") as HTMLElement;
        expect(logo.hasAttribute("data-book")).toBe(false);
    });

    // Books saved while the bug was live carry the bad mark on disk. Opening the cover has to put
    // it back rather than leaving the branding logo as the book's cover image forever.
    it("heals a cover already saved with the branding image marked", () => {
        const page = makeCustomCover(
            picture("cover", "placeHolder.png", "bloom-backgroundImage") +
                `<div class="bloom-canvas-element">
                    <div data-book="cover-branding-bottom-html">
                        <img id="logo" class="branding" src="Little-Zebra.png"
                             data-book="coverImage" />
                    </div>
                </div>`,
        );

        // Sanity check: the page really does start out mis-marked, which is the point of the test.
        expect(
            (page.querySelector("#logo") as HTMLElement).getAttribute(
                "data-book",
            ),
        ).toBe("coverImage");

        normalizeCoverImageDesignation(page);

        const logo = page.querySelector("#logo") as HTMLElement;
        const cover = page.querySelector("#cover") as HTMLElement;
        expect(logo.hasAttribute("data-book")).toBe(false);
        expect(cover.getAttribute("data-book")).toBe("coverImage");
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
