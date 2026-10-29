import { describe, expect, it } from "vitest";
import { administratorsAreValid } from "./administrators";

describe("administratorsAreValid", () => {
    it("accepts one address", () => {
        expect(administratorsAreValid("someone@example.com")).toBe(true);
    });

    it("accepts addresses separated by commas, spaces, or both", () => {
        expect(
            administratorsAreValid(
                "a@example.com,b@example.com c@example.com,  d@example.com",
            ),
        ).toBe(true);
    });

    it("refuses an empty list", () => {
        expect(administratorsAreValid("")).toBe(false);
        expect(administratorsAreValid(" , ")).toBe(false);
    });

    it("refuses a list with any address that is not valid", () => {
        expect(administratorsAreValid("a@example.com, not-an-address")).toBe(
            false,
        );
    });
});
