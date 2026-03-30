import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../utils/shared", () => ({
    getPageIframeBody: () => document.body,
}));

import {
    applyPageSettings,
    getCurrentPageSettings,
} from "./PageSettingsConfigrPages";

describe("PageSettingsConfigrPages", () => {
    beforeEach(() => {
        document.body.innerHTML =
            '<div class="bloom-page"><div class="marginBox"></div></div>';
    });

    it("uses the visible margin box color when the theme separates it from the page", () => {
        const page = document.body.querySelector(".bloom-page") as HTMLElement;
        page.style.setProperty("--page-background-color", "#2e2e2e");
        page.style.setProperty("--marginBox-background-color", "#ffffff");

        const settings = getCurrentPageSettings();

        expect(settings.page.backgroundColor).toBe("#FFFFFF");
    });

    it("uses the computed page number background color when there is no inline override", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --pageNumber-background-color: rgb(255, 255, 255);
            }
        </style>`;
        const page = document.body.querySelector(".bloom-page") as HTMLElement;

        const settings = getCurrentPageSettings();

        expect(settings.page.pageNumberBackgroundColor).toBe("#FFFFFF");
    });

    it("preserves the themed outer page background when applying a page background color", () => {
        const page = document.body.querySelector(".bloom-page") as HTMLElement;
        page.style.setProperty("--page-background-color", "#2e2e2e");
        page.style.setProperty("--marginBox-background-color", "#ffffff");

        applyPageSettings({
            page: {
                backgroundColor: "#ABCDEF",
                pageNumberColor: "#000000",
                pageNumberOutlineColor: "transparent",
                pageNumberBackgroundColor: "transparent",
            },
        });

        expect(
            page.style.getPropertyValue("--marginBox-background-color"),
        ).toBe("#ABCDEF");
        expect(page.style.getPropertyValue("--page-background-color")).toBe(
            "#2e2e2e",
        );
    });

    it("updates both page and margin box colors when the theme uses one flat background", () => {
        const page = document.body.querySelector(".bloom-page") as HTMLElement;
        page.style.setProperty("--page-background-color", "#ffffff");
        page.style.setProperty("--marginBox-background-color", "#ffffff");

        applyPageSettings({
            page: {
                backgroundColor: "#ABCDEF",
                pageNumberColor: "#000000",
                pageNumberOutlineColor: "transparent",
                pageNumberBackgroundColor: "transparent",
            },
        });

        expect(
            page.style.getPropertyValue("--marginBox-background-color"),
        ).toBe("#ABCDEF");
        expect(page.style.getPropertyValue("--page-background-color")).toBe(
            "#ABCDEF",
        );
    });
});
