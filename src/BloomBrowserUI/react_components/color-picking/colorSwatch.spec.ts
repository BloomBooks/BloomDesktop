import { describe, it, expect } from "vitest";
import { normalizeColorInfoColorsAsHex, IColorInfo } from "./colorSwatch";

describe("ColorSwatch tests", () => {
    it("normalizeColorInfoAsHex", () => {
        const colorInfoWithRgba: IColorInfo = {
            colors: ["rgba (255, 0, 0, .5)"],
            opacity: 50,
        };
        normalizeColorInfoColorsAsHex(colorInfoWithRgba);

        let expected: IColorInfo = { colors: ["#ff0000"], opacity: 50 };
        expect(colorInfoWithRgba.colors[0]).toEqual(expected.colors[0]);
        expect(colorInfoWithRgba.colors[1]).toEqual(expected.colors[1]);
        expect(colorInfoWithRgba.opacity).toEqual(expected.opacity);

        colorInfoWithRgba.colors[1] = "blue";
        normalizeColorInfoColorsAsHex(colorInfoWithRgba);

        expected = { colors: ["#ff0000", "#0000ff"], opacity: 50 };
        expect(colorInfoWithRgba.colors[0]).toEqual(expected.colors[0]);
        expect(colorInfoWithRgba.colors[1]).toEqual(expected.colors[1]);
        expect(colorInfoWithRgba.opacity).toEqual(expected.opacity);
    });
});
