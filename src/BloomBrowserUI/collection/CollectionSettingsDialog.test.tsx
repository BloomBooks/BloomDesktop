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
        signLanguage: null,
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
    experimental: { "team-collections": false },
};

const settingsResponse: ICollectionSettingsResponse = {
    values: initialValues,
    restartPaths: ["frontBackMatter.xmatter", "languages.language3.tag"],
    isTeamCollection: false,
    autoUpdateSupported: true,
    notAllowedMessage: null,
};

const {
    mockGet,
    mockPostJson,
    mockCloseDialog,
    dialogState,
    teamCollectionFeature,
} = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockPostJson: vi.fn(),
    mockCloseDialog: vi.fn(),
    // Lets a test close and re-open the dialog, which is what the real launch plumbing does.
    dialogState: { open: true },
    // Whether Bloom has answered the subscription check yet, and whether the collection's tier
    // includes Team Collections.
    teamCollectionFeature: { loaded: true, enabled: true },
}));

vi.mock("../react_components/featureStatus", () => ({
    useGetFeatureStatus: () =>
        teamCollectionFeature.loaded
            ? { enabled: teamCollectionFeature.enabled }
            : undefined,
}));

vi.mock("../react_components/requiresSubscription", () => ({
    BloomSubscriptionIndicatorIconAndText: (props: { feature: string }) => (
        <div data-testid="subscription-badge" data-feature={props.feature} />
    ),
}));

vi.mock("../utils/bloomApi", () => ({
    get: mockGet,
    postJson: mockPostJson,
}));

vi.mock("../react_components/l10nHooks", () => ({
    useL10n: (englishText: string) => englishText,
}));

vi.mock("../react_components/BloomDialog/BloomDialogPlumbing", () => ({
    useEventLaunchedBloomDialog: () => ({
        openingEvent: {},
        closeDialog: mockCloseDialog,
        propsForBloomDialog: { open: dialogState.open },
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
    DialogCloseButton: (props: { onClick: () => void }) => (
        <button data-testid="dialog-close" onClick={props.onClick} />
    ),
}));

const { MockConfigrGroup } = vi.hoisted(() => ({
    MockConfigrGroup: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
}));

vi.mock("@sillsdev/config-r", () => ({
    ConfigrPane: (props: {
        children: React.ReactNode;
        initialValues: ICollectionSettingsValues;
        onChange: (values: unknown) => void;
    }) => (
        <div>
            {/* Config-r captures initialValues at mount, so tests check what it was given. */}
            <div data-testid="initial-collection-name">
                {props.initialValues.advanced.collectionName}
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
                data-testid="add-third-language"
                onClick={() =>
                    props.onChange({
                        ...props.initialValues,
                        languages: {
                            ...props.initialValues.languages,
                            language3: {
                                ...props.initialValues.languages.language1,
                                tag: "es",
                            },
                        },
                    })
                }
            />{" "}
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
            <button
                data-testid="pad-collection-name"
                onClick={() =>
                    props.onChange({
                        ...props.initialValues,
                        advanced: {
                            ...props.initialValues.advanced,
                            collectionName: ` ${props.initialValues.advanced.collectionName}  `,
                        },
                    })
                }
            />
            {props.children}
        </div>
    ),
    ConfigrPage: (props: React.PropsWithChildren<{ pageKey: string }>) => {
        // The real ConfigrPage throws (and blanks the whole UI) unless every child is a group.
        // Like it, look only at what toArray keeps, so a group left out with `&&` is fine.
        React.Children.toArray(props.children).forEach((child) => {
            if (
                !React.isValidElement(child) ||
                child.type !== MockConfigrGroup
            ) {
                throw new Error(
                    `ConfigrPage "${props.pageKey}" has a child that is not a ConfigrGroup`,
                );
            }
        });
        return (
            <div data-testid="configr-page" data-page-key={props.pageKey}>
                {props.children}
            </div>
        );
    },
    ConfigrGroup: MockConfigrGroup,
    ConfigrStatic: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    ConfigrBoolean: (props: { path: string; disabled?: boolean }) => (
        <input
            type="checkbox"
            data-testid="configr-boolean"
            data-path={props.path}
            disabled={props.disabled}
        />
    ),
    ConfigrInput: (props: {
        path: string;
        disabled?: boolean;
        description?: string;
    }) => (
        <div>
            <input
                type="text"
                data-testid="configr-input"
                data-path={props.path}
                disabled={props.disabled}
            />
            {props.description && (
                <div data-testid="configr-input-description">
                    {props.description}
                </div>
            )}
        </div>
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

    const respondWith = (response: ICollectionSettingsResponse) => {
        mockGet.mockImplementation(
            (_url: string, successCallback: (r: unknown) => void) => {
                successCallback({ data: response });
            },
        );
    };

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        dialogState.open = true;
        teamCollectionFeature.loaded = true;
        teamCollectionFeature.enabled = true;
        mockGet.mockReset();
        mockGet.mockImplementation(
            (_url: string, successCallback: (r: unknown) => void) => {
                successCallback({ data: settingsResponse });
            },
        );
        mockPostJson.mockReset();
        mockPostJson.mockImplementation(
            (
                _url: string,
                _data: unknown,
                successCallback?: (r: unknown) => void,
            ) => {
                successCallback?.({});
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

    it("posts the values once and closes when OK is clicked", async () => {
        await renderDialog();
        expect(mockPostJson).not.toHaveBeenCalled();

        click("dialog-ok");

        expect(mockPostJson).toHaveBeenCalledTimes(1);
        expect(mockPostJson).toHaveBeenCalledWith(
            "collection/settings",
            { values: initialValues, restartRequired: false },
            expect.any(Function),
            expect.any(Function),
        );
        expect(mockCloseDialog).toHaveBeenCalledTimes(1);
    });

    it("posts the edited values, flagged for a restart, when a restart value changed", async () => {
        await renderDialog();

        click("change-restart-value");
        await flushDeferredChange();
        click("dialog-ok");

        expect(mockPostJson).toHaveBeenCalledTimes(1);
        expect(mockPostJson.mock.calls[0][1]).toEqual({
            values: {
                ...initialValues,
                frontBackMatter: {
                    ...initialValues.frontBackMatter,
                    xmatter: "Device",
                },
            },
            restartRequired: true,
        });
    });

    it("flags a restart when a third language is added", async () => {
        await renderDialog();
        expect(initialValues.languages.language3).toBeNull();

        click("add-third-language");
        await flushDeferredChange();
        click("dialog-ok");

        expect(mockPostJson.mock.calls[0][1].restartRequired).toBe(true);
    });
    it("cancels without posting the values", async () => {
        await renderDialog();

        click("dialog-cancel");

        expect(mockPostJson).not.toHaveBeenCalled();
        expect(mockCloseDialog).toHaveBeenCalledTimes(1);
    });

    it("does not post again if OK is clicked a second time while the save is in flight", async () => {
        // The save is only reported back when we say so, so the dialog is still mid-save
        // for the second click. A second POST would save (and perhaps restart Bloom) again.
        let reportSaved: (() => void) | undefined;
        mockPostJson.mockImplementation(
            (
                _url: string,
                _data: unknown,
                successCallback?: (r: unknown) => void,
            ) => {
                reportSaved = () => successCallback?.({});
            },
        );
        await renderDialog();

        click("dialog-ok");
        expect(mockPostJson).toHaveBeenCalledTimes(1);
        if (!reportSaved) {
            throw new Error(
                "The dialog did not post at all; the rest of this test proves nothing.",
            );
        }

        click("dialog-ok");

        expect(mockPostJson).toHaveBeenCalledTimes(1);
    });

    it("starts a later open from freshly fetched values, not the previous open's", async () => {
        await renderDialog();
        expect(
            container.querySelector('[data-testid="initial-collection-name"]')
                ?.textContent,
        ).toBe("Test Collection");

        // Close it. Config-r captures initialValues when its pane mounts, so the pane has to go
        // away when the dialog closes rather than linger holding the old values.
        dialogState.open = false;
        await renderDialog();
        expect(
            container.querySelector('[data-testid="initial-collection-name"]'),
        ).toBeNull();

        // Re-open against a collection whose name has since changed.
        mockGet.mockImplementation(
            (_url: string, successCallback: (r: unknown) => void) => {
                successCallback({
                    data: {
                        ...settingsResponse,
                        values: {
                            ...initialValues,
                            advanced: {
                                ...initialValues.advanced,
                                collectionName: "Renamed Collection",
                            },
                        },
                    },
                });
            },
        );
        dialogState.open = true;
        await renderDialog();

        expect(
            container.querySelector('[data-testid="initial-collection-name"]')
                ?.textContent,
        ).toBe("Renamed Collection");
    });

    it("shows only the reason and a Close button when the user may not edit", async () => {
        mockGet.mockImplementation(
            (_url: string, successCallback: (r: unknown) => void) => {
                successCallback({
                    data: {
                        values: null,
                        restartPaths: null,
                        notAllowedMessage:
                            "You must be an administrator to change collection settings",
                    },
                });
            },
        );

        await renderDialog();

        expect(
            container.querySelector('[data-testid="not-allowed-message"]')
                ?.textContent,
        ).toBe("You must be an administrator to change collection settings");
        expect(
            container.querySelectorAll('[data-testid="configr-page"]').length,
        ).toBe(0);
        expect(container.querySelector('[data-testid="dialog-ok"]')).toBeNull();

        click("dialog-close");
        expect(mockCloseDialog).toHaveBeenCalledTimes(1);
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

    it("ignores spaces around the collection name, both for Restart and in what it saves", async () => {
        respondWith({
            ...settingsResponse,
            restartPaths: [
                ...settingsResponse.restartPaths,
                "advanced.collectionName",
            ],
        });
        await renderDialog();

        click("pad-collection-name");
        await flushDeferredChange();
        // A name that differs only by spaces is no rename, so OK stays OK.
        expect(okButtonLabel()).toBe("OK");
        click("dialog-ok");

        expect(mockPostJson).toHaveBeenCalledTimes(1);
        expect(mockPostJson.mock.calls[0][1]).toEqual({
            values: initialValues,
            restartRequired: false,
        });
    });

    describe("Advanced page", () => {
        const advancedPageElement = (selector: string) =>
            container.querySelector(
                `[data-testid="configr-page"][data-page-key="advanced"] ${selector}`,
            ) as HTMLInputElement | null;

        const collectionNameBox = () => {
            const box = advancedPageElement(
                '[data-path="advanced.collectionName"]',
            );
            if (!box) {
                throw new Error("The Advanced page has no Collection Name box");
            }
            return box;
        };

        it("offers automatic updating where Bloom supports it", async () => {
            expect(settingsResponse.autoUpdateSupported).toBe(true);

            await renderDialog();

            expect(
                advancedPageElement('[data-path="advanced.autoUpdate"]'),
            ).not.toBeNull();
        });

        it("leaves out automatic updating where Bloom does not support it", async () => {
            respondWith({ ...settingsResponse, autoUpdateSupported: false });

            await renderDialog();

            expect(
                advancedPageElement('[data-path="advanced.autoUpdate"]'),
            ).toBeNull();
            // The rest of the page is still there.
            expect(collectionNameBox().disabled).toBe(false);
        });

        it("lets the user rename a collection that is not a Team Collection", async () => {
            expect(settingsResponse.isTeamCollection).toBe(false);

            await renderDialog();

            expect(collectionNameBox().disabled).toBe(false);
            expect(
                advancedPageElement(
                    '[data-testid="configr-input-description"]',
                ),
            ).toBeNull();
        });

        it("will not let a Team Collection be renamed, and says why", async () => {
            respondWith({ ...settingsResponse, isTeamCollection: true });

            await renderDialog();

            expect(collectionNameBox().disabled).toBe(true);
            expect(
                advancedPageElement('[data-testid="configr-input-description"]')
                    ?.textContent,
            ).toBe(
                "The collection name cannot be changed because this is a Team Collection. Contact the Bloom team for more information.",
            );
        });
    });

    describe("Experimental page", () => {
        const teamCollectionsCheckbox = () => {
            const checkbox = container.querySelector(
                '[data-testid="configr-page"][data-page-key="experimental"] [data-path="experimental.team-collections"]',
            ) as HTMLInputElement | null;
            if (!checkbox) {
                throw new Error(
                    "The Experimental page has no Team Collections checkbox",
                );
            }
            return checkbox;
        };

        it("offers Team Collections, with its subscription badge, when the tier allows it", async () => {
            await renderDialog();

            expect(teamCollectionsCheckbox().disabled).toBe(false);
            expect(
                container
                    .querySelector(
                        '[data-page-key="experimental"] [data-testid="subscription-badge"]',
                    )
                    ?.getAttribute("data-feature"),
            ).toBe("TeamCollection");
        });

        it("disables Team Collections when the tier does not include it", async () => {
            teamCollectionFeature.enabled = false;

            await renderDialog();

            expect(teamCollectionsCheckbox().disabled).toBe(true);
        });

        it("disables Team Collections until the subscription check has answered", async () => {
            teamCollectionFeature.loaded = false;

            await renderDialog();

            expect(teamCollectionsCheckbox().disabled).toBe(true);
        });

        it("will not let someone in a Team Collection turn the feature off", async () => {
            respondWith({
                ...settingsResponse,
                isTeamCollection: true,
                values: {
                    ...initialValues,
                    experimental: { "team-collections": true },
                },
            });

            await renderDialog();

            expect(teamCollectionsCheckbox().disabled).toBe(true);
        });

        it("lets someone in a Team Collection turn the feature on if it is off", async () => {
            respondWith({ ...settingsResponse, isTeamCollection: true });
            expect(initialValues.experimental["team-collections"]).toBe(false);

            await renderDialog();

            expect(teamCollectionsCheckbox().disabled).toBe(false);
        });
    });
});
