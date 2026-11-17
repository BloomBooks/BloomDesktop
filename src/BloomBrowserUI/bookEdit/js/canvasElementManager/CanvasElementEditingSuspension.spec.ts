import { describe, it, expect, beforeEach } from "vitest";
import {
    CanvasElementEditingSuspension,
    ICanvasElementEditingSuspensionHost,
} from "./CanvasElementEditingSuspension";
import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";

// A game page's tabs work whatever tool is open, so a game's Play mode must survive tools that
// resume comic editing when they close, and the game tabs must end only Play's suspension.

let editingOn: boolean;
let suspension: CanvasElementEditingSuspension;
let canvasText: HTMLElement;

function makeHost(): ICanvasElementEditingSuspensionHost {
    return {
        getIsCanvasElementEditingOn: () => editingOn,
        getAllBloomCanvasesOnPage: () => [],
        adjustBackgroundImageSize: () => {},
        adjustChildrenIfSizeChanged: () => {},
        turnOffCanvasElementEditing: () => {
            editingOn = false;
        },
        turnOnCanvasElementEditing: () => {
            editingOn = true;
        },
        setupControlFrame: () => {},
    };
}

function canvasTextIsEditable(): boolean {
    return canvasText.getAttribute("contenteditable") === "true";
}

describe("CanvasElementEditingSuspension with a game's Play mode", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        const canvasElement = document.createElement("div");
        canvasElement.classList.add(kCanvasElementClass);
        canvasText = document.createElement("div");
        canvasText.classList.add("bloom-editable");
        canvasText.setAttribute("contenteditable", "true");
        canvasElement.appendChild(canvasText);
        document.body.appendChild(canvasElement);
        editingOn = true;
        suspension = new CanvasElementEditingSuspension(makeHost());
    });

    it("only the game's tab control ends Play mode", () => {
        suspension.suspendComicEditing("forGamePlayMode");
        expect(canvasTextIsEditable(), "test setup").toBe(false);

        // e.g. the Talking Book tool closing while the page is in Play
        suspension.resumeComicEditing();
        expect(editingOn).toBe(false);
        expect(canvasTextIsEditable()).toBe(false);

        suspension.endGamePlayModeSuspension();
        expect(editingOn).toBe(true);
        expect(canvasTextIsEditable()).toBe(true);
        expect(suspension.isSuspended()).toBe(false);
    });

    it("Play leaves the text's HTML exactly as it found it, attribute order included", () => {
        // Bloom saves a page only when its HTML has changed, so moving an attribute would make a
        // page that had been played look edited.
        canvasText.setAttribute("data-languagetipcontent", "English");
        const before = canvasText.outerHTML;
        expect(before, "test setup: contenteditable comes first").toMatch(
            /contenteditable="true" data-languagetipcontent/,
        );

        suspension.suspendComicEditing("forGamePlayMode");
        suspension.endGamePlayModeSuspension();

        expect(canvasText.outerHTML).toBe(before);
    });

    it("choosing a game tab other than Play does not end a tool's suspension", () => {
        suspension.suspendComicEditing("forTool"); // e.g. image descriptions shown
        suspension.endGamePlayModeSuspension(); // e.g. the Start tab chosen
        expect(editingOn).toBe(false);
        expect(suspension.isSuspended()).toBe(true);

        suspension.resumeComicEditing(); // the tool is done
        expect(editingOn).toBe(true);
    });
});
