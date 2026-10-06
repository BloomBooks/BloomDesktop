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

// The img's box before the picture was replaced, which an undo hands back. (The undo manager
// puts the canvas element's box back itself, before it calls updateCanvasElementForChangedImage.)
const cropInfoOfCroppedPicture = {
    width: "150px",
    height: "120px",
    left: "-25px",
    top: "-30px",
};

describe("updateCanvasElementForChangedImage", () => {
    // Just one for the whole file: a CanvasElementManager initializes the one image undo
    // manager, which refuses to be initialized twice.
    const manager = new CanvasElementManager();

    test("an undo puts the image's box back and keeps the canvas element's", () => {
        const { canvasElement, img } = makeCroppedPicture();
        // Replacing the picture dropped the img's cropping. Sanity check that it really
        // differs from what we are about to restore.
        img.style.width = "";
        img.style.left = "";
        expect(img.style.width).not.toBe(cropInfoOfCroppedPicture.width);

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
    // background image of its bloom-canvas.
    test("an undo of a background image keeps its cropping too", () => {
        const { canvasElement, img } = makeCroppedPicture();
        canvasElement.classList.add("bloom-backgroundImage");
        img.style.width = "";
        img.style.left = "";
        expect(img.style.width).toBe(""); // sanity check before the undo

        manager.updateCanvasElementForChangedImage(
            img,
            cropInfoOfCroppedPicture,
        );

        expect(img.style.width).toBe("150px");
        expect(img.style.left).toBe("-25px");
        expect(canvasElement.style.width).toBe("100px");
        expect(canvasElement.style.height).toBe("80px");
    });

    // An undo back to a picture that had no cropping has nothing to preserve, and must not be
    // told "this is not a new image" — that would also switch off the wait for the restored
    // src to load, leaving the sizing code reading the outgoing picture's dimensions.
    test("an undo of an uncropped picture puts no cropping back", () => {
        const { img } = makeCroppedPicture();
        // The replacement cropped it; the undo takes it back to a picture that was not cropped.
        img.style.width = "300px";
        img.style.left = "-60px";
        expect(img.style.width).toBe("300px"); // sanity check before the undo

        manager.updateCanvasElementForChangedImage(img, {
            width: "",
            height: "",
            left: "",
            top: "",
        });

        expect(img.style.width).toBe("");
        expect(img.style.left).toBe("");
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
