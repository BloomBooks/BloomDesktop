import { describe, expect, it } from "vitest";
import {
    getColorInfoFromHexCodeChange,
    isCompleteHexColorInput,
} from "./hexColorInput";

describe("hex color input", () => {
    it("treats 6-digit hex as complete when opacity is enabled", () => {
        expect(isCompleteHexColorInput("#123456", true)).toBe(true);
    });

    it("treats 6-digit hex as fully opaque", () => {
        expect(getColorInfoFromHexCodeChange("#123456", true)).toEqual({
            colors: ["#123456"],
            opacity: 1,
        });
    });

    it("preserves explicit alpha from 8-digit hex", () => {
        expect(getColorInfoFromHexCodeChange("#12345680", true)).toEqual({
            colors: ["#123456"],
            opacity: 128 / 255,
        });
    });

    it.each(["#123456", "#12345680"])(
        "treats %s as complete when opacity is enabled",
        (testHex) => {
            expect(isCompleteHexColorInput(testHex, true)).toBe(true);
        },
    );
});
