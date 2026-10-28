import { describe, expect, it } from "vitest";
import { isKmwAttached, setKmwAttached } from "./longPressShared";

describe("longPressShared KMW-attachment tracking", () => {
    it("reports an element as attached only after setKmwAttached is called on it", () => {
        const editable = document.createElement("div");
        expect(isKmwAttached(editable)).toBe(false);

        setKmwAttached(editable);

        expect(isKmwAttached(editable)).toBe(true);
    });

    it("does not consider an unrelated element attached", () => {
        const attached = document.createElement("div");
        const other = document.createElement("div");
        setKmwAttached(attached);

        expect(isKmwAttached(other)).toBe(false);
    });

    it("treats null/absent targets as not attached", () => {
        expect(isKmwAttached(null)).toBe(false);
    });
});
