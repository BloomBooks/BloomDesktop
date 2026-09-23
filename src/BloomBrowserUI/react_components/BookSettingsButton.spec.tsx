import { describe, it, expect, vi } from "vitest";

vi.mock("../bookEdit/bookAndPageSettings/PageSettingsConfigrPages", () => ({
    getCurrentPageElement: vi.fn(),
}));

import { getCurrentPageElement } from "../bookEdit/bookAndPageSettings/PageSettingsConfigrPages";
import { getInitialBookSettingsPageKey } from "./BookSettingsButton";

const getCurrentPageElementMock = vi.mocked(getCurrentPageElement);

describe("BookSettingsButton", () => {
    it("defaults to Theme & Layout if the current page is not available yet", () => {
        getCurrentPageElementMock.mockImplementation(() => {
            throw new Error("page iframe not ready");
        });

        expect(getInitialBookSettingsPageKey()).toBe("themeAndLayout");
    });

    it("uses the cover page key when the current page is a cover", () => {
        const page = document.createElement("div");
        page.classList.add("cover");
        getCurrentPageElementMock.mockReturnValue(page);

        expect(getInitialBookSettingsPageKey()).toBe("cover");
    });
});
