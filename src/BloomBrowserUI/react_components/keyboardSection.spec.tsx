import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, cleanup } from "@testing-library/react";

vi.mock("../utils/bloomApi", () => ({
    get: vi.fn(),
    postData: vi.fn(),
}));

// The shared vitest.setup.ts mock for localizationManager always resolves
// useL10n() to the l10nKey itself (ignoring the English default), which would
// make assertions here check IDs instead of meaningful text. Override it for
// this file with a fake that behaves like the real thing: return the English
// default with {0}/{1} substituted from l10nParams.
vi.mock("../lib/localizationManager/localizationManager", () => {
    const simpleFormat = (
        format: string,
        args: (string | undefined)[],
    ): string => {
        let result = format;
        args.forEach((arg, index) => {
            result = result.replace(
                new RegExp(`\\{${index}\\}`, "g"),
                arg ?? "",
            );
        });
        return result;
    };
    return {
        default: {
            asyncGetTextAndSuccessInfo: (_id: string, englishText: string) => ({
                done: (callback: (result: unknown) => void) => {
                    callback({ success: true, text: englishText });
                    return { fail: () => ({}) };
                },
                fail: () => ({ done: () => ({}) }),
            }),
            simpleFormat,
        },
    };
});

import type { AxiosResponse } from "axios";
import { get, postData } from "../utils/bloomApi";
import KeyboardSection from "./keyboardSection";

const getMock = vi.mocked(get);
const postDataMock = vi.mocked(postData);

function mockKeyboardData(data: unknown) {
    getMock.mockImplementation((_urlSuffix, successCallback) => {
        successCallback({ data } as unknown as AxiosResponse);
    });
}

const baseData = {
    current: "",
    languageTag: "th",
    automaticResolvesTo: {
        kind: "none" as const,
        displayName: "No keyboard on this machine",
    },
    installed: [{ id: "thai_kedmanee_win", name: "Thai Kedmanee (Windows)" }],
    cloud: [
        {
            id: "thai_kedmanee",
            name: "Thai Kedmanee (Keyman)",
            downloads: 4321,
        },
    ],
};

describe("KeyboardSection", () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        cleanup();
    });

    it("fetches by languageNumber and renders nothing until the data arrives", () => {
        getMock.mockImplementation(() => {
            // never calls back, simulating an in-flight request
        });
        render(<KeyboardSection languageNumber={2} languageName="Thai" />);

        expect(getMock).toHaveBeenCalledWith(
            "settings/keyboardsForLanguage?languageNumber=2",
            expect.any(Function),
        );
        expect(screen.queryByText("Automatic")).not.toBeInTheDocument();
    });

    it("renders all three groups, with the Automatic secondary text", () => {
        mockKeyboardData(baseData);
        render(<KeyboardSection languageNumber={1} languageName="Thai" />);

        fireEvent.mouseDown(screen.getByRole("combobox"));

        expect(screen.getByText("Automatic")).toBeInTheDocument();
        expect(
            screen.getByText("Currently: No keyboard on this machine"),
        ).toBeInTheDocument();

        expect(screen.getByText("Installed input methods")).toBeInTheDocument();
        expect(screen.getByText("Thai Kedmanee (Windows)")).toBeInTheDocument();

        expect(
            screen.getByText("Keyman (online) keyboards"),
        ).toBeInTheDocument();
        expect(screen.getByText("Thai Kedmanee (Keyman)")).toBeInTheDocument();
        expect(screen.getByText("4,321 downloads")).toBeInTheDocument();
    });

    it("posts the raw setting string for the language number when a different keyboard is chosen", () => {
        mockKeyboardData(baseData);
        render(<KeyboardSection languageNumber={3} languageName="Thai" />);

        fireEvent.mouseDown(screen.getByRole("combobox"));
        fireEvent.click(screen.getByText("Thai Kedmanee (Windows)"));

        expect(postDataMock).toHaveBeenCalledWith(
            "settings/setKeyboardForLanguage",
            {
                languageNumber: 3,
                keyboard: "system:thai_kedmanee_win",
            },
        );
    });

    it("posts the kmw:<id>@<tag> form for a cloud keyboard selection", () => {
        mockKeyboardData(baseData);
        render(<KeyboardSection languageNumber={1} languageName="Thai" />);

        fireEvent.mouseDown(screen.getByRole("combobox"));
        fireEvent.click(screen.getByText("Thai Kedmanee (Keyman)"));

        expect(postDataMock).toHaveBeenCalledWith(
            "settings/setKeyboardForLanguage",
            {
                languageNumber: 1,
                keyboard: "kmw:thai_kedmanee@th",
            },
        );
    });

    it("shows a disabled hint instead of hiding the cloud group when offline (empty cloud list)", () => {
        mockKeyboardData({
            ...baseData,
            cloud: [],
        });
        render(<KeyboardSection languageNumber={1} languageName="Thai" />);

        fireEvent.mouseDown(screen.getByRole("combobox"));

        expect(
            screen.getByText("Keyman (online) keyboards"),
        ).toBeInTheDocument();
        const hint = screen.getByText("Not available offline");
        expect(hint.closest("li")).toHaveAttribute("aria-disabled", "true");
    });
});
