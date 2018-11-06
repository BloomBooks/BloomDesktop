import {
    ImageDescriptionAdapter,
    DraggablePositioningInfo
} from "./imageDescription";
import { remove } from "mobx";

describe("image description tests", () => {
    it("parses style position text", () => {
        // Format: imageNaturalWidth, imageNaturalHeight, containerWidth, containerHeight, expectedResultWidth, expectedResultHeight
        const testCases = [];
        testCases.push(["50%", 50, "%"]);
        testCases.push(["1.23%", 1.23, "%"]);
        testCases.push(["-1.23%", -1.23, "%"]);
        testCases.push(["-1.23px", -1.23, "px"]);
        testCases.push(["-1.23 px", -1.23, "px"]);
        for (let i = 0; i < testCases.length; ++i) {
            const testInput = testCases[i][0];
            const expectedValue = testCases[i][1];
            const expectedUnit = testCases[i][2];

            const result = ImageDescriptionAdapter.parseNumberAndUnit(
                testInput
            );
            expect(result.num).toBe(
                expectedValue,
                "Test Case #" + (i + 1) + ": value"
            );
            expect(result.unit).toBe(
                expectedUnit,
                "Test Case #" + (i + 1) + ": unit"
            );
        }
    });

    it("determines true image dimensions within bounding box correctly", () => {
        const precision: number = 1;

        const adapter = new ImageDescriptionAdapter();

        // Format: imageNaturalWidth, imageNaturalHeight, containerWidth, containerHeight, expectedResultWidth, expectedResultHeight
        const testCases: number[][] = [];

        testCases.push([400, 400, 400, 400, 400, 400]); // Perfect fit
        testCases.push([400, 300, 200, 400, 200, 150]); // Only X doesn't fit
        testCases.push([100, 500, 200, 400, 80, 400]); // Only Y doesn't fit
        testCases.push([600, 800, 200, 400, 200, 800 / 3.0]); // Neither fits
        testCases.push([1000, 387, 334, 401, 334, 129.258]); // Regression test case 1A
        testCases.push([1000, 387, 314, 200.5, 314, 121.518]); // Regression test case 1B
        testCases.push([200, 314, 400, 334, 212.7, 334]); // Regression test case 2A
        testCases.push([200, 314, 200, 314, 200, 314]); // Regression test case 2A

        testCases.push([100, 100, 401, 334, 334, 334]); // upscaling test 1A
        testCases.push([100, 100, 200.5, 314, 200.5, 200.5]); // upscaling test 1B

        for (let i = 0; i < testCases.length; ++i) {
            const imageNaturalWidth = testCases[i][0];
            const imageNaturalHeight = testCases[i][1];
            const boundingWidth = testCases[i][2];
            const boundingHeight = testCases[i][3];
            const expectedWidth = testCases[i][4];
            const expectedHeight = testCases[i][5];

            const result = adapter.getTrueImageDimensionsInBoundingBox(
                imageNaturalWidth,
                imageNaturalHeight,
                boundingWidth,
                boundingHeight
            );
            expect(result.trueWidth).toBeLessThan(
                boundingWidth + 1,
                "Test Case #" + (i + 1) + ": does not fit into width"
            );
            expect(result.trueHeight).toBeLessThan(
                boundingHeight + 1,
                "Test Case #" + (i + 1) + ": does not fit into height"
            );
            expect(result.trueWidth).toBeCloseTo(
                expectedWidth,
                precision,
                "Test Case #" + (i + 1) + ": width"
            );
            expect(result.trueHeight).toBeCloseTo(
                expectedHeight,
                precision,
                "Test Case #" + (i + 1) + ": height"
            );
        }
    });

    it("shrinks element correctly", () => {
        const precision: number = 1; // This is the number of decimal digits to round to... I think.

        const adapter = new ImageDescriptionAdapter();

        const testCases = [];
        testCases.push(["Undersized Square", 100, 100, "175px", "0px", 85, 77]);
        // Worked example #1:
        // Upscale to 334x334
        // Image X: 400/2 +- 334/2 = 33 to 367
        // Text box was at 175. Relative position = (175-33)/334 = 0.425
        // New boundary = 0 to 200.
        // new position = 0 + .425*(200-0) = 85. This is the theoretical top-left.
        //
        // Image Y: Now 200x200.
        // Center at 314/2 = 157.  57 to 257.  Then take in 20 pixel offset = 77
        // 77 - 40*(1-0.60)/2 = 69
        //
        // scaling factor = 200/334 =0.60

        testCases.push(["Exact fit", 200, 314, "150px", "0px", 53.0, 20, 0.94]);
        // Worked example #2
        // FYI, it is not exactly at 50.0 because the image is upscaled to fit the original 400x334 box.
        // Upscaled to 212.74 x 334
        // Image X: 200+-212.74/2.  (93.63, 306.37)
        // Text box was at 150.   (150-93.63) / 212.74 = 0.265
        // Now image goes from 0 to 200.
        // Text box should be at 0 + 0.265*200 = 53
        // Now apply effects of scaling:
        //  53 - 100*(1-0.94)/2 = 50

        testCases.push([
            "Landscape too wide",
            1000,
            287,
            "150px",
            "100px",
            75,
            143.5
        ]);
        testCases.push([
            "Landscape both exceed",
            800,
            600,
            "150px",
            "100px",
            75,
            143.5
        ]);
        testCases.push([
            "Portrait too tall",
            200,
            400,
            "150px",
            "100px",
            53,
            114
        ]);
        testCases.push([
            "Portrait both exceed",
            600,
            800,
            "150px",
            "100px",
            60.1,
            123.5
        ]);
        testCases.push(["Top left corner", 200, 314, "0px", "0px", -88, 20]);

        for (let i = 0; i < testCases.length; ++i) {
            const testCase = testCases[i];
            let argIndex: number = 0;
            const testId: string = testCase[argIndex++];
            const imageNaturalWidth: number = testCase[argIndex++];
            const imageNaturalHeight: number = testCase[argIndex++];
            const textBoxStyleLeft: string = testCase[argIndex++];
            const textBoxStyleTop: string = testCase[argIndex++];
            const expectedLeft: number = testCase[argIndex++];
            const expectedTop: number = testCase[argIndex++];

            // Mock up the HTMLElements using some mock classes instead, so that we can assign to some properties which are read-only
            const parentElement: HTMLElementMock = new HTMLElementMock();
            parentElement.clientWidth = 400;
            parentElement.clientHeight = 334;

            const imageElement: HTMLElementMock = new HTMLElementMock();
            imageElement.clientWidth = 200;
            imageElement.clientHeight = 314;
            imageElement.naturalWidth = imageNaturalWidth;
            imageElement.naturalHeight = imageNaturalHeight;

            const draggableElement: HTMLElementMock = new HTMLElementMock();
            draggableElement.parentElement = parentElement;
            draggableElement.clientWidth = 100;
            draggableElement.clientHeight = 40;
            draggableElement.style.left = textBoxStyleLeft;
            draggableElement.style.top = textBoxStyleTop;
            draggableElement.style.width = draggableElement.clientWidth + "px";
            draggableElement.style.height =
                draggableElement.clientHeight + "px";
            draggableElement.style.fontSize = "1em";

            adapter.shrinkDraggableForImageDescription(
                draggableElement as any,
                imageElement as any
            ); // Rely on duck typing to save us as we pass in a not-a-real-html-element

            expect(getNumber(draggableElement.style.left)).toBeCloseTo(
                expectedLeft,
                precision,
                "Test Case #" + (i + 1) + " (" + testId + "): Style left"
            );
            expect(getNumber(draggableElement.style.top)).toBeCloseTo(
                expectedTop,
                precision,
                "Test Case #" + (i + 1) + " (" + testId + "): Style top"
            );

            // // Manual adapter verification
            // // 85 is in theory the right answer.
            // const deltaWidth = (1-expectedScalingFactor) * draggableElement2.clientWidth/2;
            // const deltaHeight = (1-expectedScalingFactor) * draggableElement2.clientHeight/2;
            // expect(getNumber(draggableElement2.style.left)).toBeCloseTo(
            //     expectedLeft + deltaWidth,
            //     0,
            //     "Test Case #" + (i + 1) + " (" + testId + "): Manual Style left"
            // );
            // expect(getNumber(draggableElement2.style.top)).toBeCloseTo(
            //     expectedTop + deltaHeight,
            //     0,
            //     "Test Case #" + (i + 1) + " (" + testId + "): Manual Style top"
            // );
            // // TODO: verify height and font-size and stuff
            // //expect(draggableElement.style.height
        }
    });

    it("unshrinks element that hasn't moved", () => {
        // Setup
        const element: HTMLElementMock = new HTMLElementMock();
        setupDefaultUnshrunkenImage(element);

        const originalPos: DraggablePositioningInfo = new DraggablePositioningInfo();

        element.style.left = originalPos.lastLeft = "2.718%";
        element.style.top = originalPos.lastTop = "3.14%";
        element.style.transform = originalPos.lastTransform = "scale(0.5, 0.5)";

        originalPos.left = "1.23%";
        originalPos.top = "4.56%";
        originalPos.transform = "";

        // System under test
        const adapter = new ImageDescriptionAdapter();
        adapter.unshrinkDraggableForImageDescription(
            element as any,
            originalPos
        );

        // Verification
        expect(element.style.left).toBe(originalPos.left, "Left");
        expect(element.style.top).toBe(originalPos.top, "Top");
    });

    it("unshrinks element that has moved", () => {
        const precision = 1;

        // Setup
        const element: HTMLElementMock = new HTMLElementMock();
        element.style.left = "85px";
        element.style.top = "77px";
        element.style.transform = "scale(0.6, 0.6)";
        element.clientWidth = 100;
        element.clientHeight = 40;

        setupDefaultUnshrunkenImage(element);

        const originalPos: DraggablePositioningInfo = new DraggablePositioningInfo();

        originalPos.lastLeft = "2.718%";
        originalPos.lastTop = "3.14%";
        originalPos.lastImageClientWidth = 200;
        originalPos.lastImageClientHeight = 314;

        originalPos.left = "175px";
        originalPos.top = "0px";
        originalPos.transform = "";

        // System under test
        const adapter = new ImageDescriptionAdapter();
        adapter.unshrinkDraggableForImageDescription(
            element as any,
            originalPos
        );

        // Verification
        expect(getNumber(element.style.left)).toBeCloseTo(
            getNumber(originalPos.left),
            precision,
            "Left"
        );
        expect(getNumber(element.style.top)).toBeCloseTo(
            getNumber(originalPos.top),
            precision,
            "Top"
        );
    });

    // TODO: need test for transformForScaling false
});

// Extracts only the number component from a string containing both a numeric value and a unit
function getNumber(text: string) {
    const { num, unit } = ImageDescriptionAdapter.parseNumberAndUnit(text);
    return num;
}

// Extracts the numeric value of the scaling factor from a scaling transform string
// Precondition: format is scale(x, y), and x == y
function getScalingFactor(text: string) {
    if (!text) {
        return 1.0;
    }

    text = text.substring(6);
    let index = text.indexOf(",");
    if (index < 0) {
        index = text.indexOf(")");

        if (index < 0) {
            return Number(text);
        }
    }

    return text.substring(0, index);
}

function setupDefaultUnshrunkenImage(element) {
    const imageElement = new HTMLElementMock();
    imageElement.clientWidth = 400;
    imageElement.clientHeight = 334;
    imageElement.naturalWidth = 100;
    imageElement.naturalHeight = 100;

    if (element) {
        if (!element.parentElement) {
            element.parentElement = new HTMLElementMock();
            element.parentElement.clientWidth = 400;
            element.parentElement.clientHeight = 334;
        }
        element.parentElement.searchResultElement = imageElement;
    }
    return imageElement;
}

// Used to provide write access for test setup to some fields of HTMLElement which are read-only
class HTMLElementMock {
    public clientWidth;
    public clientHeight;
    public naturalWidth;
    public naturalHeight;
    public classList = new ClassList();

    public style = new StyleMock();

    public parentElement;

    public searchResultElement;

    public getElementsByTagName(tag: string) {
        return [this.searchResultElement];
    }

    public getElementsByClassName() {
        return [];
    }
}

class StyleMock {
    public top: string;
    public left: string;
    public transform: string;
    public height: string;
    public width: string;
    public fontSize: string;
}

// Stub class
class ClassList {
    public add(classToRemove: string) {}
    public remove(classToRemove: string) {}
    public contains(classToRemove: string): boolean {
        return false;
    }
}
