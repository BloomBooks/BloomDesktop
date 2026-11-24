import { describe, it, expect } from "vitest";
import {
    preparePageForPlay,
    removePlayModeMarkupFromClone,
    takePageOutOfPlayMode,
} from "./gamePlayMode";

// In the browser project because jsdom does not support the touch-action style property, so there
// play mode cannot set it and this could not tell whether it is removed.

describe("play mode and touch-action", () => {
    it("does not save the touch-action that play mode puts on draggables", () => {
        document.body.innerHTML = "";
        const page = document.createElement("div");
        page.classList.add("bloom-page");
        page.setAttribute("data-activity", "drag-letter-to-target");
        const draggable = document.createElement("div");
        draggable.setAttribute("data-draggable-id", "d1");
        draggable.style.left = "10px";
        draggable.style.top = "20px";
        page.appendChild(draggable);
        document.body.appendChild(page);

        preparePageForPlay(page);
        expect(
            draggable.style.getPropertyValue("touch-action"),
            "test setup: play mode sets touch-action",
        ).toBe("none");

        const copy = page.cloneNode(true) as HTMLElement;
        removePlayModeMarkupFromClone(copy);
        const saved = copy.querySelector<HTMLElement>("[data-draggable-id]")!;
        expect(saved.style.getPropertyValue("touch-action")).toBe("");
        expect(saved.style.left, "authored styles stay").toBe("10px");
        expect(
            draggable.style.getPropertyValue("touch-action"),
            "the live page goes on being played",
        ).toBe("none");

        takePageOutOfPlayMode(page);
        expect(draggable.style.getPropertyValue("touch-action")).toBe("");
    });
});
