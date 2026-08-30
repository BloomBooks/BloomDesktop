import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// The canvas element manager reaches into Comical, CKEditor and the rest of a live page,
// none of which a jsdom test has. We only want to know whether table editing asks it to
// wire the picture of a cell the table library has just rebuilt, so we watch that one call.
const { wireBloomCanvasAddedAfterPageLoad } = vi.hoisted(() => ({
    wireBloomCanvasAddedAfterPageLoad: vi.fn(),
}));
vi.mock("./canvasElementManager/CanvasElementManager", () => ({
    theOneCanvasElementManager: {
        wireBloomCanvasAddedAfterPageLoad,
        adjustAfterContainerResize: vi.fn(),
        refitBackgroundImage: vi.fn(),
    },
}));
vi.mock("./bloomImages", () => ({ SetupImagesInContainer: vi.fn() }));
vi.mock("./bloomVideo", () => ({ SetupVideoEditing: vi.fn() }));

import { kTableCellContentChangedEvent } from "bloom-table";
import { SetupTableEditing, TeardownTableEditing } from "./tableEditing";

// jsdom has no ResizeObserver, and table editing makes one to keep each cell's picture
// fitted to the cell as columns are resized.
globalThis.ResizeObserver ??= class {
    public observe(): void {}
    public unobserve(): void {}
    public disconnect(): void {}
} as unknown as typeof ResizeObserver;

describe("when the table library rebuilds a cell as a picture", () => {
    let container: HTMLElement;

    // A cell as the library leaves it once the user picks the Image content type: the cell's
    // own markup is gone and that type's template is there instead.
    const makeImageCell = (): HTMLElement => {
        const cell = document.createElement("div");
        cell.className = "bloom-cell";
        cell.setAttribute("data-content-type", "image");
        cell.innerHTML = `<div class="bloom-canvas" data-test="the picture"></div>`;
        container.appendChild(cell);
        return cell;
    };

    // The library raises this on the cell, and table editing listens on the container it
    // was set up for, so the event has to bubble the way the real one does.
    const sayTheCellChanged = (cell: HTMLElement): void => {
        cell.dispatchEvent(
            new CustomEvent(kTableCellContentChangedEvent, {
                bubbles: true,
                detail: { cell, contentType: "image" },
            }),
        );
    };

    beforeEach(() => {
        wireBloomCanvasAddedAfterPageLoad.mockClear();
        document.body.innerHTML = "";
        container = document.createElement("div");
        document.body.appendChild(container);
        SetupTableEditing(container);
    });

    it("asks the canvas element manager to wire the new picture", () => {
        const cell = makeImageCell();
        // Sanity check the fixture: nothing has been wired yet, and there is a picture to wire.
        expect(wireBloomCanvasAddedAfterPageLoad).not.toHaveBeenCalled();
        expect(cell.querySelector(".bloom-canvas")).not.toBeNull();

        sayTheCellChanged(cell);

        expect(wireBloomCanvasAddedAfterPageLoad).toHaveBeenCalledTimes(1);
        const wired = wireBloomCanvasAddedAfterPageLoad.mock.calls[0][0];
        expect(wired.getAttribute("data-test")).toBe("the picture");
    });

    it("finds the picture of a calendar day cell, which keeps it inside a wrapper", () => {
        const cell = makeImageCell();
        // A day cell holds everything it shows inside this one element, so its picture is a
        // grandchild rather than a child.
        cell.classList.add("calendarDayCell");
        const contents = document.createElement("div");
        contents.className = "calendarDayCellContents";
        contents.appendChild(cell.firstElementChild!);
        cell.appendChild(contents);

        sayTheCellChanged(cell);

        expect(wireBloomCanvasAddedAfterPageLoad).toHaveBeenCalledTimes(1);
        const wired = wireBloomCanvasAddedAfterPageLoad.mock.calls[0][0];
        expect(wired.getAttribute("data-test")).toBe("the picture");
    });

    it("asks for nothing when the cell is not a picture cell", () => {
        const cell = document.createElement("div");
        cell.className = "bloom-cell";
        cell.setAttribute("data-content-type", "text");
        cell.innerHTML = `<div class="bloom-translationGroup"></div>`;
        container.appendChild(cell);

        sayTheCellChanged(cell);

        expect(wireBloomCanvasAddedAfterPageLoad).not.toHaveBeenCalled();
    });

    afterEach(() => {
        TeardownTableEditing(container);
    });
});
