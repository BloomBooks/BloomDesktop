import { describe, it, expect, beforeEach } from "vitest";
import {
    CanvasElementEditingSuspension,
    ICanvasElementEditingSuspensionHost,
} from "./CanvasElementEditingSuspension";
import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";

// A game page can be played with any tool open, so a game's Play mode and a tool's suspension of
// comic editing (the Talking Book tool showing image descriptions, say) overlap, in either order.
// Neither may end the other's.

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

        // e.g. the Talking Book tool detaching while the page is in Play
        suspension.resumeComicEditing();
        expect(editingOn).toBe(false);
        expect(canvasTextIsEditable()).toBe(false);

        suspension.endGamePlayModeSuspension();
        expect(editingOn).toBe(true);
        expect(canvasTextIsEditable()).toBe(true);
        expect(suspension.isSuspended()).toBe(false);
    });

    it("ending Play mode does not end a suspension that came before it", () => {
        suspension.suspendComicEditing("forTool"); // image descriptions shown
        suspension.suspendComicEditing("forGamePlayMode");
        expect(canvasTextIsEditable(), "Play mode takes effect").toBe(false);

        suspension.endGamePlayModeSuspension();
        expect(editingOn, "the tool still has editing suspended").toBe(false);
        expect(canvasTextIsEditable()).toBe(true);

        suspension.resumeComicEditing(); // the tool is done
        expect(editingOn).toBe(true);
        expect(suspension.isSuspended()).toBe(false);
    });

    it("ending Play mode does not end a suspension that came during it", () => {
        suspension.suspendComicEditing("forGamePlayMode");
        suspension.suspendComicEditing("forTool");

        suspension.endGamePlayModeSuspension();
        expect(editingOn, "the tool still has editing suspended").toBe(false);
        expect(suspension.isSuspended()).toBe(true);

        suspension.resumeComicEditing();
        expect(editingOn).toBe(true);
    });

    it("a tool that resumes during Play mode is not suspended once Play ends", () => {
        suspension.suspendComicEditing("forTool");
        suspension.suspendComicEditing("forGamePlayMode");
        suspension.resumeComicEditing(); // the tool is done, but Play goes on
        expect(editingOn).toBe(false);

        suspension.endGamePlayModeSuspension();
        expect(editingOn).toBe(true);
        expect(suspension.isSuspended()).toBe(false);
    });

    it("leaving a tab other than Play does not end a tool's suspension", () => {
        suspension.suspendComicEditing("forTool");
        suspension.endGamePlayModeSuspension(); // e.g. the Start tab chosen
        expect(editingOn).toBe(false);
        expect(suspension.isSuspended()).toBe(true);
    });
});
