import { css } from "@emotion/react";
import { Slider, Typography } from "@mui/material";
import { ThemeProvider } from "@mui/material/styles";
import {
    ConfigrBoolean,
    ConfigrCustomObjectInput,
    ConfigrCustomStringInput,
    ConfigrGroup,
    ConfigrPage,
    ConfigrSelect,
    ConfigrStatic,
} from "@sillsdev/config-r";
import { default as TrashIcon } from "@mui/icons-material/Delete";
import * as React from "react";
import { kBloomBlue, lightTheme } from "../../bloomMaterialUITheme";
import { NoteBox, WarningBox } from "../../react_components/boxes";
import { Div, P } from "../../react_components/l10nComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { PWithLink } from "../../react_components/pWithLink";
import { BloomSubscriptionIndicatorIconAndText } from "../../react_components/requiresSubscription";
import { BloomPalette } from "../../react_components/color-picking/bloomPalette";
import {
    ColorDisplayButton,
    DialogResult,
} from "../../react_components/color-picking/colorPickerDialog";
import { FieldVisibilityGroup } from "./FieldVisibilityGroup";
import { StyleAndFontTable } from "./StyleAndFontTable";

// Should stay in sync with AppearanceSettings.PageNumberPosition
enum PageNumberPosition {
    Automatic = "automatic",
    Left = "left",
    Center = "center",
    Right = "right",
    Hidden = "hidden",
}

type Resolution = {
    maxWidth: number;
    maxHeight: number;
};

type BookSettingsAreaProps = {
    appearanceDisabled: boolean;
    tierAllowsFullPageCoverImage?: boolean;
    tierAllowsFullBleed?: boolean;
    pageSizeSupportsFullBleed: boolean;
    settings: object | undefined;
    settingsToReturnLater: string | object | undefined;
    getAdditionalProps: <T>(subPath: string) => {
        path: string;
        overrideValue: T;
        overrideDescription?: string;
    };
    firstPossiblyLegacyCss: string;
    theme: string;
    migratedTheme: string;
    deleteCustomBookStyles: () => void;
    saveSettingsAndCloseDialog: () => void;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
    themeNames: Array<{ label: string; value: string }>;
};

export type IConfigrAreaDefinition = {
    label: string;
    pageKey: string;
    content: string;
    pages: React.ReactElement[];
};

export const useBookSettingsAreaDefinition = (
    props: BookSettingsAreaProps,
): IConfigrAreaDefinition => {
    const bookAreaLabel = useL10n("Book", "BookAndPageSettings.BookArea");
    const bookAreaDescription = useL10n(
        "Book settings apply to all of the pages of the current book.",
        "BookAndPageSettings.BookArea.Description",
    );

    const coverLabel = useL10n("Cover", "BookSettings.CoverGroupLabel");
    const contentPagesLabel = useL10n(
        "Content Pages",
        "BookSettings.ContentPagesGroupLabel",
    );
    const printPublishingLabel = useL10n(
        "Print Publishing",
        "BookSettings.PrintPublishingGroupLabel",
    );
    const languagesToShowNormalSubgroupLabel = useL10n(
        "Languages to show in normal text boxes",
        "BookSettings.NormalTextBoxLangsLabel",
        "",
    );
    const themeLabel = useL10n("Page Theme", "BookSettings.PageThemeLabel", "");
    const themeDescription = useL10n(
        "", // will be translated or the English will come from the xliff
        "BookSettings.Theme.Description",
    );

    const coverBackgroundColorLabel = useL10n(
        "Background Color",
        "Common.BackgroundColor",
    );

    const whatToShowOnCoverLabel = useL10n(
        "Front Cover",
        "BookSettings.WhatToShowOnCover",
    );

    const showLanguageNameLabel = useL10n(
        "Show Language Name",
        "BookSettings.ShowLanguageName",
    );
    const showTopicLabel = useL10n("Show Topic", "BookSettings.ShowTopic");
    const showCreditsLabel = useL10n(
        "Show Credits",
        "BookSettings.ShowCredits",
    );
    const pageNumbersLabel = useL10n(
        "Page Numbers",
        "BookSettings.PageNumbers",
    );
    const pageNumberLocationNote = useL10n(
        "Note: some Page Themes may not know how to change the location of the Page Number.",
        "BookSettings.PageNumberLocationNote",
    );
    const pageNumberPositionAutomaticLabel = useL10n(
        "(Automatic)",
        "BookSettings.PageNumbers.Automatic",
    );
    const pageNumberPositionLeftLabel = useL10n(
        "Left",
        "BookSettings.PageNumbers.Left",
    );
    const pageNumberPositionCenterLabel = useL10n(
        "Center",
        "BookSettings.PageNumbers.Center",
    );
    const pageNumberPositionRightLabel = useL10n(
        "Right",
        "BookSettings.PageNumbers.Right",
    );
    const pageNumberPositionHiddenLabel = useL10n(
        "Hidden",
        "BookSettings.PageNumbers.Hidden",
    );

    const resolutionLabel = useL10n("Resolution", "BookSettings.Resolution");
    const bloomPubLabel = useL10n("eBooks", "PublishTab.bloomPUBButton");

    const advancedLayoutLabel = useL10n(
        "Advanced Layout",
        "BookSettings.AdvancedLayoutLabel",
    );
    const textPaddingLabel = useL10n(
        "Text Padding",
        "BookSettings.TopLevelTextPaddingLabel",
    );
    const textPaddingDescription = useL10n(
        "Smart spacing around text boxes. Works well for simple pages, but may not suit custom layouts.",
        "BookSettings.TopLevelTextPadding.Description",
    );
    const textPaddingDefaultLabel = useL10n(
        "Default (set by Theme)",
        "BookSettings.TopLevelTextPadding.DefaultLabel",
    );
    const textPadding1emLabel = useL10n(
        "1 em (font size)",
        "BookSettings.TopLevelTextPadding.1emLabel",
    );

    const gutterLabel = useL10n("Page Gutter", "BookSettings.Gutter.Label");
    const gutterDescription = useL10n(
        "Extra space between pages near the book spine. Increase this for books with many pages to ensure text isn't lost in the binding. This gap is applied to each side of the spine.",
        "BookSettings.Gutter.Description",
    );
    const gutterDefaultLabel = useL10n(
        "Default (set by Theme)",
        "BookSettings.Gutter.DefaultLabel",
    );

    const coverIsImageLabel = useL10n(
        "Fill the front cover with a single image",
        "BookSettings.CoverIsImage",
    );
    const coverIsImageDescription = useL10n(
        "Replace the front cover content with a single full-bleed image. See [Full Page Cover Images](https://docs.bloomlibrary.org/full-page-cover-images) for information on sizing your image to fit.",
        "BookSettings.CoverIsImage.Description.V2",
    );

    const fullBleedLabel = useL10n(
        "Use full bleed page layout",
        "BookSettings.FullBleed",
    );
    const fullBleedDescription = useL10n(
        "Enable full bleed layout for printing. This turns on the [Print Bleed](https://en.wikipedia.org/wiki/Bleed_%28printing%29) indicators on paper layouts. See [Full Bleed Layout](https://docs.bloomlibrary.org/full-bleed) for more information.",
        "BookSettings.FullBleed.Description",
    );

    const coverColorPickerControl = React.useCallback(
        (coverColorProps: {
            value: string;
            disabled: boolean;
            onChange: (value: string) => void;
        }) => {
            return (
                <CoverColorPickerForConfigr
                    {...coverColorProps}
                    onColorPickerVisibilityChanged={
                        props.onColorPickerVisibilityChanged
                    }
                />
            );
        },
        [props.onColorPickerVisibilityChanged],
    );

    return {
        label: bookAreaLabel,
        pageKey: "bookArea",
        content: bookAreaDescription,
        pages: [
            <ConfigrPage key="cover" label={coverLabel} pageKey="cover">
                {props.appearanceDisabled && (
                    <ConfigrStatic>
                        <NoteBox>
                            <Div l10nKey="BookSettings.ThemeDisablesOptionsNotice">
                                The selected page theme does not support the
                                following settings.
                            </Div>
                        </NoteBox>
                    </ConfigrStatic>
                )}
                <ConfigrGroup label={whatToShowOnCoverLabel}>
                    <div>
                        <ConfigrBoolean
                            label={coverIsImageLabel}
                            description={coverIsImageDescription}
                            {...props.getAdditionalProps<boolean>(
                                `coverIsImage`,
                            )}
                            disabled={
                                props.appearanceDisabled ||
                                !props.tierAllowsFullPageCoverImage
                            }
                        />
                        <div
                            css={css`
                                display: flex;
                                padding-bottom: 5px;
                                font-size: 12px;
                                font-weight: bold;
                            `}
                        >
                            <BloomSubscriptionIndicatorIconAndText
                                feature="fullPageCoverImage"
                                css={css`
                                    margin-left: auto;
                                `}
                                disabled={props.appearanceDisabled}
                            />
                        </div>
                    </div>
                    <FieldVisibilityGroup
                        field="cover-title"
                        labelFrame="Show Title in {0}"
                        labelFrameL10nKey="BookSettings.ShowWrittenLanguageTitle"
                        settings={props.settings}
                        settingsToReturnLater={props.settingsToReturnLater}
                        disabled={props.appearanceDisabled}
                        L1MustBeTurnedOn={true}
                        getAdditionalProps={props.getAdditionalProps}
                    />

                    <ConfigrBoolean
                        label={showLanguageNameLabel}
                        disabled={props.appearanceDisabled}
                        {...props.getAdditionalProps<boolean>(
                            `cover-languageName-show`,
                        )}
                    />
                    <ConfigrBoolean
                        label={showTopicLabel}
                        disabled={props.appearanceDisabled}
                        {...props.getAdditionalProps<boolean>(
                            `cover-topic-show`,
                        )}
                    />
                    <ConfigrBoolean
                        label={showCreditsLabel}
                        disabled={props.appearanceDisabled}
                        {...props.getAdditionalProps<boolean>(
                            `cover-creditsRow-show`,
                        )}
                    />
                </ConfigrGroup>
                <ConfigrGroup label={"All Cover Pages"}>
                    <ConfigrCustomStringInput
                        label={coverBackgroundColorLabel}
                        control={coverColorPickerControl}
                        disabled={props.appearanceDisabled}
                        {...props.getAdditionalProps<string>(
                            `cover-background-color`,
                        )}
                    />
                </ConfigrGroup>
            </ConfigrPage>,
            <ConfigrPage
                key="contentPages"
                label={contentPagesLabel}
                pageKey="contentPages"
            >
                {
                    // This group of four possible messages...sometimes none of them shows, so there are five options...
                    // is very similar to the one in BookInfoIndicator.tsx. If you change one, you may need to change the other.
                    // In particular, the logic for which to show and the text of the messages should be kept in sync.
                    // I'm not seeing a clean way to reuse the logic. Some sort of higher-order component might work,
                    // but I don't think the logic is complex enough to be worth it, when only used in two places.
                }
                {props.firstPossiblyLegacyCss.length > 0 &&
                    props.theme === "legacy-5-6" && (
                        <ConfigrStatic>
                            <WarningBox>
                                <MessageUsingLegacyThemeWithIncompatibleCss
                                    fileName={props.firstPossiblyLegacyCss}
                                />
                            </WarningBox>
                        </ConfigrStatic>
                    )}
                {props.firstPossiblyLegacyCss === "customBookStyles.css" &&
                    props.theme !== "legacy-5-6" && (
                        <ConfigrStatic>
                            <NoteBox>
                                <div>
                                    {props.migratedTheme ? (
                                        <MessageUsingMigratedThemeInsteadOfIncompatibleCss
                                            fileName={
                                                props.firstPossiblyLegacyCss
                                            }
                                        />
                                    ) : (
                                        <MessageIgnoringIncompatibleCssCanDelete
                                            fileName={
                                                props.firstPossiblyLegacyCss
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
                                            props.deleteCustomBookStyles()
                                        }
                                    >
                                        <TrashIcon
                                            id="trashIcon"
                                            color="primary"
                                        />
                                        <Div
                                            l10nKey="BookSettings.DeleteCustomBookStyles"
                                            l10nParam0={
                                                props.firstPossiblyLegacyCss
                                            }
                                            css={css`
                                                color: ${kBloomBlue};
                                            `}
                                        >
                                            Delete{" "}
                                            {props.firstPossiblyLegacyCss}
                                        </Div>
                                    </div>
                                </div>
                            </NoteBox>
                        </ConfigrStatic>
                    )}
                {props.firstPossiblyLegacyCss.length > 0 &&
                    props.firstPossiblyLegacyCss !== "customBookStyles.css" &&
                    props.theme !== "legacy-5-6" && (
                        <ConfigrStatic>
                            <NoteBox>
                                <MessageIgnoringIncompatibleCss
                                    fileName={props.firstPossiblyLegacyCss}
                                />
                            </NoteBox>
                        </ConfigrStatic>
                    )}
                <ConfigrGroup>
                    {/* Wrapping these two in a div prevents Config-R from sticking a divider between them */}
                    <div>
                        <ConfigrSelect
                            label={themeLabel}
                            disabled={false}
                            path={`appearance.cssThemeName`}
                            options={props.themeNames.map((x) => {
                                return {
                                    label: x.label,
                                    value: x.value,
                                };
                            })}
                            description={themeDescription}
                        />
                        {props.appearanceDisabled && (
                            <NoteBox
                                css={css`
                                    margin-left: 20px;
                                `}
                            >
                                <Div l10nKey="BookSettings.ThemeDisablesOptionsNotice">
                                    The selected page theme does not support the
                                    following settings.
                                </Div>
                            </NoteBox>
                        )}
                    </div>
                    <ConfigrSelect
                        label={pageNumbersLabel}
                        disabled={props.appearanceDisabled}
                        {...props.getAdditionalProps<string>(
                            `pageNumber-position`,
                        )}
                        options={[
                            {
                                label: pageNumberPositionAutomaticLabel,
                                value: PageNumberPosition.Automatic,
                            },
                            {
                                label: pageNumberPositionLeftLabel,
                                value: PageNumberPosition.Left,
                            },
                            {
                                label: pageNumberPositionCenterLabel,
                                value: PageNumberPosition.Center,
                            },
                            {
                                label: pageNumberPositionRightLabel,
                                value: PageNumberPosition.Right,
                            },
                            {
                                label: "--",
                                value: "--",
                            },
                            {
                                label: pageNumberPositionHiddenLabel,
                                value: PageNumberPosition.Hidden,
                            },
                        ]}
                        description={pageNumberLocationNote}
                    />
                </ConfigrGroup>
                <ConfigrGroup label={languagesToShowNormalSubgroupLabel}>
                    <FieldVisibilityGroup
                        field="autoTextBox"
                        labelFrame="Show {0}"
                        labelFrameL10nKey="BookSettings.ShowContentLanguage"
                        settings={props.settings}
                        settingsToReturnLater={props.settingsToReturnLater}
                        disabled={false}
                        getAdditionalProps={props.getAdditionalProps}
                    />
                </ConfigrGroup>
                <ConfigrGroup label={advancedLayoutLabel}>
                    <ConfigrSelect
                        label={textPaddingLabel}
                        options={[
                            {
                                label: textPaddingDefaultLabel,
                                value: "", // use whatever the theme provides
                            },
                            { label: "0 mm", value: "0mm" },
                            { label: "2 mm", value: "2mm" },
                            { label: "4 mm", value: "4mm" },
                            {
                                label: textPadding1emLabel,
                                value: "1em",
                            },
                        ]}
                        description={textPaddingDescription}
                        {...props.getAdditionalProps<string>(
                            `topLevel-text-padding`,
                        )}
                    />
                    <ConfigrSelect
                        label={gutterLabel}
                        options={[
                            {
                                label: gutterDefaultLabel,
                                value: "", // use whatever the theme provides
                            },
                            { label: "0 mm", value: "0mm" },
                            { label: "2 mm", value: "2mm" },
                            { label: "4 mm", value: "4mm" },
                            { label: "6 mm", value: "6mm" },
                            { label: "10 mm", value: "10mm" },
                        ]}
                        description={gutterDescription}
                        {...props.getAdditionalProps<string>(`page-gutter`)}
                    />
                </ConfigrGroup>
            </ConfigrPage>,
            <ConfigrPage
                key="printPublishing"
                label={printPublishingLabel}
                pageKey="printPublishing"
            >
                <ConfigrGroup>
                    <div>
                        <ConfigrBoolean
                            label={fullBleedLabel}
                            description={fullBleedDescription}
                            {...props.getAdditionalProps<boolean>(`fullBleed`)}
                            disabled={
                                !props.tierAllowsFullBleed ||
                                !props.pageSizeSupportsFullBleed
                            }
                        />
                        <div
                            css={css`
                                display: flex;
                                padding-bottom: 5px;
                                font-size: 12px;
                                font-weight: bold;
                            `}
                        >
                            <BloomSubscriptionIndicatorIconAndText
                                feature="PrintshopReady"
                                css={css`
                                    margin-left: auto;
                                `}
                            />
                        </div>
                    </div>
                </ConfigrGroup>
            </ConfigrPage>,
            <ConfigrPage
                key="bloomPub"
                label={bloomPubLabel}
                pageKey="bloomPub"
            >
                {/* note that this is used for bloomPUB and ePUB, but we don't have separate settings so we're putting them in bloomPUB and leaving it to c# code to use it for ePUB as well. */}
                <ConfigrGroup>
                    <BloomResolutionSlider
                        label={resolutionLabel}
                        path={`publish.bloomPUB.imageSettings`}
                    />
                </ConfigrGroup>
            </ConfigrPage>,
            <ConfigrPage key="fonts" label="Fonts" pageKey="fonts">
                <ConfigrGroup>
                    <ConfigrStatic>
                        <NoteBox>
                            <div>
                                <P l10nKey="BookSettings.Fonts.Problematic">
                                    When you publish a book to the web or as an
                                    ebook, Bloom will flag any problematic
                                    fonts. For example, we cannot legally host
                                    most Microsoft fonts on BloomLibrary.org.
                                </P>
                                <P l10nKey="BookSettings.Fonts.TableDescription">
                                    The following table shows where fonts have
                                    been used.
                                </P>
                            </div>
                        </NoteBox>
                        <StyleAndFontTable
                            closeDialog={props.saveSettingsAndCloseDialog}
                        />
                    </ConfigrStatic>
                </ConfigrGroup>
            </ConfigrPage>,
        ],
    };
};

const BloomResolutionSlider: React.FunctionComponent<
    React.PropsWithChildren<{
        path: string;
        label: string;
    }>
> = (props) => {
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
}> = (props) => {
    const sizes = [
        { l: "Small", w: 600, h: 600 },
        { l: "HD", w: 1280, h: 720 },
        { l: "Full HD", w: 1920, h: 1080 },
        { l: "4K", w: 3840, h: 2160 },
    ];
    let currentIndex = sizes.findIndex((x) => x.w === props.value.maxWidth);
    if (currentIndex === -1) {
        currentIndex = 1; // See BL-12803.
    }
    const current = sizes[currentIndex];
    const currentLabel = useL10n(
        current.l,
        `BookSettings.eBook.Image.MaxResolution.${current.l}`,
    );

    return (
        <ThemeProvider theme={lightTheme}>
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
                            maxHeight: sizes[value as number].h,
                        });
                    }}
                    valueLabelDisplay="auto"
                ></Slider>
            </div>
        </ThemeProvider>
    );
};

const CoverColorPickerForConfigr: React.FunctionComponent<{
    value: string;
    disabled: boolean;
    onChange: (value: string) => void;
    onColorPickerVisibilityChanged?: (open: boolean) => void;
}> = (props) => {
    const coverBackgroundColorLabel = useL10n(
        "Background Color",
        "Common.BackgroundColor",
    );

    return (
        <ColorDisplayButton
            disabled={props.disabled}
            initialColor={props.value}
            localizedTitle={coverBackgroundColorLabel}
            transparency={false}
            palette={BloomPalette.CoverBackground}
            width={75}
            onColorPickerVisibilityChanged={
                props.onColorPickerVisibilityChanged
            }
            onClose={(dialogResult: DialogResult, newColor: string) => {
                if (dialogResult === DialogResult.OK) props.onChange(newColor);
            }}
        />
    );
};

export const MessageUsingLegacyThemeWithIncompatibleCss: React.FunctionComponent<{
    fileName: string;
    className?: string;
}> = (props) => {
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
}> = (props) => {
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
}> = (props) => {
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
}> = (props) => {
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
