import { describe, expect, it } from "vitest";
import { compareFolioListWithCollection } from "./folioTocBooks";

describe("compareFolioListWithCollection", () => {
    it("keeps books still in the collection, in order, with their current titles", () => {
        const collection = new Map([
            ["a", "Apples"],
            ["b", "Bees, renamed"],
            ["c", "Cats"],
        ]);
        const result = compareFolioListWithCollection(
            ["c", "a", "b"],
            { a: "Apples", b: "Bees" },
            collection,
        );
        expect(result.ids).toEqual(["c", "a", "b"]);
        expect(result.titles).toEqual({
            c: "Cats",
            a: "Apples",
            b: "Bees, renamed",
        });
        expect(result.removed).toEqual([]);
    });

    it("removes a book no longer in the collection and names it by its stored title", () => {
        const collection = new Map([["a", "Apples"]]);
        expect(collection.has("gone")).toBe(false);
        const result = compareFolioListWithCollection(
            ["gone", "a"],
            { gone: "Gone Fishing", a: "Apples" },
            collection,
        );
        expect(result.ids).toEqual(["a"]);
        expect(result.titles).toEqual({ a: "Apples" });
        expect(result.removed).toEqual(["Gone Fishing"]);
    });

    it("names a removed book by its id when no title was stored", () => {
        const result = compareFolioListWithCollection(["gone"], {}, new Map());
        expect(result.ids).toEqual([]);
        expect(result.removed).toEqual(["gone"]);
    });
});
