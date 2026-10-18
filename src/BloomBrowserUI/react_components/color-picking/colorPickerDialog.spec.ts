import { describe, expect, it, vi } from "vitest";

import {
    hideColorPickerDialog,
    IColorPickerDialogProps,
    showColorPickerDialog,
} from "./colorPickerDialog";

describe("colorPickerDialog", () => {
    it("does not throw if hide is called before show", () => {
        expect(() => hideColorPickerDialog()).not.toThrow();
    });

    it("passes a failure to render the picker on to the caller of show", () => {
        // React reports the render error on the console as well; keep that out of the output.
        const consoleError = vi
            .spyOn(console, "error")
            .mockImplementation(() => {});
        const container = document.createElement("div");
        document.body.appendChild(container);
        // With no initial colour, the picker fails as it renders.
        const props = {
            localizedTitle: "Test",
            palette: "test",
            onChange: () => {},
            onInputFocus: () => {},
        } as unknown as IColorPickerDialogProps;
        try {
            expect(() => showColorPickerDialog(props, container)).toThrow();
        } finally {
            container.remove();
            consoleError.mockRestore();
        }
    });
});
