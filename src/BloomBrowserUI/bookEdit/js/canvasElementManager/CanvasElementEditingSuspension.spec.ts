import { describe, it, expect, beforeEach } from "vitest";
import {
    CanvasElementEditingSuspension,
    ICanvasElementEditingSuspensionHost,
} from "./CanvasElementEditingSuspension";
import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";

// A game page's tabs work whatever tool is open, so a game's Play mode must survive tools that
// resume canvas element editing when they close, and the game tabs must end only Play's suspension.

let editingOn: boolean;
let suspension: CanvasElementEditingSuspension;
let canvasText: HTMLElement;
let otherText: HTMLElement; // a text box that isn't in a canvas element
let readOnlyText: HTMLElement; // e.g. the original license in a translated shellbook

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
        const page = document.createElement("div");
        page.classList.add("bloom-page");
        const canvasElement = document.createElement("div");
        canvasElement.classList.add(kCanvasElementClass);
        canvasText = document.createElement("div");
        canvasText.classList.add("bloom-editable");
        canvasText.setAttribute("contenteditable", "true");
        canvasElement.appendChild(canvasText);
        page.appendChild(canvasElement);
        otherText = document.createElement("div");
        otherText.classList.add("bloom-editable");
        otherText.setAttribute("contenteditable", "true");
        page.appendChild(otherText);
        readOnlyText = document.createElement("div");
        readOnlyText.classList.add("bloom-editable");
        readOnlyText.setAttribute("readonly", "true");
        page.appendChild(readOnlyText);
        document.body.appendChild(page);
        editingOn = true;
        suspension = new CanvasElementEditingSuspension(makeHost());
    });

    it("only the game's tab control ends Play mode", () => {
        suspension.suspendCanvasElementEditing("forGamePlayMode");
        expect(canvasTextIsEditable(), "test setup").toBe(false);

        // e.g. the Talking Book tool closing while the page is in Play
        suspension.resumeCanvasElementEditing();
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

        suspension.suspendCanvasElementEditing("forGamePlayMode");
        suspension.endGamePlayModeSuspension();

        expect(canvasText.outerHTML).toBe(before);
    });

    it("Play makes all the page's text uneditable, and only what was editable editable again", () => {
        suspension.suspendCanvasElementEditing("forGamePlayMode");
        expect(otherText.getAttribute("contenteditable")).toBe("false");
        expect(
            readOnlyText.hasAttribute("contenteditable"),
            "a read-only field is left as it is",
        ).toBe(false);

        suspension.endGamePlayModeSuspension();
        expect(otherText.getAttribute("contenteditable")).toBe("true");
        expect(
            readOnlyText.hasAttribute("contenteditable"),
            "Play ending doesn't make a read-only field editable",
        ).toBe(false);
    });

    it("choosing a game tab other than Play does not end a tool's suspension", () => {
        suspension.suspendCanvasElementEditing("forTool"); // e.g. image descriptions shown
        suspension.endGamePlayModeSuspension(); // e.g. the Start tab chosen
        expect(editingOn).toBe(false);
        expect(suspension.isSuspended()).toBe(true);

        suspension.resumeCanvasElementEditing(); // the tool is done
        expect(editingOn).toBe(true);
    });
});
