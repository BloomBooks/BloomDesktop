import { describe, it, expect } from "vitest";
import { add, IColorInfo, shortName } from "./testimport";

describe("Simple tests", () => {
    it("arithmetic", () => {
        expect(add(1, 3)).toBe(4);
    });
    it("string concatenation", () => {
        const output = "Hello, " + "world!";
        expect(output).toBe("Hello, world!");
    });
});

describe("ColorSwatch tests", () => {
    it("normalizeColorInfoAsHex", () => {
        const input = [];
        shortName(input);
        expect(input[0]).toBe("fake");
    });
});
