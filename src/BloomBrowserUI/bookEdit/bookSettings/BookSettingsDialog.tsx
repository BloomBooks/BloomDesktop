import { css } from "@emotion/react";
import { ListItem, Slider, Typography } from "@mui/material";
import {
    ConfigrPane,
    ConfigrGroup,
    ConfigrSubgroup,
    ConfigrCustomStringInput,
    ConfigrCustomNumberInput,
    ConfigrColorPicker,
    ConfigrInput,
    ConfigrCustomObjectInput,
    ConfigrBoolean,
    ConfigrSelect
} from "@sillsdev/config-r";
import React = require("react");
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogTitle
} from "../../react_components/BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton
} from "../../react_components/BloomDialog/commonDialogComponents";
import {
    BloomPalette,
    getDefaultColorsFromPalette
} from "../../react_components/color-picking/bloomPalette";
import ColorPicker from "../../react_components/color-picking/colorPicker";
import {
    ColorDisplayButton,
    DialogResult
} from "../../react_components/color-picking/colorPickerDialog";
import { IColorInfo } from "../../react_components/color-picking/colorSwatch";
import {
    post,
    postJson,
    postString,
    useApiObject,
    useApiStringState
} from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../editViewFrame";
import { useL10n } from "../../react_components/l10nHooks";
import { Div, P } from "../../react_components/l10nComponents";
import { NoteBox, WarningBox } from "../../react_components/boxes";
import { default as TrashIcon } from "@mui/icons-material/Delete";
import { PWithLink } from "../../react_components/pWithLink";
import { FieldVisibilityGroup } from "./FieldVisibilityGroup";
import { StyleAndFontTable } from "./StyleAndFontTable";

let isOpenAlready = false;

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
    appearance: IAppearanceSettings;
    firstPossiblyLegacyCss?: string;
}

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
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false
    });

    const appearanceUIOptions: IAppearanceUIOptions = useApiObject<
        IAppearanceUIOptions
    >("book/settings/appearanceUIOptions", {
        themeNames: []
    });

    const overrideInformation: IOverrideInformation | undefined = useApiObject<
        IOverrideInformation
    >("book/settings/overrides", {
        xmatter: {},
        branding: {},
        xmatterName: "",
        brandingName: ""
    });

    const xmatterLockedBy = useL10n(
        "Locked by {0} Front/Back matter",
        "BookSettings.LockedByXMatter",
        "",
        overrideInformation?.xmatterName
    );

    const brandingLockedBy = useL10n(
        "Locked by {0} Branding",
        "BookSettings.LockedByBranding",
        "",
        overrideInformation?.brandingName
    );

    const coverLabel = useL10n("Cover", "BookSettings.CoverGroupLabel");
    const contentPagesLabel = useL10n(
        "Content Pages",
        "BookSettings.ContentPagesGroupLabel"
    );
    const languagesToShowNormalSubgroupLabel = useL10n(
        "Languages to show in normal text boxes",
        "BookSettings.NormalTextBoxLangsLabel",
        ""
    );
    const themeLabel = useL10n("Page Theme", "BookSettings.PageThemeLabel", "");
    const themeDescription = useL10n(
        "", // will be translated or the English will come from the xliff
        "BookSettings.Theme.Description"
    );
    /* can't use this yet. See https://issues.bloomlibrary.org/youtrack/issue/BL-13094/Enable-links-in-Config-r-Descriptions
    const pageThemeDescriptionElement = (
        <PWithLink
            href="https://docs.bloomlibrary.org/incompatible-custombookstyles"
            l10nKey="BookSettings.Theme.Description"
            l10nComment="The text inside the [Page Themes Catalog] will become a link to a website."
        >
            Page Themes are a bundle of margins, borders, and other page settings. For information about each theme, see [Page Themes Catalog].
        </PWithLink>
    );
    */

    const coverBackgroundColorLabel = useL10n(
        "Background Color",
        "Common.BackgroundColor"
    );

    const whatToShowOnCoverLabel = useL10n(
        "Front Cover",
        "BookSettings.WhatToShowOnCover"
    );

    const showLanguageNameLabel = useL10n(
        "Show Language Name",
        "BookSettings.ShowLanguageName"
    );
    const showTopicLabel = useL10n("Show Topic", "BookSettings.ShowTopic");
    const frontAndBackMatterLabel = useL10n(
        "Front & Back Matter",
        "BookSettings.FrontAndBackMatter"
    );
    const pageNumbersLabel = useL10n(
        "Page Numbers",
        "BookSettings.PageNumbers"
    );
    const showPageNumbersLabel = useL10n(
        "Show Page Numbers",
        "BookSettings.ShowPageNumbers"
    );
    const frontAndBackMatterDescription = useL10n(
        "Normally, books use the front & back matter pack that is chosen for the entire collection. Using this setting, you can cause this individual book to use a different one.",
        "BookSettings.FrontAndBackMatter.Description"
    );
    const resolutionLabel = useL10n("Resolution", "BookSettings.Resolution");
    const bloomPubLabel = useL10n("eBooks", "PublishTab.bloomPUBButton"); // reuse the same string localized for the Publish tab

    const advancedLayoutLabel = useL10n(
        "Advanced Layout",
        "BookSettings.AdvancedLayoutLabel"
    );
    const textPaddingLabel = useL10n(
        "Text Padding",
        "BookSettings.TopLevelTextPaddingLabel"
    );
    const textPaddingDescription = useL10n(
        "Smart spacing around text boxes. Works well for simple pages, but may not suit custom layouts.",
        "BookSettings.TopLevelTextPadding.Description"
    );
    const textPaddingDefaultLabel = useL10n(
        "Default (set by Theme)",
        "BookSettings.TopLevelTextPadding.DefaultLabel"
    );
    const textPadding1emLabel = useL10n(
        "1em (font size)",
        "BookSettings.TopLevelTextPadding.1emLabel"
    );

    // This is a helper function to make it easier to pass the override information
    function getAdditionalProps<T>(
        subPath: string
    ): {
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
            overrideDescription: appearanceDisabled ? "" : description
        };
    }

    const [settingsString] = useApiStringState(
        "book/settings",
        "{}",
        () => propsForBloomDialog.open
    );

    const [settings, setSettings] = React.useState<object | undefined>(
        undefined
    );

    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState(
        ""
    );

    const [appearanceDisabled, setAppearanceDisabled] = React.useState(false);

    // We use state here to allow the dialog UI to update without permanently changing the settings
    // and getting notified of  those changes. The changes are persisted when the user clicks OK
    // (except for the button to delete customBookStyles.css, which is done immediately).
    // A downside of this is that when we delete customBookStyles.css, we don't know whether
    // the result will be no conflicts or that customCollectionStyles.css will now be the
    // firstPossiblyLegacyCss. For now it just behaves as if there are now no conflicts.
    // One possible approach is to have the server return the new firstPossiblyLegacyCss
    // as the result of the deleteCustomBookStyles call.
    const [theme, setTheme] = React.useState("");
    const [firstPossiblyLegacyCss, setFirstPossiblyLegacyCss] = React.useState(
        ""
    );
    const [migratedTheme, setMigratedTheme] = React.useState("");

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
        setFirstPossiblyLegacyCss(
            appearanceUIOptions?.firstPossiblyLegacyCss ?? ""
        );
        setMigratedTheme(appearanceUIOptions?.migratedTheme ?? "");
    }, [appearanceUIOptions]);

    const bookSettingsTitle = useL10n("Book Settings", "BookSettings.Title");
    React.useEffect(() => {
        if (settings && (settings as any).appearance) {
            const liveSettings = settingsToReturnLater || settings;
            // when we're in legacy, we're just going to disable all the appearance controls
            setAppearanceDisabled(
                (liveSettings as any)?.appearance?.cssThemeName === "legacy-5-6"
            );
            setTheme((liveSettings as IBookSettings)?.appearance?.cssThemeName);
        }
    }, [settings, settingsToReturnLater]);

    const deleteCustomBookStyles = () => {
        post(
            `book/settings/deleteCustomBookStyles?file=${firstPossiblyLegacyCss}`
        );
        setFirstPossiblyLegacyCss("");
        setMigratedTheme("");
    };

    function saveSettingsAndCloseDialog() {
        if (settingsToReturnLater) {
            // If nothing changed, we don't get any...and don't need to make this call.
            postJson("book/settings", settingsToReturnLater);
        }
        isOpenAlready = false;
        closeDialog();
        // todo: how do we make the pageThumbnailList reload? It's in a different browser, so
        // we can't use a global. It listens to websocket, but we currently can only listen,
        // we cannot send.
    }

    return (
        <BloomDialog
            css={css`
                // TODO: we would like a background color, but setting it here makes the dialog's backdrop turn that color!
                // conceivably we could wrap the current children in a div that just provides the background color.
                //background-color: #fbf8ff;
            `}
            // cssForDialogContents={css`
            //     background-color: #e4f1f3;
            //     height: 500px;
            // `}
            {...propsForBloomDialog}
            onClose={closeDialog}
            onCancel={() => {
                isOpenAlready = false;
                closeDialog();
            }}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={bookSettingsTitle} />
            <DialogMiddle
                css={css`
                    &:first-child {
                        margin-top: 0; // override the default that sees a lack of a title and adds a margin
                    }
                    overflow-y: auto; // This displays a scrollbar only when needed.  'scroll' always shows one.
                    // hack might need this: overflow-y: hidden; // but I need help on the css, so we're going with this for now

                    // HACK: TODO get the divs to all just maximize height until the available space is used or we don't need anymore height
                    form {
                        overflow-y: scroll;
                        height: 600px;
                        // This odd width was chosen to make the customBookStyles alert box format nicely.
                        // See BL-12956. It's not that important, but I don't think anything else is affected
                        // much by the exact witdh.
                        width: 638px;
                        #groups {
                            margin-right: 10px; // make room for the scrollbar
                        }
                    }

                    a {
                        color: ${kBloomBlue};
                    }
                `}
            >
                {settings && (
                    <ConfigrPane
                        label={bookSettingsTitle}
                        initialValues={settings}
                        showAllGroups={true}
                        //themeOverrides={lightTheme}
                        themeOverrides={{
                            // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
                            palette: {
                                primary: { main: kBloomBlue }
                            }
                        }}
                        showAppBar={false}
                        showJson={false}
                        onChange={s => {
                            setSettingsToReturnLater(s);
                            //setSettings(s);
                        }}
                        selectedGroupIndex={props.initiallySelectedGroupIndex}
                    >
                        <ConfigrGroup label={coverLabel} level={1}>
                            {appearanceDisabled && (
                                <NoteBox>
                                    <Div l10nKey="BookSettings.ThemeDisablesOptionsNotice">
                                        The selected page theme does not support
                                        the following settings.
                                    </Div>
                                </NoteBox>
                            )}
                            <ConfigrSubgroup
                                label={whatToShowOnCoverLabel}
                                path={`appearance`}
                            >
                                <FieldVisibilityGroup
                                    field="cover-title"
                                    labelFrame="Show Title in {0}"
                                    labelFrameL10nKey="BookSettings.ShowWrittenLanguageTitle"
                                    settings={settings}
                                    settingsToReturnLater={
                                        settingsToReturnLater
                                    }
                                    disabled={appearanceDisabled}
                                    L1MustBeTurnedOn={true}
                                    getAdditionalProps={getAdditionalProps}
                                />

                                <ConfigrBoolean
                                    label={showLanguageNameLabel}
                                    disabled={appearanceDisabled}
                                    {...getAdditionalProps<boolean>(
                                        `cover-languageName-show`
                                    )}
                                />
                                <ConfigrBoolean
                                    label={showTopicLabel}
                                    disabled={appearanceDisabled}
                                    {...getAdditionalProps<boolean>(
                                        `cover-topic-show`
                                    )}
                                />
                            </ConfigrSubgroup>
                            <ConfigrSubgroup
                                label={"All Cover Pages"}
                                path={`appearance`}
                            >
                                <ConfigrCustomStringInput
                                    label={coverBackgroundColorLabel}
                                    control={ColorPickerForConfigr}
                                    disabled={appearanceDisabled}
                                    {...getAdditionalProps<string>(
                                        `cover-background-color`
                                    )}
                                />
                            </ConfigrSubgroup>
                            {/*

                            <ConfigrSubgroup
                                label={
                                    frontAndBackMatterLabel +
                                    "  (Not implemented yet)"
                                }
                                path={`appearance`}
                            >
                                <ConfigrSelect
                                    disabled={true}
                                    label={frontAndBackMatterLabel}
                                    path={`appearance.TODO`}
                                    options={[
                                        { label: "Page Saver", value: "TODO" }
                                    ]}
                                    description={frontAndBackMatterDescription}
                                />
                            </ConfigrSubgroup> */}
                        </ConfigrGroup>
                        <ConfigrGroup label={contentPagesLabel} level={1}>
                            {
                                // This group of four possible messages...sometimes none of them shows, so there are five options...
                                // is very similar to the one in BookInfoIndicator.tsx. If you change one, you may need to change the other.
                                // In particular, the logic for which to show and the text of the messages should be kept in sync.
                                // I'm not seeing a clean way to reuse the logic. Some sort of higher-order component might work,
                                // but I don't think the logic is complex enough to be worth it, when only used in two places.
                            }
                            {firstPossiblyLegacyCss && theme === "legacy-5-6" && (
                                <WarningBox>
                                    <MessageUsingLegacyThemeWithIncompatibleCss
                                        fileName={firstPossiblyLegacyCss}
                                    />
                                </WarningBox>
                            )}
                            {firstPossiblyLegacyCss ===
                                "customBookStyles.css" &&
                                theme !== "legacy-5-6" && (
                                    <NoteBox>
                                        <div>
                                            {migratedTheme ? (
                                                <MessageUsingMigratedThemeInsteadOfIncompatibleCss
                                                    fileName={
                                                        firstPossiblyLegacyCss
                                                    }
                                                />
                                            ) : (
                                                <MessageIgnoringIncompatibleCssCanDelete
                                                    fileName={
                                                        firstPossiblyLegacyCss
                                                    }
                                                />
                                            )}
                                            <div
                                                css={css`
                                                    display: flex;
                                                    align-items: center;
                                                    // The way it comes out in English, we'd be better off without this, or even
                                                    // some negative margin. But a translation may produce a last line of the
                                                    // main message
                                                    margin-top: 2px;
                                                    justify-content: flex-end;
                                                    &:hover {
                                                        cursor: pointer;
                                                    }
                                                `}
                                                onClick={() =>
                                                    deleteCustomBookStyles()
                                                }
                                            >
                                                <TrashIcon
                                                    id="trashIcon"
                                                    color="primary"
                                                />
                                                <Div
                                                    l10nKey="BookSettings.DeleteCustomBookStyles"
                                                    l10nParam0={
                                                        firstPossiblyLegacyCss
                                                    }
                                                    css={css`
                                                        color: ${kBloomBlue};
                                                    `}
                                                >
                                                    Delete{" "}
                                                    {firstPossiblyLegacyCss}
                                                </Div>
                                            </div>
                                        </div>
                                    </NoteBox>
                                )}
                            {firstPossiblyLegacyCss &&
                                firstPossiblyLegacyCss !==
                                    "customBookStyles.css" &&
                                theme !== "legacy-5-6" && (
                                    <NoteBox>
                                        <MessageIgnoringIncompatibleCss
                                            fileName={firstPossiblyLegacyCss}
                                        />
                                    </NoteBox>
                                )}
                            <ConfigrSubgroup label="" path={`appearance`}>
                                {/* Wrapping these two in a div prevents Config-R from sticking a divider between them */}
                                <div>
                                    <ConfigrSelect
                                        label={themeLabel}
                                        disabled={false}
                                        path={`appearance.cssThemeName`}
                                        options={appearanceUIOptions.themeNames.map(
                                            x => {
                                                return {
                                                    label: x.label,
                                                    value: x.value
                                                };
                                            }
                                        )}
                                        description={themeDescription}
                                    />
                                    {appearanceDisabled && (
                                        <NoteBox
                                            css={css`
                                                margin-left: 20px;
                                            `}
                                        >
                                            <Div l10nKey="BookSettings.ThemeDisablesOptionsNotice">
                                                The selected page theme does not
                                                support the following settings.
                                            </Div>
                                        </NoteBox>
                                    )}
                                </div>
                                <ConfigrBoolean
                                    label={showPageNumbersLabel}
                                    disabled={appearanceDisabled}
                                    {...getAdditionalProps<boolean>(
                                        `pageNumber-show`
                                    )}
                                />
                            </ConfigrSubgroup>
                            <ConfigrSubgroup
                                label={languagesToShowNormalSubgroupLabel}
                                path={`appearance`}
                            >
                                <FieldVisibilityGroup
                                    field="autoTextBox"
                                    labelFrame="Show {0}"
                                    labelFrameL10nKey="BookSettings.ShowContentLanguage"
                                    settings={settings}
                                    settingsToReturnLater={
                                        settingsToReturnLater
                                    }
                                    disabled={false}
                                    getAdditionalProps={getAdditionalProps}
                                />
                            </ConfigrSubgroup>
                            <ConfigrSubgroup
                                label={advancedLayoutLabel}
                                path={`appearance`}
                            >
                                <ConfigrSelect
                                    label={textPaddingLabel}
                                    options={[
                                        {
                                            label: textPaddingDefaultLabel,
                                            value: "" // use whatever the theme provides
                                        },
                                        { label: "0mm", value: "0mm" },
                                        { label: "2mm", value: "2mm" },
                                        { label: "4mm", value: "4mm" },
                                        {
                                            label: textPadding1emLabel,
                                            value: "1em"
                                        }
                                    ]}
                                    description={textPaddingDescription}
                                    {...getAdditionalProps<string>(
                                        `topLevel-text-padding`
                                    )}
                                />
                            </ConfigrSubgroup>
                        </ConfigrGroup>
                        <ConfigrGroup label={bloomPubLabel} level={1}>
                            {/* note that this is used for bloomPUB and ePUB, but we don't have separate settings so we're putting them in bloomPUB and leaving it to c# code to use it for ePUB as well. */}
                            <BloomResolutionSlider
                                label={resolutionLabel}
                                path={`publish.bloomPUB.imageSettings`}
                            />
                        </ConfigrGroup>
                        <ConfigrGroup label="Fonts" level={1}>
                            <NoteBox>
                                <div>
                                    <P l10nKey="BookSettings.Fonts.Problematic">
                                        When you publish a book to the web or as
                                        an ebook, Bloom will flag any
                                        problematic fonts. For example, we
                                        cannot legally host most Microsoft fonts
                                        on BloomLibrary.org.
                                    </P>
                                    <P l10nKey="BookSettings.Fonts.TableDescription">
                                        The following table shows where fonts
                                        have been used.
                                    </P>
                                </div>
                            </NoteBox>
                            <StyleAndFontTable
                                closeDialog={saveSettingsAndCloseDialog}
                            />
                        </ConfigrGroup>
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

type Resolution = {
    maxWidth: number;
    maxHeight: number;
};

const BloomResolutionSlider: React.FunctionComponent<React.PropsWithChildren<{
    path: string;
    label: string;
}>> = props => {
    return (
        <div>
            <ConfigrCustomObjectInput<Resolution>
                control={BloomResolutionSliderInner}
                {...props}
            ></ConfigrCustomObjectInput>
            <Div
                l10nKey="BookSettings.eBook.Image.MaxResolution.Directions"
                css={css`
                    padding: 0 10px;
                    font-size: 9pt;
                `}
            >
                Bloom reduces images to a maximum size to make books easier to
                view over poor internet connections and take up less space on
                phones.
            </Div>
        </div>
    );
};

const BloomResolutionSliderInner: React.FunctionComponent<{
    value: Resolution;
    onChange: (value: Resolution) => void;
}> = props => {
    const sizes = [
        { l: "Small", w: 600, h: 600 },
        { l: "HD", w: 1280, h: 720 },
        { l: "Full HD", w: 1920, h: 1080 },
        { l: "4K", w: 3840, h: 2160 }
    ];
    let currentIndex = sizes.findIndex(x => x.w === props.value.maxWidth);
    if (currentIndex === -1) {
        currentIndex = 1; // See BL-12803.
    }
    const current = sizes[currentIndex];
    const currentLabel = useL10n(
        current.l,
        `BookSettings.eBook.Image.MaxResolution.${current.l}`
    );

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                width: 200px; // todo: what should this be?
                padding: 0 10px; // allow space for slider knob image
                margin-right: 1.5em; // allow space for the slider knob tooltip (BL-13067)
            `}
        >
            <Typography
                css={css`
                    text-align: right;
                    font-size: 12px;
                `}
                variant="h4"
            >{`${currentLabel}`}</Typography>
            <Slider
                track={false}
                max={sizes.length - 1}
                min={0}
                step={1}
                value={currentIndex}
                valueLabelFormat={() => {
                    return `${current.w}x${current.h}`;
                }}
                onChange={(e, value) => {
                    props.onChange({
                        maxWidth: sizes[value as number].w,
                        maxHeight: sizes[value as number].h
                    });
                }}
                valueLabelDisplay="auto"
            ></Slider>
        </div>
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
            />
        );
    }
}

export const MessageUsingLegacyThemeWithIncompatibleCss: React.FunctionComponent<{
    fileName: string;
    className?: string;
}> = props => {
    return (
        <PWithLink
            href="https://docs.bloomlibrary.org/incompatible-custombookstyles"
            l10nKey="BookSettings.UsingLegacyThemeWithIncompatibleCss"
            l10nParam0={props.fileName}
            l10nComment="{0} is a placeholder for a filename. The text inside the [square brackets] will become a link to a website."
            className={props.className}
        >
            The {0} stylesheet of this book is incompatible with modern themes.
            Bloom is using it because the book is using the Legacy-5-6 theme.
            Click [here] for more information.
        </PWithLink>
    );
};

export const MessageUsingMigratedThemeInsteadOfIncompatibleCss: React.FunctionComponent<{
    fileName: string;
    className?: string;
}> = props => {
    return (
        <Div
            l10nKey="BookSettings.UsingMigratedThemeInsteadOfIncompatibleCss"
            l10nParam0={props.fileName}
            l10nComment="{0} is a placeholder for a filename."
            className={props.className}
        >
            Bloom found a known version of {props.fileName} in this book and
            replaced it with a modern theme. You can delete it unless you still
            need to publish the book from an earlier version of Bloom.
        </Div>
    );
};

export const MessageIgnoringIncompatibleCssCanDelete: React.FunctionComponent<{
    fileName: string;
    className?: string;
}> = props => {
    return (
        <PWithLink
            href="https://docs.bloomlibrary.org/incompatible-custombookstyles"
            l10nKey="BookSettings.IgnoringIncompatibleCssCanDelete"
            l10nParam0={props.fileName}
            l10nComment="{0} is a placeholder for a filename. The text inside the [square brackets] will become a link to a website."
            className={props.className}
        >
            The
            {props.fileName} stylesheet of this book is incompatible with modern
            themes. Bloom is currently ignoring it. If you don't need those
            customizations any more, you can delete your
            {props.fileName}. Click [here] for more information.
        </PWithLink>
    );
};
export const MessageIgnoringIncompatibleCss: React.FunctionComponent<{
    fileName: string;
    className?: string;
}> = props => {
    return (
        <PWithLink
            href="https://docs.bloomlibrary.org/incompatible-custombookstyles"
            l10nKey="BookSettings.IgnoringIncompatibleCss"
            l10nParam0={props.fileName}
            l10nComment="{0} is a placeholder for a filename. The text inside the [square brackets] will become a link to a website."
        >
            The {props.fileName} stylesheet of this book is incompatible with
            modern themes. Bloom is currently ignoring it. Click [here] for more
            information.
        </PWithLink>
    );
};

const ColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled: boolean;
    onChange: (value: string) => void;
}> = props => {
    return (
        <ColorDisplayButton
            disabled={props.disabled}
            initialColor={props.value}
            localizedTitle={"foo"}
            transparency={false}
            palette={BloomPalette.CoverBackground}
            width={75}
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
        />
    );
};

// TODO: move this to config-r
const ConfigrCustomRow: React.FunctionComponent<React.PropsWithChildren<{}>> = props => {
    return (
        <ListItem
            css={css`
                flex-direction: column;
                align-items: flex-start;
            `}
        >
            {props.children}
        </ListItem>
    );
};
