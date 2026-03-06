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
import { useL10n } from "../../react_components/l10nHooks";
import { ShowEditViewDialog } from "../editViewFrame";
import { ElementAttributeSnapshot } from "../../utils/ElementAttributeSnapshot";
import { useGetFeatureStatus } from "../../react_components/featureStatus";
import {
    arePageSettingsEquivalent,
    applyPageSettings,
    getCurrentPageElement,
    getCurrentPageSettings,
    IPageSettings,
    parsePageSettingsFromConfigrValue,
    usePageSettingsAreaDefinition,
} from "./PageSettingsConfigrPages";
import { useBookSettingsAreaDefinition } from "./BookSettingsConfigrPages";

let isOpenAlready = false;
const kBookSettingsDialogWidthPx = 900;
const kBookSettingsDialogHeightPx = 720;

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

type IBookSettingsDialogValues = IBookSettings & IPageSettings;

// Stuff we get from the book/settings/overrides api.
// The branding and xmatter objects contain the corresponding settings,
// using the same keys as appearance.json. Currently the values are all
// booleans.
interface IOverrideInformation {
    branding: object;
    xmatter: object;
    brandingName: string;
    xmatterName: string;
}

export const BookSettingsDialog: React.FunctionComponent<{
    initiallySelectedGroupIndex?: number;
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
        const xmatterOverride: T | undefined =
            overrideInformation?.xmatter?.[subPath];
        const brandingOverride = overrideInformation?.branding?.[subPath];
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

    const [settings, setSettings] = React.useState<IBookSettings | undefined>(
        undefined,
    );

    const [pageSettings, setPageSettings] = React.useState<
        IPageSettings | undefined
    >(undefined);

    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState<
        ConfigrValues | undefined
    >(undefined);
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

    const [appearanceDisabled, setAppearanceDisabled] = React.useState(false);

    // We use state here to allow the dialog UI to update without permanently changing the settings
    // and getting notified of those changes. The changes are persisted when the user clicks OK.
    const [theme, setTheme] = React.useState("");
    const [firstPossiblyLegacyCss, setFirstPossiblyLegacyCss] =
        React.useState("");
    const [migratedTheme, setMigratedTheme] = React.useState("");

    const initialPageAttributeSnapshot = React.useRef<
        ElementAttributeSnapshot | undefined
    >(undefined);

    React.useEffect(() => {
        if (settingsString === "{}") {
            return; // leave settings as undefined
        }
        if (typeof settingsString === "string") {
            setSettings(JSON.parse(settingsString));
        } else {
            setSettings(settingsString);
        }
    }, [settingsString]);

    React.useEffect(() => {
        setPageSettings(getCurrentPageSettings());
        initialPageAttributeSnapshot.current =
            ElementAttributeSnapshot.fromElement(getCurrentPageElement());
    }, []);

    React.useEffect(() => {
        return () => {
            setDialogVisibleWhileColorPickerOpen(false);
        };
    }, [setDialogVisibleWhileColorPickerOpen]);

    React.useEffect(() => {
        setFirstPossiblyLegacyCss(
            appearanceUIOptions?.firstPossiblyLegacyCss ?? "",
        );
        setMigratedTheme(appearanceUIOptions?.migratedTheme ?? "");
    }, [appearanceUIOptions]);

    const bookSettingsTitle = useL10n(
        "Book and Page Settings",
        "BookAndPageSettings.Title",
    );

    React.useEffect(() => {
        if (settings?.appearance) {
            const liveAppearance =
                (settingsToReturnLater?.["appearance"] as
                    | IAppearanceSettings
                    | undefined) ?? settings.appearance;
            // when we're in legacy, we're just going to disable all the appearance controls
            setAppearanceDisabled(
                liveAppearance?.cssThemeName === "legacy-5-6",
            );
            setTheme(liveAppearance?.cssThemeName ?? "");
        }
    }, [settings, settingsToReturnLater]);

    const deleteCustomBookStyles = () => {
        post(
            `book/settings/deleteCustomBookStyles?file=${firstPossiblyLegacyCss}`,
        );
        setFirstPossiblyLegacyCss("");
        setMigratedTheme("");
    };

    const tierAllowsFullPageCoverImage =
        useGetFeatureStatus("fullPageCoverImage")?.enabled;

    const tierAllowsFullBleed = useGetFeatureStatus("PrintshopReady")?.enabled;

    const closeDialogAndClearOpenFlag = React.useCallback(() => {
        isOpenAlready = false;
        closeDialog();
    }, [closeDialog]);

    const cancelAndCloseDialog = React.useCallback(() => {
        if (initialPageAttributeSnapshot.current) {
            initialPageAttributeSnapshot.current.restoreToElement(
                getCurrentPageElement(),
            );
        }
        closeDialogAndClearOpenFlag();
    }, [closeDialogAndClearOpenFlag]);

    function saveSettingsAndCloseDialog() {
        const latestSettings = settingsToReturnLater;
        if (latestSettings) {
            applyPageSettings(
                parsePageSettingsFromConfigrValue(latestSettings),
            );

            const settingsToPost =
                removePageSettingsFromConfigrSettings(latestSettings);
            // If nothing changed, we don't get any...and don't need to make this call.
            postJson("book/settings", settingsToPost);
        }

        closeDialogAndClearOpenFlag();
        // todo: how do we make the pageThumbnailList reload? It's in a different browser, so
        // we can't use a global. It listens to websocket, but we currently can only listen,
        // we cannot send.
    }

    const bookSettingsArea = useBookSettingsAreaDefinition({
        appearanceDisabled,
        tierAllowsFullPageCoverImage,
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

    return (
        <BloomDialog
            css={css`
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

                    // HACK: TODO get the divs to all just maximize height until the available space is used or we don't need anymore height
                    form {
                        overflow-y: auto;
                        height: 600px;
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
                            const isInitialConfigrEcho =
                                !settingsToReturnLater &&
                                !!pageSettings &&
                                arePageSettingsEquivalent(
                                    parsedPageSettings,
                                    pageSettings,
                                );

                            // Config-r may call onChange while rendering, so defer state updates.
                            window.setTimeout(() => {
                                setSettingsToReturnLater(s);
                            }, 0);

                            if (isInitialConfigrEcho) {
                                return;
                            }

                            applyPageSettings(parsedPageSettings);
                        }}
                        initiallySelectedTopLevelPageIndex={
                            props.initiallySelectedGroupIndex
                        }
                    >
                        <ConfigrArea
                            label={bookSettingsArea.label}
                            pageKey={bookSettingsArea.pageKey}
                            content={bookSettingsArea.content}
                        >
                            {bookSettingsArea.pages}
                        </ConfigrArea>
                        <ConfigrArea
                            label={pageSettingsArea.label}
                            pageKey={pageSettingsArea.pageKey}
                            content={pageSettingsArea.content}
                        >
                            {pageSettingsArea.pages}
                        </ConfigrArea>
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

export function showBookSettingsDialog(initiallySelectedGroupIndex?: number) {
    // once Bloom's tab bar is also in react, it won't be possible
    // to open another copy of this without closing it first, but
    // for now, we need to prevent that.
    if (!isOpenAlready) {
        isOpenAlready = true;
        ShowEditViewDialog(
            <BookSettingsDialog
                initiallySelectedGroupIndex={initiallySelectedGroupIndex}
            />,
        );
    }
}
