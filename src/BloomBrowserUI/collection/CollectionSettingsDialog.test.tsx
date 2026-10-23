import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../utils/reactRender";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
    IQrCodeBadgeData,
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
    subscription: { code: "Saved-Code-123456-7890" },
    teamCollection: { administrators: "", createInFolder: "" },
    bloomLibrary: { defaultBookshelf: "" },
    advanced: { autoUpdate: true, collectionName: "Test Collection" },
    experimental: { "team-collections": false, tables: false },
};

const settingsResponse: ICollectionSettingsResponse = {
    values: initialValues,
    restartPaths: ["frontBackMatter.xmatter", "languages.language3.tag"],
    isTeamCollection: false,
    editingABlorgBook: false,
    notAllowedMessage: null,
};

// What GET settings/xmatter offers, unless a test says otherwise.
const defaultXmatterOfferings = [
    {
        displayName: "Traditional",
        internalName: "Traditional",
        description: "Title page and credits page inside the covers.",
    },
    {
        displayName: "Device",
        internalName: "Device",
        description: "Made for reading on phones.",
    },
];

// What GET teamCollection/defaultRepoFolder answers.
const kDropboxFolder = "C:\\Users\\me\\Dropbox";

const qrCodeBadge: IQrCodeBadgeData = {
    qrCode: "data:image/png;base64,AAAA",
    languageName: "Kalaba",
    displayUrl: "BloomLibrary.org/language:xkal",
    defaultCaption: "More books in {0} language:",
};

const {
    mockGet,
    mockPostJson,
    mockCloseDialog,
    dialogState,
    teamCollectionFeature,
    tableFeature,
    enterpriseBookshelves,
} = vi.hoisted(() => ({
    mockGet: vi.fn(),
    mockPostJson: vi.fn(),
    mockCloseDialog: vi.fn(),
    // Lets a test close and re-open the dialog, which is what the real launch plumbing does, and
    // launch it again (a new event) while it is open.
    dialogState: { open: true, openingEvent: {} as Record<string, unknown> },
    // Whether Bloom has answered the subscription check yet, and whether the collection's tier
    // includes Team Collections.
    teamCollectionFeature: { loaded: true, enabled: true },
    // The same, for Tables.
    tableFeature: { loaded: true, enabled: true },
    // What useGetEnterpriseBookshelves reports: the subscription's shelves (after its "none"),
    // and whether Bloom Library could not be reached.
    enterpriseBookshelves: {
        shelves: [] as { value: string; label: string; tooltip: string }[],
        error: false,
    },
}));

// The packs GET settings/xmatter offers in the current test.
let xmatterOfferings = defaultXmatterOfferings;

vi.mock("./useGetEnterpriseBookshelves", () => ({
    useGetEnterpriseBookshelves: () => ({
        project: "",
        defaultBookshelfUrlKey: "",
        validBookshelves: [
            { value: "none", label: "None", tooltip: "" },
            ...enterpriseBookshelves.shelves,
        ],
        error: enterpriseBookshelves.error,
    }),
}));

vi.mock("../react_components/featureStatus", () => ({
    useGetFeatureStatus: (featureName: string | undefined) => {
        const feature =
            featureName === "Table" ? tableFeature : teamCollectionFeature;
        return feature.loaded ? { enabled: feature.enabled } : undefined;
    },
}));

vi.mock("../react_components/requiresSubscription", () => ({
    BloomSubscriptionIndicatorIconAndText: (props: { feature: string }) => (
        <div data-testid="subscription-badge" data-feature={props.feature} />
    ),
}));

// The Subscription page's code editor and status talk to Bloom themselves. These
// stand-ins show the code they were given, and let a test type a new one.
vi.mock("./subscriptionCodeControl", () => ({
    SubscriptionControls: (props: {
        code: string;
        onCodeChange: (code: string) => void;
    }) => (
        <div>
            <div data-testid="subscription-code">{props.code}</div>
            <button
                data-testid="type-subscription-code"
                onClick={() => props.onCodeChange(" New-Code-654321-0987 ")}
            />
            <button
                data-testid="clear-subscription-code"
                onClick={() => props.onCodeChange("")}
            />
        </div>
    ),
}));

vi.mock("../collectionsTab/SubscriptionStatus", () => ({
    SubscriptionStatus: () => null,
}));

vi.mock("../react_components/markdown", () => ({
    Markdown: () => null,
}));

// On the Subscription page, the note box holds only the community invitation.
vi.mock("../react_components/boxes", () => ({
    NoteBox: () => <div data-testid="community-invitation" />,
}));

// The Team Collection page's pieces come from the WinForms tab and talk to Bloom themselves. These
// stand-ins show what they were given, and let a test type administrators or choose a folder.
vi.mock("../teamCollection/TeamCollectionSettingsPanel", () => ({
    TeamCollectionSettings: (props: {
        administrators: string;
        onAdministratorsChange: (administrators: string) => void;
        administratorsError?: string;
        onCreateTeamCollection: () => void;
        pendingCreateFolder?: string;
    }) => (
        <div>
            <div data-testid="administrators">{props.administrators}</div>
            <div data-testid="administrators-error">
                {props.administratorsError}
            </div>
            <div data-testid="pending-create-folder">
                {props.pendingCreateFolder}
            </div>
            <button
                data-testid="type-valid-administrators"
                onClick={() =>
                    props.onAdministratorsChange(
                        " a@example.com, b@example.com ",
                    )
                }
            />
            <button
                data-testid="type-invalid-administrators"
                onClick={() => props.onAdministratorsChange("a@example.com, b")}
            />
            <button
                data-testid="create-team-collection"
                onClick={props.onCreateTeamCollection}
            />
        </div>
    ),
}));

vi.mock("../teamCollection/CreateTeamCollection", () => ({
    CreateTeamCollectionDialog: (props: {
        defaultRepoFolder: string;
        onFolderChosen: (folder: string) => void;
        onClose: () => void;
    }) => (
        <div data-testid="create-dialog">
            <div data-testid="create-dialog-folder">
                {props.defaultRepoFolder}
            </div>
            <button
                data-testid="create-dialog-ok"
                onClick={() => {
                    props.onFolderChosen(props.defaultRepoFolder);
                    props.onClose();
                }}
            />
            <button
                data-testid="create-dialog-cancel"
                onClick={props.onClose}
            />
        </div>
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
        openingEvent: dialogState.openingEvent,
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
    DialogHelpButton: (props: { helpId: string }) => (
        <button data-testid="dialog-help" data-help-id={props.helpId} />
    ),
}));

const {
    MockConfigrGroup,
    MockConfigrStatic,
    MockMountedWith,
    MockShownPage,
    withPaneRef,
} = vi.hoisted(() => ({
    // The dialog gives ConfigrPane a ref, so the stand-in must accept one; this passes it on
    // to the stand-in as its paneRef prop.
    withPaneRef: <P,>(Pane: React.FunctionComponent<P>) => {
        const PaneTakingRef = Pane as unknown as React.FunctionComponent<{
            paneRef: React.ForwardedRef<unknown>;
        }>;
        const MockConfigrPane = React.forwardRef((props: object, ref) => (
            <PaneTakingRef {...props} paneRef={ref} />
        ));
        MockConfigrPane.displayName = "MockConfigrPane";
        return MockConfigrPane;
    },
    // The page the pane shows. Like Config-r, it opens on the initially selected page, moves
    // when the user clicks another one, and moves when the pane's handle asks it to.
    MockShownPage: (props: {
        paneRef: React.ForwardedRef<unknown>;
        initialPageKey?: string;
    }) => {
        const [shownPageKey, setShownPageKey] = React.useState(
            props.initialPageKey,
        );
        React.useImperativeHandle(props.paneRef, () => ({
            selectTopLevelPage: setShownPageKey,
        }));
        return (
            <>
                <div data-testid="shown-page">{shownPageKey}</div>
                <button
                    data-testid="user-shows-bloom-library"
                    onClick={() => setShownPageKey("bloomLibrary")}
                />
            </>
        );
    },
    // What the pane was given as it mounted; later renders do not change it, as in Config-r.
    MockMountedWith: (props: { collectionName: string; pageKey?: string }) => {
        const [mountedWith] = React.useState(props);
        return (
            <>
                <div data-testid="initial-collection-name">
                    {mountedWith.collectionName}
                </div>
                <div data-testid="initial-page-key">{mountedWith.pageKey}</div>
            </>
        );
    },
    MockConfigrGroup: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
    MockConfigrStatic: (props: React.PropsWithChildren<object>) => (
        <div>{props.children}</div>
    ),
}));

vi.mock("@sillsdev/config-r", () => ({
    ConfigrPane: withPaneRef(
        (props: {
            children: React.ReactNode;
            initialValues: ICollectionSettingsValues;
            initiallySelectedTopLevelPageKey?: string;
            onChange: (values: unknown) => void;
            onTopLevelPageChange: (pageKey: string | undefined) => void;
            paneRef: React.ForwardedRef<unknown>;
        }) => (
            <div>
                {/* Config-R reports the page it shows; these stand for the user choosing one. */}
                <button
                    data-testid="show-subscription-page"
                    onClick={() => props.onTopLevelPageChange("subscription")}
                />
                <button
                    data-testid="show-advanced-page"
                    onClick={() => props.onTopLevelPageChange("advanced")}
                />
                {/* Config-r reads initialValues and the page to show only as it mounts, so tests check what it was given. */}
                <MockMountedWith
                    collectionName={props.initialValues.advanced.collectionName}
                    pageKey={props.initiallySelectedTopLevelPageKey}
                />
                <MockShownPage
                    paneRef={props.paneRef}
                    initialPageKey={props.initiallySelectedTopLevelPageKey}
                />
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
                    data-testid="show-qr-code-with-caption"
                    onClick={() =>
                        props.onChange({
                            ...props.initialValues,
                            frontBackMatter: {
                                ...props.initialValues.frontBackMatter,
                                showQrCode: true,
                                qrcodeCaption: "Books in {0}, free:",
                            },
                        })
                    }
                />
                <button
                    data-testid="pad-country"
                    onClick={() =>
                        props.onChange({
                            ...props.initialValues,
                            frontBackMatter: {
                                ...props.initialValues.frontBackMatter,
                                country: "  Kenya ",
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
                <button
                    data-testid="choose-no-bookshelf"
                    onClick={() =>
                        props.onChange({
                            ...props.initialValues,
                            bloomLibrary: { defaultBookshelf: "" },
                        })
                    }
                />
                {props.children}
            </div>
        ),
    ),
    ConfigrPage: (props: React.PropsWithChildren<{ pageKey: string }>) => {
        // The real ConfigrPage throws (and blanks the whole UI) unless every child is a group or
        // a static block.
        React.Children.forEach(props.children, (child) => {
            if (
                !React.isValidElement(child) ||
                (child.type !== MockConfigrGroup &&
                    child.type !== MockConfigrStatic)
            ) {
                throw new Error(
                    `ConfigrPage "${props.pageKey}" has a child that is neither a ConfigrGroup nor a ConfigrStatic`,
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
    ConfigrStatic: MockConfigrStatic,
    ConfigrBoolean: (props: { path: string; disabled?: boolean }) => (
        <input
            type="checkbox"
            data-testid="configr-boolean"
            data-path={props.path}
            disabled={props.disabled}
        />
    ),
    ConfigrSelect: (props: {
        path: string;
        disabled?: boolean;
        description?: string;
        options: { value: string; label?: string }[];
    }) => (
        <div>
            <select
                data-testid="configr-select"
                data-path={props.path}
                disabled={props.disabled}
            >
                {props.options.map((option) => (
                    <option key={option.value} value={option.value}>
                        {option.label}
                    </option>
                ))}
            </select>
            {props.description && (
                <div data-testid="configr-select-description">
                    {props.description}
                </div>
            )}
        </div>
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

    // Answers GET collection/settings with the given response, and the Front & Back Matter page's
    // own GETs with their test data.
    const respondWith = (response: ICollectionSettingsResponse) => {
        mockGet.mockImplementation(
            (url: string, successCallback: (r: unknown) => void) => {
                switch (url) {
                    case "collection/settings":
                        successCallback({ data: response });
                        break;
                    case "settings/xmatter":
                        successCallback({
                            data: { xmatterOfferings: xmatterOfferings },
                        });
                        break;
                    case "collection/qrCodeBadge":
                        successCallback({ data: qrCodeBadge });
                        break;
                    case "teamCollection/defaultRepoFolder":
                        successCallback({ data: kDropboxFolder });
                        break;
                    default:
                        throw new Error(`The test did not expect GET ${url}`);
                }
            },
        );
    };

    beforeEach(() => {
        container = document.createElement("div");
        document.body.appendChild(container);
        dialogState.open = true;
        dialogState.openingEvent = {};
        teamCollectionFeature.loaded = true;
        teamCollectionFeature.enabled = true;
        enterpriseBookshelves.shelves = [];
        enterpriseBookshelves.error = false;
        tableFeature.loaded = true;
        tableFeature.enabled = true;
        xmatterOfferings = defaultXmatterOfferings;
        mockGet.mockReset();
        respondWith(settingsResponse);
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
        respondWith({
            ...settingsResponse,
            values: {
                ...initialValues,
                experimental: { "team-collections": true, tables: false },
            },
        });
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
        respondWith({
            ...settingsResponse,
            values: {
                ...initialValues,
                advanced: {
                    ...initialValues.advanced,
                    collectionName: "Renamed Collection",
                },
            },
        });
        dialogState.open = true;
        await renderDialog();

        expect(
            container.querySelector('[data-testid="initial-collection-name"]')
                ?.textContent,
        ).toBe("Renamed Collection");
    });

    it("shows only the reason and a Close button when the user may not edit", async () => {
        respondWith({
            values: null,
            restartPaths: null,
            notAllowedMessage:
                "You must be an administrator to change collection settings",
        } as unknown as ICollectionSettingsResponse);

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

    it("trims any text the user edited, on any page, when it saves", async () => {
        expect(initialValues.frontBackMatter.country).toBe("");
        await renderDialog();

        click("pad-country");
        await flushDeferredChange();
        click("dialog-ok");

        expect(mockPostJson.mock.calls[0][1].values).toEqual({
            ...initialValues,
            frontBackMatter: {
                ...initialValues.frontBackMatter,
                country: "Kenya",
            },
        });
    });

    it("does not rename a collection whose name already has a leading space when it saves", async () => {
        const spacedValues: ICollectionSettingsValues = {
            ...initialValues,
            advanced: {
                ...initialValues.advanced,
                collectionName: " Spaced Books",
            },
        };
        respondWith({
            ...settingsResponse,
            values: spacedValues,
            restartPaths: [
                ...settingsResponse.restartPaths,
                "advanced.collectionName",
            ],
        });
        await renderDialog();
        expect(okButtonLabel()).toBe("OK");

        click("dialog-ok");

        // The name goes back exactly as it was, so C# sees no rename.
        expect(mockPostJson.mock.calls[0][1]).toEqual({
            values: spacedValues,
            restartRequired: false,
        });
    });

    it("lets the user remove a leading space the collection name already has", async () => {
        const spacedValues: ICollectionSettingsValues = {
            ...initialValues,
            advanced: {
                ...initialValues.advanced,
                collectionName: " Spaced Books",
            },
        };
        respondWith({
            ...settingsResponse,
            values: spacedValues,
            restartPaths: [
                ...settingsResponse.restartPaths,
                "advanced.collectionName",
            ],
        });
        await renderDialog();

        // Any edit of the name; the posted name is then trimmed.
        click("pad-collection-name");
        await flushDeferredChange();
        expect(okButtonLabel()).toBe("Restart");
        click("dialog-ok");

        expect(
            mockPostJson.mock.calls[0][1].values.advanced.collectionName,
        ).toBe("Spaced Books");
        expect(mockPostJson.mock.calls[0][1].restartRequired).toBe(true);
    });

    describe("Front & Back Matter page", () => {
        const pageElement = (selector: string) =>
            container.querySelector(
                `[data-testid="configr-page"][data-page-key="frontBackMatter"] ${selector}`,
            ) as HTMLElement | null;

        const styleSelect = () => {
            const select = pageElement(
                '[data-path="frontBackMatter.xmatter"]',
            ) as HTMLSelectElement | null;
            if (!select) {
                throw new Error(
                    "The Front & Back Matter page has no Style list",
                );
            }
            return select;
        };

        const styleChoices = () =>
            Array.from(styleSelect().options).map((option) => [
                option.value,
                option.textContent,
            ]);

        const captionBox = () => {
            const box = pageElement(
                '[data-path="frontBackMatter.qrcodeCaption"]',
            ) as HTMLInputElement | null;
            if (!box) {
                throw new Error(
                    "The Front & Back Matter page has no Caption box",
                );
            }
            return box;
        };

        it("offers the packs, and describes the one chosen", async () => {
            expect(initialValues.frontBackMatter.xmatter).toBe("Traditional");

            await renderDialog();

            expect(styleChoices()).toEqual([
                ["Traditional", "Traditional"],
                ["Device", "Device"],
            ]);
            expect(styleSelect().disabled).toBe(false);
            expect(
                pageElement('[data-testid="configr-select-description"]')
                    ?.textContent,
            ).toBe("Title page and credits page inside the covers.");

            click("change-restart-value");
            await flushDeferredChange();

            expect(
                pageElement('[data-testid="configr-select-description"]')
                    ?.textContent,
            ).toBe("Made for reading on phones.");
        });

        it("disables the list when the branding chooses the pack", async () => {
            xmatterOfferings = [defaultXmatterOfferings[0]];

            await renderDialog();

            expect(styleChoices()).toEqual([["Traditional", "Traditional"]]);
            expect(styleSelect().disabled).toBe(true);
        });

        it("keeps the saved pack choosable when it is no longer offered", async () => {
            respondWith({
                ...settingsResponse,
                values: {
                    ...initialValues,
                    frontBackMatter: {
                        ...initialValues.frontBackMatter,
                        xmatter: "Uninstalled",
                    },
                },
            });

            await renderDialog();

            expect(styleChoices()).toEqual([
                ["Traditional", "Traditional"],
                ["Device", "Device"],
                ["Uninstalled", "Uninstalled"],
            ]);
        });

        it("shows the plain badge, and disables the caption, when QR codes are off", async () => {
            expect(initialValues.frontBackMatter.showQrCode).toBe(false);

            await renderDialog();

            expect(captionBox().disabled).toBe(true);
            const preview = pageElement(
                '[data-testid="qr-code-badge-preview"]',
            );
            expect(preview?.querySelectorAll("img").length).toBe(1);
            expect(preview?.querySelector("img")?.getAttribute("src")).toBe(
                "/bloom/branding/Default/made-with-bloom-badge.svg",
            );
        });

        it("previews the QR code and the caption as typed, with the language in bold", async () => {
            await renderDialog();

            click("show-qr-code-with-caption");
            await flushDeferredChange();

            expect(captionBox().disabled).toBe(false);
            const preview = pageElement(
                '[data-testid="qr-code-badge-preview"]',
            );
            expect(
                Array.from(preview?.querySelectorAll("img") ?? []).map((img) =>
                    img.getAttribute("src"),
                ),
            ).toEqual([
                "/bloom/branding/Default/made-with-bloom-badge-text.svg",
                qrCodeBadge.qrCode,
            ]);
            const caption = pageElement(
                '[data-testid="qr-code-badge-caption"]',
            );
            expect(caption?.textContent).toBe("Books in Kalaba, free:");
            expect(caption?.querySelector("b")?.textContent).toBe("Kalaba");
            expect(preview?.textContent).toContain(qrCodeBadge.displayUrl);
        });

        it("previews the default caption, as books get it, when the caption is empty", async () => {
            respondWith({
                ...settingsResponse,
                values: {
                    ...initialValues,
                    frontBackMatter: {
                        ...initialValues.frontBackMatter,
                        showQrCode: true,
                        qrcodeCaption: "",
                    },
                },
            });

            await renderDialog();

            expect(
                pageElement('[data-testid="qr-code-badge-caption"]')
                    ?.textContent,
            ).toBe("More books in Kalaba language:");
        });

        it("previews an untouched caption of only spaces as it is, since that is saved and books show it", async () => {
            respondWith({
                ...settingsResponse,
                values: {
                    ...initialValues,
                    frontBackMatter: {
                        ...initialValues.frontBackMatter,
                        showQrCode: true,
                        qrcodeCaption: "  ",
                    },
                },
            });

            await renderDialog();

            expect(
                pageElement('[data-testid="qr-code-badge-caption"]')
                    ?.textContent,
            ).toBe("  ");
        });

        it("has boxes for the place names", async () => {
            await renderDialog();

            for (const place of ["country", "province", "district"]) {
                expect(
                    pageElement(`[data-path="frontBackMatter.${place}"]`),
                ).not.toBeNull();
            }
        });
    });

    it("asks Help for the topic of the page Config-R says is showing", async () => {
        const helpTopic = () =>
            container
                .querySelector('[data-testid="dialog-help"]')
                ?.getAttribute("data-help-id");
        await renderDialog();
        expect(helpTopic()).toBe(
            "User_Interface/Dialog_boxes/Settings_dialog_box.htm",
        );

        click("show-subscription-page");
        expect(helpTopic()).toBe(
            "Tasks/Basic_tasks/Enter_Subscription_Code.htm",
        );

        click("show-advanced-page");
        expect(helpTopic()).toBe(
            "User_Interface/Dialog_boxes/Settings_dialog_box.htm",
        );
    });

    it("offers Help even when the user may not edit the settings", async () => {
        respondWith({
            ...settingsResponse,
            notAllowedMessage: "You must be an administrator",
        });
        await renderDialog();

        expect(
            container
                .querySelector('[data-testid="dialog-help"]')
                ?.getAttribute("data-help-id"),
        ).toBe("User_Interface/Dialog_boxes/Settings_dialog_box.htm");
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

        it("offers automatic updating", async () => {
            await renderDialog();

            expect(
                advancedPageElement('[data-path="advanced.autoUpdate"]'),
            ).not.toBeNull();
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

    describe("Bloom Library page", () => {
        const bloomLibraryPageElement = (selector: string) =>
            container.querySelector(
                `[data-testid="configr-page"][data-page-key="bloomLibrary"] ${selector}`,
            );

        const bookshelfSelect = () => {
            const select = bloomLibraryPageElement(
                '[data-path="bloomLibrary.defaultBookshelf"]',
            ) as HTMLSelectElement | null;
            if (!select) {
                throw new Error("The Bloom Library page has no bookshelf list");
            }
            return select;
        };

        const bookshelfChoices = () =>
            Array.from(bookshelfSelect().options).map((option) => [
                option.value,
                option.textContent,
            ]);

        const respondWithSavedBookshelf = (defaultBookshelf: string) => {
            respondWith({
                ...settingsResponse,
                values: {
                    ...initialValues,
                    bloomLibrary: { defaultBookshelf },
                },
                restartPaths: [
                    ...settingsResponse.restartPaths,
                    "bloomLibrary.defaultBookshelf",
                ],
            });
        };

        it("offers None and the subscription's bookshelves, with the subscription badge", async () => {
            enterpriseBookshelves.shelves = [
                { value: "shelf-a", label: "shelf-a", tooltip: "Shelf A" },
                { value: "shelf-b", label: "shelf-b", tooltip: "Shelf B" },
            ];

            await renderDialog();

            // "None" stands for the "" the collection stores, not the hook's "none".
            expect(bookshelfChoices()).toEqual([
                ["", "None"],
                ["shelf-a", "shelf-a"],
                ["shelf-b", "shelf-b"],
            ]);
            expect(bookshelfSelect().disabled).toBe(false);
            expect(
                bloomLibraryPageElement(
                    '[data-testid="subscription-badge"]',
                )?.getAttribute("data-feature"),
            ).toBe("Bookshelf");
        });

        it("disables the list when the subscription has no bookshelves", async () => {
            expect(enterpriseBookshelves.shelves).toEqual([]);

            await renderDialog();

            expect(bookshelfChoices()).toEqual([["", "None"]]);
            expect(bookshelfSelect().disabled).toBe(true);
        });

        it("keeps the saved bookshelf choosable when Bloom Library cannot be reached, and says so", async () => {
            enterpriseBookshelves.error = true;
            respondWithSavedBookshelf("saved-shelf");

            await renderDialog();

            expect(bookshelfChoices()).toEqual([
                ["", "None"],
                ["saved-shelf", "saved-shelf"],
            ]);
            expect(bookshelfSelect().disabled).toBe(false);
            expect(
                bloomLibraryPageElement(
                    '[data-testid="configr-select-description"]',
                )?.textContent,
            ).toBe(
                "Bloom could not reach the server to get the list of bookshelves.",
            );
        });

        it("restarts when the user changes the bookshelf, and saves None as no bookshelf", async () => {
            respondWithSavedBookshelf("saved-shelf");
            await renderDialog();
            expect(okButtonLabel()).toBe("OK");

            click("choose-no-bookshelf");
            await flushDeferredChange();
            expect(okButtonLabel()).toBe("Restart");
            click("dialog-ok");

            expect(mockPostJson.mock.calls[0][1]).toEqual({
                values: {
                    ...initialValues,
                    bloomLibrary: { defaultBookshelf: "" },
                },
                restartRequired: true,
            });
        });

        it("saves an unchanged bookshelf as it was, without a restart", async () => {
            respondWithSavedBookshelf("saved-shelf");
            await renderDialog();
            expect(okButtonLabel()).toBe("OK");

            click("dialog-ok");

            expect(mockPostJson.mock.calls[0][1]).toEqual({
                values: {
                    ...initialValues,
                    bloomLibrary: { defaultBookshelf: "saved-shelf" },
                },
                restartRequired: false,
            });
        });
    });

    describe("Subscription page", () => {
        const subscriptionPageElement = (testId: string) =>
            container.querySelector(
                `[data-testid="configr-page"][data-page-key="subscription"] [data-testid="${testId}"]`,
            );

        const respondWithSubscriptionRestart = (code: string) => {
            respondWith({
                ...settingsResponse,
                values: { ...initialValues, subscription: { code } },
                restartPaths: [
                    ...settingsResponse.restartPaths,
                    "subscription.code",
                ],
            });
        };

        it("shows the saved code, and no community invitation", async () => {
            await renderDialog();

            expect(
                subscriptionPageElement("subscription-code")?.textContent,
            ).toBe("Saved-Code-123456-7890");
            expect(subscriptionPageElement("community-invitation")).toBeNull();
        });

        it("invites a collection with no code to a community subscription", async () => {
            respondWithSubscriptionRestart("");

            await renderDialog();

            expect(
                subscriptionPageElement("community-invitation"),
            ).not.toBeNull();
        });

        it("saves the code as typed, trimmed, and restarts", async () => {
            respondWithSubscriptionRestart("Saved-Code-123456-7890");
            await renderDialog();
            expect(okButtonLabel()).toBe("OK");

            click("type-subscription-code");
            expect(
                subscriptionPageElement("subscription-code")?.textContent,
            ).toBe(" New-Code-654321-0987 ");
            expect(okButtonLabel()).toBe("Restart");
            click("dialog-ok");

            expect(mockPostJson.mock.calls[0][1]).toEqual({
                values: {
                    ...initialValues,
                    subscription: { code: "New-Code-654321-0987" },
                },
                restartRequired: true,
            });
        });

        it("keeps a typed code when another page's value changes", async () => {
            respondWithSubscriptionRestart("Saved-Code-123456-7890");
            await renderDialog();

            click("clear-subscription-code");
            // Config-R's values still carry the code it was given when the dialog opened.
            click("change-other-value");
            await flushDeferredChange();
            click("dialog-ok");

            expect(mockPostJson.mock.calls[0][1].values.subscription).toEqual({
                code: "",
            });
            expect(
                mockPostJson.mock.calls[0][1].values.advanced.collectionName,
            ).toBe("Renamed");
        });

        it("goes to the Subscription page, keeping the user's changes, when a badge in the open dialog asks for it", async () => {
            await renderDialog();
            const textOf = (testId: string) =>
                container.querySelector(`[data-testid="${testId}"]`)
                    ?.textContent;
            expect(textOf("shown-page")).toBe("");
            click("change-other-value");
            click("type-subscription-code");
            await flushDeferredChange();

            // What a subscription badge's click becomes: the dialog is launched again.
            dialogState.openingEvent = { initialPageKey: "subscription" };
            await renderDialog();
            expect(textOf("shown-page")).toBe("subscription");

            // The user goes back to another page, and a badge there asks again.
            click("user-shows-bloom-library");
            expect(textOf("shown-page")).toBe("bloomLibrary");
            dialogState.openingEvent = { initialPageKey: "subscription" };
            await renderDialog();
            expect(textOf("shown-page")).toBe("subscription");

            // The pane was never mounted again, so it is still editing the changed values.
            expect(textOf("initial-collection-name")).toBe("Test Collection");
            click("dialog-ok");
            expect(
                mockPostJson.mock.calls[0][1].values.advanced.collectionName,
            ).toBe("Renamed");
            expect(mockPostJson.mock.calls[0][1].values.subscription).toEqual({
                code: "New-Code-654321-0987",
            });
        });

        it("starts a later open from the saved code, not the one typed and cancelled", async () => {
            await renderDialog();
            click("type-subscription-code");
            click("dialog-cancel");
            expect(mockPostJson).not.toHaveBeenCalled();

            dialogState.open = false;
            await renderDialog();
            dialogState.open = true;
            await renderDialog();

            expect(
                subscriptionPageElement("subscription-code")?.textContent,
            ).toBe("Saved-Code-123456-7890");
        });
    });

    describe("Team Collection page", () => {
        const teamCollectionPageElement = (testId: string) =>
            container.querySelector(
                `[data-testid="configr-page"][data-page-key="teamCollection"] [data-testid="${testId}"]`,
            );
        const pageKeys = () =>
            Array.from(
                container.querySelectorAll('[data-testid="configr-page"]'),
            ).map((page) => page.getAttribute("data-page-key"));
        const okButton = () =>
            container.querySelector(
                '[data-testid="dialog-ok"]',
            ) as HTMLButtonElement;

        // A Team Collection with one administrator, whose administrators need a restart.
        const respondAsTeamCollection = () => {
            respondWith({
                ...settingsResponse,
                isTeamCollection: true,
                values: {
                    ...initialValues,
                    teamCollection: {
                        administrators: "boss@example.com",
                        createInFolder: "",
                    },
                },
                restartPaths: [
                    ...settingsResponse.restartPaths,
                    "teamCollection.administrators",
                ],
            });
        };

        // Not a Team Collection, with the feature on, so it can become one.
        const respondReadyToCreate = () => {
            respondWith({
                ...settingsResponse,
                values: {
                    ...initialValues,
                    experimental: { "team-collections": true, tables: false },
                },
                restartPaths: [
                    ...settingsResponse.restartPaths,
                    "teamCollection.createInFolder",
                ],
            });
        };

        it("is not offered while the feature is off and this is not a Team Collection", async () => {
            expect(initialValues.experimental["team-collections"]).toBe(false);
            expect(settingsResponse.isTeamCollection).toBe(false);

            await renderDialog();

            expect(pageKeys()).not.toContain("teamCollection");
            expect(pageKeys()).toContain("experimental");
        });

        it("is offered in a Team Collection even with the feature off", async () => {
            respondAsTeamCollection();

            await renderDialog();

            expect(pageKeys()).toContain("teamCollection");
            expect(
                teamCollectionPageElement("administrators")?.textContent,
            ).toBe("boss@example.com");
        });

        it("is not offered in a collection holding a book downloaded for editing", async () => {
            respondWith({
                ...settingsResponse,
                editingABlorgBook: true,
                values: {
                    ...initialValues,
                    experimental: { "team-collections": true, tables: false },
                },
            });

            await renderDialog();

            expect(pageKeys()).not.toContain("teamCollection");
        });

        it("saves the administrators as typed, trimmed, and restarts", async () => {
            respondAsTeamCollection();
            await renderDialog();
            expect(okButtonLabel()).toBe("OK");

            click("type-valid-administrators");
            expect(
                teamCollectionPageElement("administrators-error")?.textContent,
            ).toBe("");
            expect(okButtonLabel()).toBe("Restart");
            click("dialog-ok");

            expect(mockPostJson.mock.calls[0][1]).toEqual({
                values: {
                    ...initialValues,
                    teamCollection: {
                        administrators: "a@example.com, b@example.com",
                        createInFolder: "",
                    },
                },
                restartRequired: true,
            });
        });

        it("will not save administrators that are not valid, and says why", async () => {
            respondAsTeamCollection();
            await renderDialog();
            expect(okButton().disabled).toBe(false);

            click("type-invalid-administrators");

            expect(okButton().disabled).toBe(true);
            expect(
                teamCollectionPageElement("administrators-error")?.textContent,
            ).toContain("valid administrator email addresses");
        });

        it("does not check the administrators of a collection that is not a Team Collection", async () => {
            respondReadyToCreate();
            expect(initialValues.teamCollection.administrators).toBe("");

            await renderDialog();

            expect(okButton().disabled).toBe(false);
            expect(
                teamCollectionPageElement("administrators-error")?.textContent,
            ).toBe("");
        });

        it("creates a Team Collection in the chosen folder only when the settings are saved", async () => {
            respondReadyToCreate();
            await renderDialog();
            expect(teamCollectionPageElement("create-dialog")).toBeNull();

            click("create-team-collection");
            expect(
                teamCollectionPageElement("create-dialog-folder")?.textContent,
            ).toBe(kDropboxFolder);
            click("create-dialog-ok");

            expect(teamCollectionPageElement("create-dialog")).toBeNull();
            expect(
                teamCollectionPageElement("pending-create-folder")?.textContent,
            ).toBe(kDropboxFolder);
            expect(mockPostJson).not.toHaveBeenCalled();
            expect(okButtonLabel()).toBe("Restart");
            click("dialog-ok");

            expect(mockPostJson.mock.calls[0][1].values.teamCollection).toEqual(
                {
                    administrators: "",
                    createInFolder: kDropboxFolder,
                },
            );
            expect(mockPostJson.mock.calls[0][1].restartRequired).toBe(true);
        });

        it("forgets the folder if the Create dialog is cancelled", async () => {
            respondReadyToCreate();
            await renderDialog();

            click("create-team-collection");
            click("create-dialog-cancel");

            expect(teamCollectionPageElement("create-dialog")).toBeNull();
            expect(
                teamCollectionPageElement("pending-create-folder")?.textContent,
            ).toBe("");
            expect(okButtonLabel()).toBe("OK");
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

        const tablesCheckbox = () => {
            const checkbox = container.querySelector(
                '[data-testid="configr-page"][data-page-key="experimental"] [data-path="experimental.tables"]',
            ) as HTMLInputElement | null;
            if (!checkbox) {
                throw new Error("The Experimental page has no Tables checkbox");
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

        it("offers Tables, with its subscription badge, when the tier allows it", async () => {
            await renderDialog();

            expect(tablesCheckbox().disabled).toBe(false);
            const badgeFeatures = Array.from(
                container.querySelectorAll(
                    '[data-page-key="experimental"] [data-testid="subscription-badge"]',
                ),
            ).map((badge) => badge.getAttribute("data-feature"));
            expect(badgeFeatures).toContain("Table");
        });

        it("disables Tables when the tier does not include it", async () => {
            tableFeature.enabled = false;

            await renderDialog();

            expect(tablesCheckbox().disabled).toBe(true);
            expect(teamCollectionsCheckbox().disabled).toBe(false);
        });

        it("disables Tables until the subscription check has answered", async () => {
            tableFeature.loaded = false;

            await renderDialog();

            expect(tablesCheckbox().disabled).toBe(true);
        });
    });
});
