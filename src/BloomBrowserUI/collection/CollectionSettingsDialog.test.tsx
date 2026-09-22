import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "./collectionSettingsTypes";

const initialValues: ICollectionSettingsValues = {
    languages: {
        language1: {
            tag: "xkal",
            name: "Kalaba",
            isCustomName: false,
            fontName: "Andika",
            isRightToLeft: false,
            lineHeight: 0,
            breaksLinesOnlyAtSpaces: false,
            baseUIFontSizeInPoints: 10,
        },
        language2: {
            tag: "en",
            name: "English",
            isCustomName: false,
            fontName: "Andika",
            isRightToLeft: false,
            lineHeight: 0,
            breaksLinesOnlyAtSpaces: false,
            baseUIFontSizeInPoints: 10,
        },
        language3: null,
        signLanguage: { tag: "", name: "", isCustomName: false },
    },
    frontBackMatter: {
        xmatter: "Traditional",
        pageNumberStyle: "Decimal",
        showQrCode: false,
        qrcodeCaption: "",
        country: "",
        province: "",
        district: "",
    },
    advanced: { autoUpdate: true, collectionName: "Test Collection" },
    experimental: {},
};

const settingsResponse: ICollectionSettingsResponse = {
    values: initialValues,
    context: {
        isTeamCollection: false,
        editingBlorgBook: false,
        showAutoUpdate: true,
        teamCollectionsAllowed: true,
        xmatterOfferings: [],
        brandingForcedXmatter: null,
        numberingStyles: [],
    },
    restartPaths: ["frontBackMatter.xmatter"],
};

const { mockGet, mockPost, mockPostJson, mockCloseDialog } = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockPost: vi.fn(),
    mockPostJson: vi.fn(),
    mockCloseDialog: vi.fn(),
}));

vi.mock("../utils/bloomApi", () => ({
    get: mockGet,
    post: mockPost,
    postJson: mockPostJson,
}));

vi.mock("../react_components/l10nHooks", () => ({
    useL10n: (englishText: string) => englishText,
}));

vi.mock("../react_components/BloomDialog/BloomDialogPlumbing", () => ({
    useEventLaunchedBloomDialog: () => ({
        openingEvent: { initialPageKey: "subscription" },
        closeDialog: mockCloseDialog,
        propsForBloomDialog: { open: true },
    }),
}));

vi.mock("../react_components/BloomDialog/BloomDialog", () => ({
    // The real Cancel button and the dialog frame's close both route through BloomDialog's
    // onCancel, so this stand-in exposes that as a button the tests can click.
    BloomDialog: (props: React.PropsWithChildren<{ onCancel: () => void }>) => (
        <div>
            <button data-testid="dialog-cancel" onClick={props.onCancel} />
            {props.children}
        </div>
    ),
    DialogBottomButtons: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    DialogBottomLeftButtons: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    DialogMiddle: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    DialogTitle: (props: { title: string }) => <div>{props.title}</div>,
}));

vi.mock("../react_components/BloomDialog/commonDialogComponents", () => ({
    DialogOkButton: (props: {
        onClick: () => void;
        enabled?: boolean;
        englishText?: string;
    }) => (
        <button
            data-testid="dialog-ok"
            disabled={props.enabled === false}
            onClick={props.onClick}
        >
            {props.englishText ?? "OK"}
        </button>
    ),
    DialogCancelButton: () => null,
}));

vi.mock("../react_components/boxes", () => ({
    WarningBox: (props: React.PropsWithChildren<object>) => (
        <div data-testid="save-error">{props.children}</div>
    ),
}));

vi.mock("@sillsdev/config-r", () => ({
    ConfigrPane: (props: {
        children: React.ReactNode;
        initialValues: ICollectionSettingsValues;
        initiallySelectedTopLevelPageKey?: string;
        onChange: (values: unknown) => void;
    }) => (
        <div>
            <div data-testid="initial-page-key">
                {props.initiallySelectedTopLevelPageKey}
            </div>
            <button
                data-testid="change-restart-value"
                onClick={() =>
                    props.onChange({
                        ...props.initialValues,
                        frontBackMatter: {
                            ...props.initialValues.frontBackMatter,
                            xmatter: "Device",
                        },
                    })
                }
            />
            <button
                data-testid="change-other-value"
                onClick={() =>
                    props.onChange({
                        ...props.initialValues,
                        advanced: {
                            ...props.initialValues.advanced,
                            collectionName: "Renamed",
                        },
                    })
                }
            />
            {props.children}
        </div>
    ),
    ConfigrPage: (props: React.PropsWithChildren<{ pageKey: string }>) => (
        <div data-testid="configr-page" data-page-key={props.pageKey}>
            {props.children}
        </div>
    ),
    ConfigrGroup: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrStatic: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
}));

import { CollectionSettingsDialog } from "./CollectionSettingsDialog";

describe("CollectionSettingsDialog", () => {
    let container: HTMLDivElement;

    const renderDialog = async () => {
        await act(async () => {
            renderRoot(<CollectionSettingsDialog />, container);
        });
    };

    const click = (testId: string) => {
        const button = container.querySelector(
            `[data-testid="${testId}"]`,
        ) as HTMLButtonElement;
        if (!button) {
            throw new Error(`No element with data-testid="${testId}"`);
        }
        act(() => {
            button.click();
        });
    };

    // The dialog defers the state update from Config-r's onChange into a zero-length timeout
    // (Config-r can call onChange while rendering), so tests that check the resulting UI have to
    // let that timeout run. It is a flush, not a delay: timers of equal length run in order.
    const flushDeferredChange = async () => {
        await act(async () => {
            await new Promise<void>((resolve) => {
                window.setTimeout(resolve, 0);
            });
        });
    };

    const okButtonLabel = () =>
        (container.querySelector('[data-testid="dialog-ok"]') as HTMLElement)
            .textContent;

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        mockGet.mockReset();
        mockGet.mockImplementation(
            (_url: string, successCallback: (r: unknown) => void) => {
                successCallback({ data: settingsResponse });
            },
        );
        mockPost.mockReset();
        mockPostJson.mockReset();
        mockPostJson.mockImplementation(
            (
                _url: string,
                _data: unknown,
                successCallback?: (r: unknown) => void,
            ) => {
                successCallback?.({
                    data: { restartRequired: false, errorMessage: null },
                });
            },
        );
        mockCloseDialog.mockReset();
    });

    afterEach(() => {
        unmountRoot(container);
        container.remove();
        document.body.innerHTML = "";
    });

    it("asks for the settings when it opens and shows the seven pages", async () => {
        await renderDialog();

        expect(mockGet).toHaveBeenCalledWith(
            "collection/settings",
            expect.any(Function),
        );
        expect(
            Array.from(
                container.querySelectorAll('[data-testid="configr-page"]'),
            ).map((page) => page.getAttribute("data-page-key")),
        ).toEqual([
            "languages",
            "frontBackMatter",
            "subscription",
            "teamCollection",
            "bloomLibrary",
            "advanced",
            "experimental",
        ]);
    });

    it("passes the launch event's page key on to config-r", async () => {
        await renderDialog();

        expect(
            container.querySelector('[data-testid="initial-page-key"]')
                ?.textContent,
        ).toBe("subscription");
    });

    it("posts the values once and closes when OK is clicked", async () => {
        await renderDialog();
        expect(mockPostJson).not.toHaveBeenCalled();

        click("dialog-ok");

        expect(mockPostJson).toHaveBeenCalledTimes(1);
        expect(mockPostJson).toHaveBeenCalledWith(
            "collection/settings",
            initialValues,
            expect.any(Function),
        );
        expect(mockCloseDialog).toHaveBeenCalledTimes(1);
    });

    it("posts the edited values when OK is clicked after a change", async () => {
        await renderDialog();

        click("change-restart-value");
        await flushDeferredChange();
        click("dialog-ok");

        expect(mockPostJson).toHaveBeenCalledTimes(1);
        expect(mockPostJson.mock.calls[0][1]).toEqual({
            ...initialValues,
            frontBackMatter: {
                ...initialValues.frontBackMatter,
                xmatter: "Device",
            },
        });
    });

    it("cancels without posting the values", async () => {
        await renderDialog();

        click("dialog-cancel");

        expect(mockPost).toHaveBeenCalledWith("collection/settings/cancel");
        expect(mockPostJson).not.toHaveBeenCalled();
        expect(mockCloseDialog).toHaveBeenCalledTimes(1);
    });

    it("stays open and shows the message when the save reports an error", async () => {
        mockPostJson.mockImplementation(
            (
                _url: string,
                _data: unknown,
                successCallback?: (r: unknown) => void,
            ) => {
                successCallback?.({
                    data: {
                        restartRequired: false,
                        errorMessage: "That is not a valid email address.",
                    },
                });
            },
        );
        await renderDialog();

        click("dialog-ok");

        expect(
            container.querySelector('[data-testid="save-error"]')?.textContent,
        ).toBe("That is not a valid email address.");
        expect(mockCloseDialog).not.toHaveBeenCalled();
    });

    it("relabels OK as Restart only when a restart-path value changes", async () => {
        await renderDialog();
        expect(okButtonLabel()).toBe("OK");

        click("change-other-value");
        await flushDeferredChange();
        expect(okButtonLabel()).toBe("OK");

        click("change-restart-value");
        await flushDeferredChange();

        expect(okButtonLabel()).toBe("Restart");
    });
});
