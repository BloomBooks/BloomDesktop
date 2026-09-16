import { describe, expect, test, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Nothing here
// needs it; constructing a CanvasElementManager is what pulls it in.
vi.mock("comicaljs", () => ({
    Bubble: class {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: () => {},
    },
}));

// This import deliberately comes after the vi.mock call above, so that the module graph
// it pulls in gets the stubbed comicaljs.
import { CanvasElementManager } from "./CanvasElementManager";

// Builds a cropped picture: the canvas element is the shape of the part that shows, and the
// img inside it is larger and pushed up and left, so only that part of it is visible.
function makeCroppedPicture(): {
    canvasElement: HTMLElement;
    img: HTMLImageElement;
} {
    document.body.innerHTML = `<div class="bloom-page"><div class="bloom-canvas">
        <div class="bloom-canvas-element" style="width: 100px; height: 80px; left: 10px; top: 20px;">
            <div class="bloom-imageContainer">
                <img src="old.png" style="width: 150px; height: 120px; left: -25px; top: -30px;"/>
            </div>
        </div></div></div>`;
    return {
        canvasElement: document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement,
        img: document.querySelector("img") as HTMLImageElement,
    };
}

// The state an undo hands back: what both boxes were before the picture was replaced.
const cropInfoOfCroppedPicture = {
    width: "150px",
    height: "120px",
    left: "-25px",
    top: "-30px",
    canvasElement: {
        width: "100px",
        height: "80px",
        left: "10px",
        top: "20px",
    },
};

describe("updateCanvasElementForChangedImage", () => {
    // Just one for the whole file: a CanvasElementManager initializes the one image undo
    // manager, which refuses to be initialized twice.
    const manager = new CanvasElementManager();

    test("an undo puts both the image's box and the canvas element's back", () => {
        const { canvasElement, img } = makeCroppedPicture();
        // Replacing the picture reshaped both boxes to suit the new one. Sanity check that
        // they really do differ from what we are about to restore.
        canvasElement.style.width = "200px";
        canvasElement.style.height = "50px";
        canvasElement.style.left = "0px";
        canvasElement.style.top = "35px";
        img.style.width = "";
        img.style.left = "";
        expect(canvasElement.style.width).not.toBe(
            cropInfoOfCroppedPicture.canvasElement.width,
        );

        manager.updateCanvasElementForChangedImage(
            img,
            cropInfoOfCroppedPicture,
        );

        expect(img.style.width).toBe("150px");
        expect(img.style.height).toBe("120px");
        expect(img.style.left).toBe("-25px");
        expect(img.style.top).toBe("-30px");
        expect(canvasElement.style.width).toBe("100px");
        expect(canvasElement.style.height).toBe("80px");
        expect(canvasElement.style.left).toBe("10px");
        expect(canvasElement.style.top).toBe("20px");
    });

    // The case the AI Image Editor produces: the picture on a page is normally the
    // background image of its bloom-canvas, and that path used to be the only one that put
    // any cropping back at all. Even it put back only the img's box (BL-16868).
    test("an undo of a background image puts both boxes back too", () => {
        const { canvasElement, img } = makeCroppedPicture();
        canvasElement.classList.add("bloom-backgroundImage");
        canvasElement.style.width = "200px";
        canvasElement.style.height = "50px";
        img.style.width = "";

        manager.updateCanvasElementForChangedImage(
            img,
            cropInfoOfCroppedPicture,
        );

        expect(img.style.width).toBe("150px");
        expect(img.style.left).toBe("-25px");
        expect(canvasElement.style.width).toBe("100px");
        expect(canvasElement.style.height).toBe("80px");
    });

    test("replacing the picture drops the cropping", () => {
        const { img } = makeCroppedPicture();
        expect(img.style.width).toBe("150px"); // sanity check: it starts cropped

        manager.updateCanvasElementForChangedImage(img);

        expect(img.style.width).toBe("");
        expect(img.style.height).toBe("");
        expect(img.style.left).toBe("");
        expect(img.style.top).toBe("");
    });
});
