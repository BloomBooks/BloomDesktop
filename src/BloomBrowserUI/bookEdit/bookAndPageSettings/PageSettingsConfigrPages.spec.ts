import * as React from "react";
import ReactDOM from "react-dom";
import { act } from "react-dom/test-utils";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../react_components/l10nHooks", () => ({
    useL10n: (englishText: string) => englishText,
}));

vi.mock("@sillsdev/config-r", () => ({
    ConfigrPage: (props: React.PropsWithChildren<object>) =>
        React.createElement("div", undefined, props.children),
    ConfigrGroup: (props: React.PropsWithChildren<object>) =>
        React.createElement("div", undefined, props.children),
    ConfigrCustomStringInput: (props: { label: string; path: string }) =>
        React.createElement("div", { "data-testid": props.path }, props.label),
}));

vi.mock("../../utils/shared", () => ({
    getBloomPageElement: () =>
        document.body.querySelector(".bloom-page") as HTMLElement | null,
}));

import {
    applyPageSettings,
    getCurrentPageSettings,
    usePageSettingsAreaDefinition,
} from "./PageSettingsConfigrPages";

describe("PageSettingsConfigrPages", () => {
    beforeEach(() => {
        document.head.innerHTML = "";
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

    it("ignores computed marginBox aliases and reads the resolved page surface color", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --page-background-color: #fdf3c5;
                --marginBox-background-color: var(--page-background-color);
            }
        </style>`;

        const settings = getCurrentPageSettings();

        expect(settings.page.backgroundColor).toBe("#FDF3C5");
    });

    it("uses the computed page number background color when there is no inline override", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --pageNumber-background-color: rgb(255, 255, 255);
            }
        </style>`;

        const settings = getCurrentPageSettings();

        expect(settings.page.pageNumberBackgroundColor).toBe("#FFFFFF");
    });

    it("shows the page number color controls in the colors page", () => {
        const container = document.createElement("div");

        const TestComponent: React.FunctionComponent = () => {
            const pageSettingsArea = usePageSettingsAreaDefinition({});
            return React.createElement(
                React.Fragment,
                undefined,
                pageSettingsArea.pages[0],
            );
        };

        act(() => {
            ReactDOM.render(React.createElement(TestComponent), container);
        });

        expect(
            container.querySelector('[data-testid="page.pageNumberColor"]'),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="page.pageNumberOutlineColor"]',
            ),
        ).not.toBeNull();
        expect(
            container.querySelector(
                '[data-testid="page.pageNumberBackgroundColor"]',
            ),
        ).not.toBeNull();

        ReactDOM.unmountComponentAtNode(container);
    });

    it("updates only the page background variable when applying a page background color", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --page-frame-color: #2e2e2e;
                --page-background-color: #ffffff;
                --marginBox-background-color: #ffffff;
                --page-and-marginBox-are-same-color-multiplicand: 0;
            }
        </style>`;
        const page = document.body.querySelector(".bloom-page") as HTMLElement;

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
        ).toBe("");
        expect(page.style.getPropertyValue("--page-background-color")).toBe(
            "#ABCDEF",
        );
    });

    it("updates both page and margin box colors when the theme uses one flat background", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --page-background-color: #ffffff;
                --marginBox-background-color: #ffffff;
                --page-and-marginBox-are-same-color-multiplicand: 1;
            }
        </style>`;
        const page = document.body.querySelector(".bloom-page") as HTMLElement;

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
        ).toBe("");
        expect(page.style.getPropertyValue("--page-background-color")).toBe(
            "#ABCDEF",
        );
    });

    it("updates the page surface when the book theme changes to default", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --page-background-color: #2e2e2e;
                --marginBox-background-color: #ffffff;
                --page-and-marginBox-are-same-color-multiplicand: 0;
            }
        </style>`;
        const page = document.body.querySelector(".bloom-page") as HTMLElement;

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
        ).toBe("");
        expect(page.style.getPropertyValue("--page-background-color")).toBe(
            "#ABCDEF",
        );
    });

    it("updates the page surface when the book theme changes to rounded-border-ebook", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --page-background-color: #ffffff;
                --marginBox-background-color: #ffffff;
                --page-and-marginBox-are-same-color-multiplicand: 1;
            }
        </style>`;
        const page = document.body.querySelector(".bloom-page") as HTMLElement;

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
        ).toBe("");
        expect(page.style.getPropertyValue("--page-background-color")).toBe(
            "#ABCDEF",
        );
    });

    it("updates the page surface the same way for other themes", () => {
        document.head.innerHTML = `<style>
            .bloom-page {
                --page-background-color: #2e2e2e;
                --marginBox-background-color: #ffffff;
                --page-and-marginBox-are-same-color-multiplicand: 0;
            }
        </style>`;
        const page = document.body.querySelector(".bloom-page") as HTMLElement;

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
        ).toBe("");
        expect(page.style.getPropertyValue("--page-background-color")).toBe(
            "#ABCDEF",
        );
    });
});
