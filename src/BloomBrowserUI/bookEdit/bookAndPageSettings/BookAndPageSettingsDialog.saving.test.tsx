import * as React from "react";
import ReactDOM from "react-dom";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { mockPost, mockPostJson, mockCloseDialog, configrPaneRenderState } =
    vi.hoisted(() => ({
        mockPost: vi.fn(),
        mockPostJson: vi.fn(),
        mockCloseDialog: vi.fn(),
        configrPaneRenderState: {
            lastInitialValues: undefined as
                | {
                      appearance?: { cssThemeName?: string };
                      page: {
                          backgroundColor: string;
                          pageNumberColor: string;
                          pageNumberOutlineColor: string;
                          pageNumberBackgroundColor: string;
                      };
                  }
                | undefined,
        },
    }));

vi.mock("../../utils/shared", () => ({
    getPageIframeBody: () => document.body,
    getBloomPageElement: () =>
        document.body.querySelector(".bloom-page") as HTMLElement | null,
    whenBloomPageIsReady: (callback: (page: HTMLElement) => void) => {
        const page = document.body.querySelector(".bloom-page") as HTMLElement;
        callback(page);
        return () => {};
    },
}));

vi.mock("../../utils/bloomApi", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("../../utils/bloomApi")>();

    return {
        ...actual,
        post: mockPost,
        postJson: mockPostJson,
        useApiBoolean: () => [true],
        useApiObject: (endpoint: string, defaultValue: unknown) => {
            if (endpoint === "book/settings/appearanceUIOptions") {
                return {
                    themeNames: [
                        { label: "Default", value: "default" },
                        {
                            label: "Rounded Border",
                            value: "rounded-border-ebook",
                        },
                    ],
                };
            }

            if (endpoint === "book/settings/overrides") {
                return defaultValue;
            }

            return defaultValue;
        },
        useApiStringState: () => [
            JSON.stringify({ appearance: { cssThemeName: "default" } }),
        ],
    };
});

vi.mock("../../react_components/l10nHooks", () => ({
    useL10n: (englishText: string) => englishText,
}));

vi.mock("../../react_components/featureStatus", () => ({
    useGetFeatureStatus: () => ({ enabled: true }),
}));

vi.mock("../../react_components/BloomDialog/BloomDialogPlumbing", () => ({
    useSetupBloomDialog: () => ({
        closeDialog: mockCloseDialog,
        propsForBloomDialog: { open: true },
    }),
}));

vi.mock("../../react_components/BloomDialog/BloomDialog", () => {
    const MockBloomDialog = React.forwardRef<
        HTMLDivElement,
        React.PropsWithChildren<object>
    >((props, ref) => <div ref={ref}>{props.children}</div>);
    MockBloomDialog.displayName = "MockBloomDialog";

    return {
        BloomDialog: MockBloomDialog,
        DialogBottomButtons: (props: React.PropsWithChildren<object>) => (
            <div>{props.children}</div>
        ),
        DialogMiddle: (props: React.PropsWithChildren<object>) => (
            <div>{props.children}</div>
        ),
        DialogTitle: (props: { title: string }) => <div>{props.title}</div>,
    };
});

vi.mock("../../react_components/BloomDialog/commonDialogComponents", () => ({
    DialogOkButton: (props: { onClick: () => void }) => (
        <button data-testid="dialog-ok" onClick={props.onClick}>
            OK
        </button>
    ),
    DialogCancelButton: () => <button>Cancel</button>,
}));

vi.mock("@sillsdev/config-r", () => ({
    ConfigrPane: (props: {
        children: React.ReactNode;
        initialValues: {
            appearance?: { cssThemeName?: string };
            page: {
                backgroundColor: string;
                pageNumberColor: string;
                pageNumberOutlineColor: string;
                pageNumberBackgroundColor: string;
            };
        };
        onChange: (settings: unknown) => void;
    }) => {
        configrPaneRenderState.lastInitialValues = props.initialValues;
        return (
            <div>
                <button
                    data-testid="theme-change"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            appearance: {
                                cssThemeName: "rounded-border-ebook",
                            },
                        });
                    }}
                >
                    Theme Change
                </button>
                <button
                    data-testid="page-change"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            page: {
                                ...props.initialValues.page,
                                backgroundColor: "#ABCDEF",
                            },
                        });
                    }}
                >
                    Page Change
                </button>
                <button
                    data-testid="page-reset"
                    onClick={() => {
                        props.onChange({
                            ...props.initialValues,
                            page: {
                                ...props.initialValues.page,
                            },
                        });
                    }}
                >
                    Page Reset
                </button>
            </div>
        );
    },
    ConfigrArea: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrPage: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrGroup: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrSelect: () => null,
    ConfigrBoolean: () => null,
    ConfigrStatic: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrCustomStringInput: () => null,
    ConfigrCustomObjectInput: () => null,
}));

import { BookAndPageSettingsDialog } from "./BookAndPageSettingsDialog";

describe("BookAndPageSettingsDialog saving", () => {
    let container: HTMLDivElement;

    const click = (selector: string) => {
        const button = container.querySelector(selector) as HTMLButtonElement;
        expect(button).not.toBeNull();
        act(() => {
            button.click();
        });
    };

    const renderDialog = async () => {
        await act(async () => {
            ReactDOM.render(<BookAndPageSettingsDialog />, container);
        });
    };

    beforeEach(() => {
        container = document.createElement("div");
        document.body.innerHTML =
            '<div class="bloom-page"><div class="marginBox"></div></div>';
        document.body.appendChild(container);
        configrPaneRenderState.lastInitialValues = undefined;
        mockPost.mockReset();
        mockPostJson.mockReset();
        mockCloseDialog.mockReset();
    });

    afterEach(() => {
        ReactDOM.unmountComponentAtNode(container);
        container.remove();
        document.body.innerHTML = "";
    });

    it("does not lock in untouched page values when only book settings change", async () => {
        await renderDialog();

        click('[data-testid="theme-change"]');
        click('[data-testid="dialog-ok"]');

        const page = document.body.querySelector(".bloom-page") as HTMLElement;

        expect(
            page.style.getPropertyValue("--marginBox-background-color"),
        ).toBe("");
        expect(page.style.getPropertyValue("--page-background-color")).toBe("");
        expect(page.style.getPropertyValue("--pageNumber-color")).toBe("");
        expect(page.style.getPropertyValue("--pageNumber-outline-color")).toBe(
            "",
        );
        expect(
            page.style.getPropertyValue("--pageNumber-background-color"),
        ).toBe("");
        expect(mockPostJson).toHaveBeenCalledWith("book/settings", {
            appearance: { cssThemeName: "rounded-border-ebook" },
        });
    });

    it("does not save a page value that was changed back to its original value", async () => {
        await renderDialog();

        click('[data-testid="page-change"]');
        click('[data-testid="page-reset"]');
        click('[data-testid="dialog-ok"]');

        const page = document.body.querySelector(".bloom-page") as HTMLElement;

        expect(
            page.style.getPropertyValue("--marginBox-background-color"),
        ).toBe("");
        expect(page.style.getPropertyValue("--page-background-color")).toBe("");
        expect(page.style.getPropertyValue("--pageNumber-color")).toBe("");
        expect(page.style.getPropertyValue("--pageNumber-outline-color")).toBe(
            "",
        );
        expect(
            page.style.getPropertyValue("--pageNumber-background-color"),
        ).toBe("");
    });

    it("does not post book settings when only page settings change", async () => {
        await renderDialog();

        click('[data-testid="page-change"]');
        click('[data-testid="dialog-ok"]');

        expect(mockPostJson).not.toHaveBeenCalled();
    });
});
