import { describe, expect, it } from "vitest";

import { hideColorPickerDialog } from "./colorPickerDialog";

describe("colorPickerDialog", () => {
    it("does not throw if hide is called before show", () => {
        expect(() => hideColorPickerDialog()).not.toThrow();
    });
});
