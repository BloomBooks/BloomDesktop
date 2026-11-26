import { beforeEach, describe, expect, it, vi } from "vitest";

// Image descriptions never show while a game page is in Play: showImageDescriptions does nothing
// then, not even suspend canvas element editing, which Play's own suspension owns.

const mocks = vi.hoisted(() => ({
    inPlay: false,
    suspendCanvasElementEditing: vi.fn(),
    redrawGameTabs: vi.fn(),
}));

vi.mock("../canvas/canvasElementPageBridge", () => ({
    getCanvasElementManager: () => ({
        suspendCanvasElementEditing: mocks.suspendCanvasElementEditing,
    }),
}));

vi.mock("../../js/workspaceFrames", () => ({
    getToolboxBundleExports: () => ({
        isGamePageInPlay: () => mocks.inPlay,
        redrawGameTabs: mocks.redrawGameTabs,
    }),
}));

import {
    areImageDescriptionsShowing,
    showImageDescriptions,
} from "./imageDescriptionUtils";

describe("showImageDescriptions", () => {
    let body: HTMLElement;

    beforeEach(() => {
        mocks.suspendCanvasElementEditing.mockClear();
        mocks.redrawGameTabs.mockClear();
        body = document.createElement("body");
        const canvas = document.createElement("div");
        canvas.classList.add("bloom-canvas");
        canvas.appendChild(document.createElement("img"));
        body.appendChild(canvas);
    });

    it("shows nothing and suspends nothing while a game page is in Play", () => {
        mocks.inPlay = true;
        showImageDescriptions(body);
        expect(areImageDescriptionsShowing(body)).toBe(false);
        expect(body.getElementsByClassName("bloom-describedImage").length).toBe(
            0,
        );
        expect(mocks.suspendCanvasElementEditing).not.toHaveBeenCalled();
    });

    it("shows them, suspending canvas element editing, when no game is in Play", () => {
        mocks.inPlay = false;
        showImageDescriptions(body);
        expect(areImageDescriptionsShowing(body)).toBe(true);
        expect(body.getElementsByClassName("bloom-describedImage").length).toBe(
            1,
        );
        expect(mocks.suspendCanvasElementEditing).toHaveBeenCalledWith(
            "forTool",
        );
        expect(mocks.redrawGameTabs).toHaveBeenCalled();
    });
});
