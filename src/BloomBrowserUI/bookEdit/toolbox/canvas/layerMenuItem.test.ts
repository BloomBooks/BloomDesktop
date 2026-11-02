import { afterEach, describe, expect, test } from "vitest";

import { buildCanvasElementControlRegistryContext } from "./buildCanvasElementControlRegistryContext";
import { canvasElementControlRegistry } from "./canvasElementControlRegistry";
import { getMenuSections } from "./canvasControlResolution";
import {
    IControlContext,
    IControlMenuCommandRow,
    IResolvedControl,
} from "./canvasControlTypes";

// Tests for the "Layer" submenu of the canvas element menu (BL-15992): where it sits in the
// menu, which element types offer it, and how its rows are disabled when the selected element
// is already at the front or back of the stack, or is the only thing that could move.

// Build a page with a bloom-canvas holding a background image and `count` picture canvas
// elements above it, with bubble levels matching their DOM order, as Bloom keeps them.
// Returns the picture elements, bottom-most first.
function makePageWithPictures(count: number): HTMLElement[] {
    const page = document.createElement("div");
    page.className = "bloom-page";
    const bloomCanvas = document.createElement("div");
    bloomCanvas.className = "bloom-canvas";
    page.appendChild(bloomCanvas);

    const makePicture = (level: number): HTMLElement => {
        const canvasElement = document.createElement("div");
        canvasElement.className = "bloom-canvas-element";
        canvasElement.setAttribute(
            "data-bubble",
            `{\`version\`:\`1.0\`,\`style\`:\`none\`,\`tails\`:[],\`level\`:${level}}`,
        );
        const container = document.createElement("div");
        container.className = "bloom-imageContainer";
        container.appendChild(document.createElement("img"));
        canvasElement.appendChild(container);
        bloomCanvas.appendChild(canvasElement);
        return canvasElement;
    };

    const background = makePicture(1);
    background.classList.add("bloom-backgroundImage");
    const pictures: HTMLElement[] = [];
    for (let i = 0; i < count; i++) {
        pictures.push(makePicture(i + 2));
    }
    document.body.appendChild(page);
    return pictures;
}

const getBackgroundImage = (): HTMLElement => {
    const background = document.querySelector(
        ".bloom-backgroundImage",
    ) as HTMLElement | null;
    if (!background) {
        throw new Error("test page has no background image");
    }
    return background;
};

const ctxFor = (canvasElement: HTMLElement): IControlContext =>
    buildCanvasElementControlRegistryContext(canvasElement);

// Resolve the menu for a picture canvas element, exactly as CanvasElementContextControls does.
const menuSectionsFor = (canvasElement: HTMLElement): IResolvedControl[][] =>
    getMenuSections(canvasElementControlRegistry.image, ctxFor(canvasElement));

const findLayerRow = (
    sections: IResolvedControl[][],
): IControlMenuCommandRow | undefined =>
    sections
        .flat()
        .map((item) => item.menuRow)
        .find((row) => row?.id === "layer");

const getLayerRow = (canvasElement: HTMLElement): IControlMenuCommandRow => {
    const row = findLayerRow(menuSectionsFor(canvasElement));
    if (!row) {
        throw new Error("expected a Layer row in the menu");
    }
    return row;
};

const subRow = (
    layerRow: IControlMenuCommandRow,
    id: string,
): IControlMenuCommandRow => {
    const found = layerRow.subMenuItems?.find((item) => item.id === id);
    if (!found) {
        throw new Error(
            `No Layer submenu item "${id}". Found: ${layerRow.subMenuItems
                ?.map((item) => item.id)
                .join(", ")}`,
        );
    }
    return found as IControlMenuCommandRow;
};

describe("Layer submenu", () => {
    afterEach(() => {
        document.body.innerHTML = "";
    });

    test("setup: the pictures are seen as image elements with the expected z-order flags", () => {
        const [bottom, middle, top] = makePageWithPictures(3);
        expect(ctxFor(middle).elementType).toBe("image");
        expect(ctxFor(middle).isBackgroundImage).toBe(false);
        expect(ctxFor(getBackgroundImage()).isBackgroundImage).toBe(true);

        expect(ctxFor(bottom).canSendBackward).toBe(false);
        expect(ctxFor(bottom).canBringForward).toBe(true);
        expect(ctxFor(middle).canSendBackward).toBe(true);
        expect(ctxFor(middle).canBringForward).toBe(true);
        expect(ctxFor(top).canSendBackward).toBe(true);
        expect(ctxFor(top).canBringForward).toBe(false);
    });

    test("every canvas element type puts the layer section right before Duplicate/Delete", () => {
        Object.values(canvasElementControlRegistry).forEach((configuration) => {
            const layerIndex = configuration.menuSections.indexOf("layer");
            expect(
                layerIndex,
                `${configuration.type} has no layer section`,
            ).toBeGreaterThanOrEqual(0);
            expect(
                configuration.menuSections[layerIndex + 1],
                `${configuration.type}: layer should come just before wholeElement`,
            ).toBe("wholeElement");
        });
    });

    test("the Layer row is its own section, immediately before the Duplicate/Delete section", () => {
        const [, middle] = makePageWithPictures(3);
        const sections = menuSectionsFor(middle);
        const layerSectionIndex = sections.findIndex((section) =>
            section.some((item) => item.menuRow?.id === "layer"),
        );
        expect(layerSectionIndex).toBeGreaterThan(0);
        expect(
            sections[layerSectionIndex].map((item) => item.menuRow?.id),
        ).toEqual(["layer"]);
        expect(
            sections[layerSectionIndex + 1].map((item) => item.menuRow?.id),
        ).toEqual(["duplicate", "delete"]);
    });

    test("the submenu has the four commands in order, with their shortcuts", () => {
        const [, middle] = makePageWithPictures(3);
        const layerRow = getLayerRow(middle);
        expect(layerRow.l10nId).toBe("EditTab.Toolbox.CanvasTool.Layer");
        expect(layerRow.englishLabel).toBe("Layer");
        expect(layerRow.icon).toBeTruthy();

        expect(layerRow.subMenuItems?.map((item) => item.id)).toEqual([
            "bringForward",
            "bringToFront",
            "sendBackward",
            "sendToBack",
        ]);
        expect(subRow(layerRow, "bringForward").shortcut?.display).toBe(
            "Ctrl+]",
        );
        expect(subRow(layerRow, "bringToFront").shortcut?.display).toBe(
            "Ctrl+Shift+]",
        );
        expect(subRow(layerRow, "sendBackward").shortcut?.display).toBe(
            "Ctrl+[",
        );
        expect(subRow(layerRow, "sendToBack").shortcut?.display).toBe(
            "Ctrl+Shift+[",
        );
        layerRow.subMenuItems?.forEach((item) => {
            expect(item.icon, `${item.id} should have an icon`).toBeTruthy();
            expect(item.l10nId).toMatch(
                /^EditTab\.Toolbox\.CanvasTool\.Layer\./,
            );
        });
    });

    test("in the middle of the stack, everything is enabled", () => {
        const [, middle] = makePageWithPictures(3);
        const layerRow = getLayerRow(middle);
        expect(layerRow.disabled).toBe(false);
        layerRow.subMenuItems?.forEach((item) => {
            expect(item.disabled, `${item.id} should be enabled`).toBe(false);
        });
    });

    test("at the top of the stack, only the backward commands are enabled", () => {
        const [, , top] = makePageWithPictures(3);
        const layerRow = getLayerRow(top);
        expect(layerRow.disabled).toBe(false);
        expect(subRow(layerRow, "bringForward").disabled).toBe(true);
        expect(subRow(layerRow, "bringToFront").disabled).toBe(true);
        expect(subRow(layerRow, "sendBackward").disabled).toBe(false);
        expect(subRow(layerRow, "sendToBack").disabled).toBe(false);
    });

    test("at the bottom of the stack (just above the background), only the forward commands are enabled", () => {
        const [bottom] = makePageWithPictures(3);
        const layerRow = getLayerRow(bottom);
        expect(layerRow.disabled).toBe(false);
        expect(subRow(layerRow, "bringForward").disabled).toBe(false);
        expect(subRow(layerRow, "bringToFront").disabled).toBe(false);
        expect(subRow(layerRow, "sendBackward").disabled).toBe(true);
        expect(subRow(layerRow, "sendToBack").disabled).toBe(true);
    });

    test("with only one movable element, the whole submenu is disabled", () => {
        const [only] = makePageWithPictures(1);
        const layerRow = getLayerRow(only);
        expect(layerRow.disabled).toBe(true);
        layerRow.subMenuItems?.forEach((item) => {
            expect(item.disabled, `${item.id} should be disabled`).toBe(true);
        });
    });

    test("the background image has no Layer row at all", () => {
        makePageWithPictures(3);
        const sections = menuSectionsFor(getBackgroundImage());
        expect(findLayerRow(sections)).toBeUndefined();
        // Sanity: the background image does still get a menu (e.g. Delete), so the absence of
        // Layer is not just an empty menu.
        expect(
            sections.flat().some((item) => item.menuRow?.id === "delete"),
        ).toBe(true);
    });
});
