import { describe, expect, test } from "vitest";

import { getPersistedCanvasColor } from "./canvasColorUtils";

describe("getPersistedCanvasColor", () => {
    test("keeps opaque colors unchanged", () => {
        expect(
            getPersistedCanvasColor({
                colors: ["#123456"],
                opacity: 1,
            }),
        ).toBe("#123456");
    });

    test("converts partial opacity to rgba for persistence", () => {
        expect(
            getPersistedCanvasColor({
                colors: ["#123456"],
                opacity: 0.25,
            }),
        ).toBe("rgba(18, 52, 86, 0.25)");
    });
});
