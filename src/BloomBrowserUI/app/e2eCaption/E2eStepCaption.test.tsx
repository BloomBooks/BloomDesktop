import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import * as React from "react";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";

// The one thing the component asks the server: whether this Bloom is running under --e2e. Each
// test sets the answer before rendering.
let runningE2eTests = false;
vi.mock("../../utils/bloomApi", () => ({
    useApiObject: () => ({ runningE2eTests }),
}));

import { E2eStepCaption } from "./E2eStepCaption";

let container: HTMLDivElement;
let root: Root;

const render = () => {
    act(() => {
        root.render(React.createElement(E2eStepCaption));
    });
};

const caption = () =>
    container.querySelector('[data-testid="e2e-step-caption"]');

beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
    root = createRoot(container);
});

afterEach(() => {
    act(() => root.unmount());
    container.remove();
    delete window.bloomE2eCaption;
});

describe("E2eStepCaption", () => {
    it("is not in the DOM, and installs nothing on window, in a normal run", () => {
        runningE2eTests = false;
        render();
        expect(caption()).toBeNull();
        expect(window.bloomE2eCaption).toBeUndefined();
    });

    it("stays out of the DOM under --e2e until a test begins", () => {
        runningE2eTests = true;
        render();
        // Sanity check: the API is there to be called, but nothing is drawn yet.
        expect(window.bloomE2eCaption).toBeDefined();
        expect(caption()).toBeNull();
    });

    it("shows the test, then its steps, and never intercepts a click", () => {
        runningE2eTests = true;
        render();
        act(() => {
            window.bloomE2eCaption!.begin("tables-core", "add a table");
        });
        const strip = caption();
        if (!strip) throw new Error("The caption did not appear.");
        expect(strip.textContent).toContain("tables-core");
        expect(strip.textContent).toContain("add a table");
        expect(getComputedStyle(strip).pointerEvents).toBe("none");

        act(() => {
            window.bloomE2eCaption!.step("Open the toolbox");
        });
        expect(caption()!.textContent).toContain("Open the toolbox");
        // The step counter, with no total because the test side did not know one.
        expect(caption()!.textContent).toContain("1 ·");

        act(() => {
            window.bloomE2eCaption!.end("passed", 2100);
            window.bloomE2eCaption!.step("Type into a cell");
        });
        expect(caption()!.textContent).toContain("2.1s");
        expect(caption()!.textContent).toContain("Type into a cell");
    });

    it("shows only the last three steps", () => {
        runningE2eTests = true;
        render();
        act(() => {
            window.bloomE2eCaption!.begin("s", "t");
            ["one", "two", "three", "four"].forEach((title) => {
                window.bloomE2eCaption!.step(title);
                window.bloomE2eCaption!.end("passed", 100);
            });
        });
        expect(caption()!.textContent).not.toContain("one");
        expect(caption()!.textContent).toContain("four");
    });

    it("takes its API back off window when it unmounts", () => {
        runningE2eTests = true;
        render();
        expect(window.bloomE2eCaption).toBeDefined();
        act(() => root.unmount());
        expect(window.bloomE2eCaption).toBeUndefined();
        // The afterEach unmount must still be safe.
        root = createRoot(container);
    });
});
