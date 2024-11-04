import { getTestRoot, removeTestRoot } from "../../utils/testHelper";
import { BubbleManager } from "./bubbleManager";

// A (currently very incomplete) set of tests for BubbleManager.

describe("BubbleManager.getLabeledNumber", () => {
    // beforeEach(() => {
    // });

    // // Politely clean up for the next test suite.
    // afterAll(removeTestRoot);

    it("extracts integer size from style", () => {
        const result = BubbleManager.getLabeledNumberInPx(
            "width",
            "left: 224px; top: 79.6px; width: 66px; height: 30px;"
        );
        expect(result).toBe(66);
    });

    it("extracts float size from style", () => {
        const result = BubbleManager.getLabeledNumberInPx(
            "top",
            "left: 224px; top: 79.6px; width: 66px; height: 30px;"
        );
        expect(result).toBe(79.6);
    });
    it("extracts negative size from style", () => {
        const result = BubbleManager.getLabeledNumberInPx(
            "left",
            "left: -10.4px; top: 79.6px; width: 66px; height: 30px;"
        );
        expect(result).toBe(-10.4);
    });
});

describe("BubbleManager.adjustLabeledNumber", () => {
    // beforeEach(() => {
    // });

    // // Politely clean up for the next test suite.
    // afterAll(removeTestRoot);

    it("adjusts center of text box", () => {
        const result = BubbleManager.adjustCenterOfTextBox(
            "left",
            "left: 30px; top: 79.6px; width: 66px; height: 30px;",
            2, // making stuff 2x larger
            7, // the old area being resized was 7px from the left
            13, // the new area is 13px from the left
            20 // the object we're adjusting is 20px wide
        );
        // So, the point we're adjusting is the center, originally 40px from the container left
        // That's 33px from the left of the area being scaled, which becomes 66px
        // from the left of the new area, which is now 13px from the container left,
        // which we add to get 79. The left is still 10px (half the width) less,
        // so we get 69
        expect(result).toBe(
            "left: 69px; top: 79.6px; width: 66px; height: 30px;"
        );
    });
});
