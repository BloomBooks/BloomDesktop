import { describe, it, expect, beforeEach } from "vitest";
import { MeasureText } from "./measureText";

// Runs in a real browser (the "browser" vitest project): it measures real layout and
// reads canvas pixels.
describe("MeasureText.getDescentMeasurementsOfBox", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    function makeBox(html: string, fontSize: number): HTMLElement {
        const box = document.createElement("div");
        box.style.fontFamily = "Arial";
        box.style.fontSize = `${fontSize}px`;
        box.innerHTML = html;
        document.body.appendChild(box);
        return box;
    }

    // Measures a box the first time MeasureText ever runs in this document,
    // so no earlier measurement can affect the result.
    function measureFresh(html: string, fontSize: number) {
        document.getElementById("measureTextDiv")?.remove();
        return MeasureText.getDescentMeasurementsOfBox(makeBox(html, fontSize));
    }

    it("measures a font's descent as a small part of its line", () => {
        const result = measureFresh("Test texty", 24);
        expect(result.fontDescent).toBeGreaterThan(0);
        expect(result.fontDescent).toBeLessThan(12);
        expect(result.actualDescent).toBeGreaterThan(0); // the "y" has a descender
    });

    it("is not thrown off by text that starts with whitespace", () => {
        const expected = measureFresh("Test texty", 24);
        expect(measureFresh("\n    Test texty\n", 24)).toEqual(expected);
    });

    // BL-16925: the measuring div is reused between calls. Measuring text that started
    // with a newline used to leave a <br> in it, and every later measurement then came
    // out about a line too big, which could hide real overflow.
    it.each([
        { what: "starts with a newline", html: "\n    Some text" },
        { what: "is empty", html: "" },
        { what: "is just whitespace", html: "\n    " },
    ])(
        "is not thrown off by having just measured a box whose text $what",
        (c) => {
            const expected = measureFresh("Test texty", 24);
            document.getElementById("measureTextDiv")?.remove();
            MeasureText.getDescentMeasurementsOfBox(makeBox(c.html, 16));
            expect(document.getElementById("measureTextDiv")).not.toBeNull(); // sanity: it's reused
            expect(
                MeasureText.getDescentMeasurementsOfBox(
                    makeBox("Test texty", 24),
                ),
            ).toEqual(expected);
        },
    );
});
