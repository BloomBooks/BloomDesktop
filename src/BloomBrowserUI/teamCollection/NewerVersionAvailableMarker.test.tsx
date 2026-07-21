import { describe, expect, it } from "vitest";
import { renderTestRoot } from "../utils/testRender";
import { NewerVersionAvailableMarker } from "./NewerVersionAvailableMarker";

// Tests the Wave-2 "newer version exists" book-thumbnail marker in isolation: it's a pure
// function of its `show` prop (the gating/version-comparison logic lives in BookButton.tsx,
// which composes this marker the same way it already composes BookOnBlorgBadge).

function render(show: boolean): HTMLDivElement {
    return renderTestRoot(<NewerVersionAvailableMarker show={show} />);
}

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
