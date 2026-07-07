import { act } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import { NewerVersionAvailableMarker } from "./NewerVersionAvailableMarker";

// Tests the Wave-2 "newer version exists" book-thumbnail marker in isolation: it's a pure
// function of its `show` prop (the gating/version-comparison logic lives in BookButton.tsx,
// which composes this marker the same way it already composes BookOnBlorgBadge).

let renderedContainer: HTMLDivElement | undefined;

function render(show: boolean): HTMLDivElement {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;
    act(() => {
        renderRoot(<NewerVersionAvailableMarker show={show} />, container);
    });
    return container;
}

afterEach(() => {
    if (renderedContainer) {
        unmountRoot(renderedContainer);
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    document.body.innerHTML = "";
});

describe("NewerVersionAvailableMarker", () => {
    it("renders nothing when show is false", () => {
        const container = render(false);
        expect(
            container.querySelector(
                '[data-testid="newer-version-available-marker"]',
            ),
        ).toBeNull();
    });

    it("renders the marker when show is true", () => {
        const container = render(true);
        expect(
            container.querySelector(
                '[data-testid="newer-version-available-marker"]',
            ),
        ).not.toBeNull();
    });
});
