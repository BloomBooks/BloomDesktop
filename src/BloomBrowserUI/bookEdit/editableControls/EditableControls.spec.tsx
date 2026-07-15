import { afterEach, describe, expect, it, vi, beforeEach } from "vitest";
import {
    render,
    screen,
    fireEvent,
    cleanup,
    act,
} from "@testing-library/react";

// Control the keyman integration surface the component depends on. The event
// name must match the real module's constant so dispatching below reaches the
// component's listener.
vi.mock("../js/keymanWebIntegration", () => ({
    kFieldKeyboardInfoEvent: "bloom-fieldKeyboardInfo",
    getKeyboardInfoFor: vi.fn(),
    showOsk: vi.fn(),
}));

import {
    getKeyboardInfoFor,
    showOsk,
    kFieldKeyboardInfoEvent,
    FieldKeyboardInfo,
} from "../js/keymanWebIntegration";
import { EditableControls } from "./EditableControls";

const getKeyboardInfoForMock = vi.mocked(getKeyboardInfoFor);
const showOskMock = vi.mocked(showOsk);

// A bloom-editable to adorn. lang "en" resolves to "English" via the shared
// localizationManager mock in vitest.setup.ts.
function makeEditable(lang: string): HTMLElement {
    const editable = document.createElement("div");
    editable.className = "bloom-editable";
    editable.setAttribute("lang", lang);
    document.body.appendChild(editable);
    return editable;
}

const kmwInfo: FieldKeyboardInfo = {
    useKmw: true,
    keyboardId: "thai_kedmanee",
    languageTag: "th",
    keyboardFileUrl: "some.js",
};

describe("EditableControls", () => {
    beforeEach(() => {
        vi.clearAllMocks();
        getKeyboardInfoForMock.mockReturnValue(undefined);
    });

    afterEach(() => {
        cleanup();
        document.body.innerHTML = "";
    });

    it("shows the localized language tag when showTag is true", () => {
        const editable = makeEditable("en");
        render(
            <EditableControls
                editable={editable}
                focused={false}
                showTag={true}
            />,
        );
        expect(screen.getByText("English")).toBeInTheDocument();
    });

    it("does not show the language tag when showTag is false", () => {
        const editable = makeEditable("en");
        render(
            <EditableControls
                editable={editable}
                focused={false}
                showTag={false}
            />,
        );
        expect(screen.queryByText("English")).not.toBeInTheDocument();
    });

    it("does not show the keyboard indicator when the field is not focused, even with a KMW keyboard", () => {
        getKeyboardInfoForMock.mockReturnValue(kmwInfo);
        const editable = makeEditable("th");
        render(
            <EditableControls
                editable={editable}
                focused={false}
                showTag={true}
            />,
        );
        expect(screen.queryByRole("button")).not.toBeInTheDocument();
    });

    it("does not show the keyboard indicator when focused but the field does not use KMW", () => {
        getKeyboardInfoForMock.mockReturnValue({
            ...kmwInfo,
            useKmw: false,
        });
        const editable = makeEditable("en");
        render(
            <EditableControls
                editable={editable}
                focused={true}
                showTag={true}
            />,
        );
        expect(screen.queryByRole("button")).not.toBeInTheDocument();
    });

    it("shows the keyboard indicator when focused with a KMW keyboard, and re-shows the OSK on click", () => {
        getKeyboardInfoForMock.mockReturnValue(kmwInfo);
        const editable = makeEditable("th");
        render(
            <EditableControls
                editable={editable}
                focused={true}
                showTag={true}
            />,
        );

        const button = screen.getByRole("button");
        // Sanity: showOsk not called until the user clicks.
        expect(showOskMock).not.toHaveBeenCalled();

        fireEvent.click(button);
        expect(showOskMock).toHaveBeenCalledTimes(1);
    });

    it("appears when keyboard info arrives asynchronously via the notification event", () => {
        // No info at mount time: the field was focused before the fieldFocused
        // POST resolved.
        getKeyboardInfoForMock.mockReturnValue(undefined);
        const editable = makeEditable("th");
        render(
            <EditableControls
                editable={editable}
                focused={true}
                showTag={true}
            />,
        );
        expect(screen.queryByRole("button")).not.toBeInTheDocument();

        act(() => {
            editable.dispatchEvent(
                new CustomEvent<FieldKeyboardInfo>(kFieldKeyboardInfoEvent, {
                    detail: kmwInfo,
                }),
            );
        });

        expect(screen.getByRole("button")).toBeInTheDocument();
    });
});
