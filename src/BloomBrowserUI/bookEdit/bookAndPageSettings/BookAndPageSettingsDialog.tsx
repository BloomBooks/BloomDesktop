import { css } from "@emotion/react";
import { ConfigrArea, ConfigrPane, ConfigrValues } from "@sillsdev/config-r";
import * as React from "react";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../../react_components/BloomDialog/commonDialogComponents";
import {
    post,
    postJson,
    useApiBoolean,
    useApiObject,
    useApiStringState,
} from "../../utils/bloomApi";
import { whenBloomPageIsReady } from "../../utils/shared";
import { useL10n } from "../../react_components/l10nHooks";
import { getWorkspaceBundleExports } from "../js/workspaceFrames";
import { ElementAttributeSnapshot } from "../../utils/ElementAttributeSnapshot";
import { useGetFeatureStatus } from "../../react_components/featureStatus";
import {
    applyChangedPageSettings,
    getCurrentPageElement,
    getChangedPageSettings,
    getCurrentPageSettings,
    IPageSettings,
    parsePageSettingsFromConfigrValue,
    usePageSettingsAreaDefinition,
} from "./PageSettingsConfigrPages";
import { isLegacyThemeName } from "./appearanceThemeUtils";
import { useBookSettingsAreaDefinition } from "./BookSettingsConfigrPages";

let isOpenAlready = false;
const kBookSettingsDialogWidthPx = 900;
const kBookSettingsDialogHeightPx = 720;
const kConfigrPaneClassName = "book-page-settings-configr-pane";

type IPageStyle = { label: string; value: string };
type IPageStyles = Array<IPageStyle>;
type IAppearanceUIOptions = {
    firstPossiblyLegacyCss?: string;
    migratedTheme?: string;
    themeNames: IPageStyles;
};

// Stuff we find in the appearance property of the object we get from the book/settings api.
// Not yet complete
export interface IAppearanceSettings {
    cssThemeName: string;
}

// Stuff we get from the book/settings api.
// Not yet complete
export interface IBookSettings {
    appearance?: IAppearanceSettings;
    firstPossiblyLegacyCss?: string;
}

// Stuff we get from the book/settings/overrides api.
// The branding and xmatter objects contain the corresponding settings,
// using the same keys as appearance.json. Currently the values are all
// booleans.
interface IOverrideInformation {
    branding: Record<string, unknown>;
    xmatter: Record<string, unknown>;
    brandingName: string;
    xmatterName: string;
}

export const BookAndPageSettingsDialog: React.FunctionComponent<{
    initiallySelectedPageKey?: string;
}> = (props) => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false,
    });

    const appearanceUIOptions: IAppearanceUIOptions =
        useApiObject<IAppearanceUIOptions>(
            "book/settings/appearanceUIOptions",
            {
                themeNames: [],
            },
        );

    // If we pass a new default value to useApiObject on every render, it will query the host
    // every time and then set the result, which triggers a new render, making an infinite loop.
    const defaultOverrides = React.useMemo(() => {
        return {
            xmatter: {},
            branding: {},
            xmatterName: "",
            brandingName: "",
        };
    }, []);

    const overrideInformation: IOverrideInformation | undefined =
        useApiObject<IOverrideInformation>(
            "book/settings/overrides",
            defaultOverrides,
        );

    const [pageSizeSupportsFullBleed] = useApiBoolean(
        "book/settings/pageSizeSupportsFullBleed",
        true,
    );

    const xmatterLockedBy = useL10n(
        "Locked by {0} Front/Back matter",
        "BookSettings.LockedByXMatter",
        "",
        overrideInformation?.xmatterName,
    );

    const brandingLockedBy = useL10n(
        "Locked by {0} Branding",
        "BookSettings.LockedByBranding",
        "",
        overrideInformation?.brandingName,
    );

    // This is a helper function to make it easier to pass the override information
    function getAdditionalProps<T>(subPath: string): {
        path: string;
        overrideValue: T;
        overrideDescription?: string;
    } {
        // some properties will be overridden by branding and/or xmatter
        const xmatterOverride = overrideInformation?.xmatter?.[subPath] as
            | T
            | undefined;
        const brandingOverride = overrideInformation?.branding?.[subPath] as
            | T
            | undefined;
        const override = xmatterOverride ?? brandingOverride;
        // nb: xmatterOverride can be boolean, hence the need to spell out !==undefined
        let description =
            xmatterOverride !== undefined ? xmatterLockedBy : undefined;
        if (!description) {
            // xmatter wins if both are present
            description =
                brandingOverride !== undefined ? brandingLockedBy : undefined;
        }
        // make a an object that can be spread as props in any of the Configr controls
        return {
            path: "appearance." + subPath,
            overrideValue: override as T,
            // if we're disabling all appearance controls (e.g. because we're in legacy), don't list a second reason for this overload
            overrideDescription: appearanceDisabled ? "" : description,
        };
    }

    const [settingsString] = useApiStringState(
        "book/settings",
        "{}",
        () => propsForBloomDialog.open,
    );

    const [pageSettings, setPageSettings] = React.useState<
        IPageSettings | undefined
    >(undefined);
    const [currentPageIsXMatter, setCurrentPageIsXMatter] =
        React.useState(false);

    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState<
        ConfigrValues | undefined
    >(undefined);
    const latestSettingsRef = React.useRef<ConfigrValues | undefined>(
        undefined,
    );
    const dialogRef = React.useRef<HTMLDivElement>(null);

    const setDialogVisibleWhileColorPickerOpen = React.useCallback(
        (open: boolean) => {
            const dialogRoot = dialogRef.current?.closest(".MuiDialog-root");
            if (!(dialogRoot instanceof HTMLElement)) {
                return;
            }
            if (open) {
                dialogRoot.style.visibility = "hidden";
                dialogRoot.style.pointerEvents = "none";
            } else {
                dialogRoot.style.visibility = "";
                dialogRoot.style.pointerEvents = "";
            }
        },
        [],
    );

    const removePageSettingsFromConfigrSettings = (
        settingsValue: ConfigrValues,
    ): IBookSettings => {
        const settingsWithoutPage = {
            ...settingsValue,
        } as Record<string, unknown>;
        delete settingsWithoutPage["page"];
        return settingsWithoutPage as IBookSettings;
    };

    const haveBookSettingsChanged = (
        currentSettingsValue: ConfigrValues,
    ): boolean => {
        const currentBookSettings =
            removePageSettingsFromConfigrSettings(currentSettingsValue);
        return (
            JSON.stringify(currentBookSettings) !==
            JSON.stringify(settings ?? {})
        );
    };

    const settings: IBookSettings | undefined = React.useMemo(() => {
        if (settingsString === "{}") {
            return undefined;
        }
        if (typeof settingsString === "string") {
            return JSON.parse(settingsString) as IBookSettings;
        }
        return settingsString as unknown as IBookSettings;
    }, [settingsString]);

    const configrInitialValues: ConfigrValues | undefined =
        React.useMemo(() => {
            if (!settings || !pageSettings) {
                return undefined;
            }

            return {
                ...settings,
                page: pageSettings.page,
            } as unknown as ConfigrValues;
        }, [settings, pageSettings]);

    const [deletedCustomBookStyles, setDeletedCustomBookStyles] =
        React.useState(false);

    const initialPageAttributeSnapshot = React.useRef<
        ElementAttributeSnapshot | undefined
    >(undefined);

    // This effect synchronizes dialog state with the editable page iframe, which is outside React.
    // The iframe body can exist slightly before .bloom-page is inserted, so wait for that element
    // once on mount before snapshotting settings used by Cancel and the initial config-r values.
    React.useEffect(() => {
        return whenBloomPageIsReady((currentPageElement) => {
            setPageSettings(getCurrentPageSettings());
            initialPageAttributeSnapshot.current =
                ElementAttributeSnapshot.fromElement(currentPageElement);
            setCurrentPageIsXMatter(
                currentPageElement.classList.contains("bloom-frontMatter") ||
                    currentPageElement.classList.contains("bloom-backMatter"),
            );
        });
    }, []);

    // If the dialog unmounts while a nested color picker is open, clear the shared visibility flag
    // so the parent dialog does not stay hidden after this component is gone.
    React.useEffect(() => {
        return () => {
            setDialogVisibleWhileColorPickerOpen(false);
        };
    }, [setDialogVisibleWhileColorPickerOpen]);

    const bookSettingsTitle = useL10n(
        "Book and Page Settings",
        "BookAndPageSettings.Title",
    );

    const firstPossiblyLegacyCss = deletedCustomBookStyles
        ? ""
        : (appearanceUIOptions?.firstPossiblyLegacyCss ?? "");
    const migratedTheme = deletedCustomBookStyles
        ? ""
        : (appearanceUIOptions?.migratedTheme ?? "");
    const liveAppearance =
        (settingsToReturnLater?.["appearance"] as
            | IAppearanceSettings
            | undefined) ?? settings?.appearance;
    const appearanceDisabled = isLegacyThemeName(liveAppearance?.cssThemeName);

    // We keep theme as a render-time value from the latest working settings so the dialog reflects
    // Configr edits immediately without a second state synchronization layer.
    const theme = liveAppearance?.cssThemeName ?? "";

    const deleteCustomBookStyles = () => {
        post(
            `book/settings/deleteCustomBookStyles?file=${firstPossiblyLegacyCss}`,
        );
        setDeletedCustomBookStyles(true);
    };

    const tierAllowsFullBleed = useGetFeatureStatus("PrintshopReady")?.enabled;

    const closeDialogAndClearOpenFlag = React.useCallback(() => {
        latestSettingsRef.current = undefined;
        isOpenAlready = false;
        closeDialog();
    }, [closeDialog]);

    const cancelAndCloseDialog = React.useCallback(() => {
        try {
            if (initialPageAttributeSnapshot.current) {
                initialPageAttributeSnapshot.current.restoreToElement(
                    getCurrentPageElement(),
                );
            }
        } finally {
            closeDialogAndClearOpenFlag();
        }
    }, [closeDialogAndClearOpenFlag]);

    function saveSettingsAndCloseDialog() {
        try {
            const latestSettings =
                latestSettingsRef.current ?? settingsToReturnLater;
            if (latestSettings) {
                const parsedPageSettings =
                    parsePageSettingsFromConfigrValue(latestSettings);
                const changedPageSettings = pageSettings
                    ? getChangedPageSettings(pageSettings, parsedPageSettings)
                    : undefined;

                if (changedPageSettings) {
                    applyChangedPageSettings(changedPageSettings);
                }

                if (haveBookSettingsChanged(latestSettings)) {
                    const settingsToPost =
                        removePageSettingsFromConfigrSettings(latestSettings);
                    postJson("book/settings", settingsToPost);
                }
            }
        } finally {
            closeDialogAndClearOpenFlag();
        }
        // todo: how do we make the pageThumbnailList reload? It's in a different browser, so
        // we can't use a global. It listens to websocket, but we currently can only listen,
        // we cannot send.
    }

    const bookSettingsArea = useBookSettingsAreaDefinition({
        appearanceDisabled,
        tierAllowsFullBleed,
        pageSizeSupportsFullBleed,
        settings,
        settingsToReturnLater,
        getAdditionalProps,
        firstPossiblyLegacyCss,
        theme,
        migratedTheme,
        deleteCustomBookStyles,
        saveSettingsAndCloseDialog,
        onColorPickerVisibilityChanged: setDialogVisibleWhileColorPickerOpen,
        themeNames: appearanceUIOptions.themeNames,
    });

    const pageSettingsArea = usePageSettingsAreaDefinition({
        onColorPickerVisibilityChanged: setDialogVisibleWhileColorPickerOpen,
    });

    const initiallySelectedConfigrPageKey = currentPageIsXMatter
        ? undefined
        : props.initiallySelectedPageKey;

    const configrAreas = [
        <ConfigrArea
            key={bookSettingsArea.pageKey}
            label={bookSettingsArea.label}
            pageKey={bookSettingsArea.pageKey}
            content={bookSettingsArea.content}
        >
            {bookSettingsArea.pages}
        </ConfigrArea>,
    ];

    if (!currentPageIsXMatter) {
        configrAreas.push(
            <ConfigrArea
                key={pageSettingsArea.pageKey}
                label={pageSettingsArea.label}
                pageKey={pageSettingsArea.pageKey}
                content={pageSettingsArea.content}
            >
                {pageSettingsArea.pages}
            </ConfigrArea>,
        );
    }

    return (
        <BloomDialog
            css={css`
                height: 100%;
                box-sizing: border-box;

                .MuiDialog-paper {
                    width: ${kBookSettingsDialogWidthPx}px;
                    height: ${kBookSettingsDialogHeightPx}px;
                    max-width: none;
                    max-height: none;
                }
            `}
            ref={dialogRef}
            {...propsForBloomDialog}
            onClose={() => cancelAndCloseDialog()}
            onCancel={() => cancelAndCloseDialog()}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={bookSettingsTitle} />
            <DialogMiddle
                css={css`
                    &:first-child {
                        margin-top: 0; // override the default that sees a lack of a title and adds a margin
                    }
                    overflow-y: hidden;
                    min-height: 0;

                    .${kConfigrPaneClassName} {
                        height: 100%;
                        min-height: 0;
                    }

                    // Let config-r consume the available dialog height in both the page form and
                    // the area-description states so the button row stays pinned to the bottom.
                    form {
                        overflow-y: auto;
                        height: 100%;
                        min-height: 0;
                        width: 100%;
                        box-sizing: border-box;
                        #groups {
                            margin-right: 10px; // make room for the scrollbar
                        }
                    }

                    a {
                        color: ${kBloomBlue};
                    }
                `}
            >
                {configrInitialValues && (
                    <ConfigrPane
                        className={kConfigrPaneClassName}
                        label={bookSettingsTitle}
                        initialValues={configrInitialValues}
                        themeOverrides={{
                            // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
                            palette: {
                                primary: { main: kBloomBlue },
                            },
                        }}
                        showAppBar={false}
                        showJson={false}
                        onChange={(s) => {
                            const parsedPageSettings =
                                parsePageSettingsFromConfigrValue(s);
                            const changedPageSettings = pageSettings
                                ? getChangedPageSettings(
                                      pageSettings,
                                      parsedPageSettings,
                                  )
                                : undefined;

                            // Config-r may call onChange while rendering, so defer state updates.
                            latestSettingsRef.current = s;
                            window.setTimeout(() => {
                                setSettingsToReturnLater(s);
                            }, 0);

                            if (
                                !pageSettings ||
                                !initialPageAttributeSnapshot.current
                            ) {
                                return;
                            }

                            initialPageAttributeSnapshot.current.restoreToElement(
                                getCurrentPageElement(),
                            );

                            if (!changedPageSettings) {
                                return;
                            }

                            applyChangedPageSettings(changedPageSettings);
                        }}
                        initiallySelectedTopLevelPageKey={
                            initiallySelectedConfigrPageKey
                        }
                    >
                        {configrAreas}
                    </ConfigrPane>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={saveSettingsAndCloseDialog}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export function showBookSettingsDialog(initiallySelectedPageKey?: string) {
    // once Bloom's tab bar is also in react, it won't be possible
    // to open another copy of this without closing it first, but
    // for now, we need to prevent that.
    if (!isOpenAlready) {
        isOpenAlready = true;
        try {
            getWorkspaceBundleExports().ShowEditViewDialog(
                <BookAndPageSettingsDialog
                    initiallySelectedPageKey={initiallySelectedPageKey}
                />,
            );
        } catch (error) {
            isOpenAlready = false;
            throw error;
        }
    }
}
