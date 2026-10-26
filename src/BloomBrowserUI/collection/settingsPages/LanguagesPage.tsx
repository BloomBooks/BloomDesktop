import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrCustomNumberInput,
    ConfigrCustomObjectInput,
    ConfigrCustomStringInput,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
    ConfigrSelect,
} from "@sillsdev/config-r";
import { Button, MenuItem, Select } from "@mui/material";
import { useL10n } from "../../react_components/l10nHooks";
import { get } from "../../utils/bloomApi";
import FontSelectComponent, {
    IFontMetaData,
} from "../../bookEdit/StyleEditor/fontSelectComponent";
import { cleanSpaceDelimitedList } from "../../bookEdit/toolbox/readers/readerSetup/spaceDelimitedList";
import {
    ICollectionSettingsLanguage,
    ICollectionSettingsResponse,
    ICollectionSettingsSignLanguage,
    ICollectionSettingsValues,
} from "../collectionSettingsTypes";
import { ILanguageData } from "../languageData";
import { LanguageChooserDialog } from "../LanguageChooserDialog";

// The choices the old Special Script Settings dialog offered. 0 means Bloom's default.
const kLineSpacings = [
    1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.0, 2.5, 3.0,
];
const kToolFontSizes = [9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26];

// What the reader tools' Reader Setup dialog shows as an example alphabet; it is not translated.
const kEnglishAlphabetExample =
    "a b c ch d e f g h i j k l m n ng o p q r s sh t th u v w x y z ' -";

/**
 * A select of numbers in which 0 means "Auto" (Bloom's default).
 */
const NumberSelect: React.FunctionComponent<{
    choices: number[];
    // How each choice is shown; numbers shown to the user follow their culture.
    format: (choice: number) => string;
    value: number;
    disabled?: boolean;
    onChange: (value: number) => void;
}> = (props) => {
    const autoLabel = useL10n(
        "Auto",
        "CollectionSettingsDialog.LanguagesPage.Auto",
    );
    return (
        <Select
            variant="standard"
            value={props.value}
            disabled={props.disabled}
            onChange={(event) => props.onChange(Number(event.target.value))}
            css={css`
                min-width: 120px;
            `}
        >
            <MenuItem value={0}>{autoLabel}</MenuItem>
            {props.choices.map((choice) => (
                <MenuItem key={choice} value={choice}>
                    {props.format(choice)}
                </MenuItem>
            ))}
        </Select>
    );
};

// Config-R mounts a custom control again whenever its identity changes, so these are made once.
const LineSpacingSelect: React.FunctionComponent<{
    value: number;
    disabled?: boolean;
    onChange: (value: number) => void;
}> = (props) => (
    <NumberSelect
        choices={kLineSpacings}
        format={(choice) =>
            choice.toLocaleString(undefined, { minimumFractionDigits: 1 })
        }
        value={props.value}
        disabled={props.disabled}
        onChange={props.onChange}
    />
);
const ToolFontSizeSelect: React.FunctionComponent<{
    value: number;
    disabled?: boolean;
    onChange: (value: number) => void;
}> = (props) => (
    <NumberSelect
        choices={kToolFontSizes}
        format={(choice) => choice.toLocaleString()}
        value={props.value}
        disabled={props.disabled}
        onChange={props.onChange}
    />
);

/**
 * The language the user picked in the chooser, as one of the collection's languages. The rest of
 * a language they changed is kept; a language they added starts from Bloom's defaults. The name
 * counts as custom when it is not the chooser's own name for the language, as in C#
 * (LanguageChangeEventArgs.IsCustomName).
 */
export function languageFromChooser(
    chosen: ILanguageData,
    previous: ICollectionSettingsLanguage | null,
    defaultFontName: string,
): ICollectionSettingsLanguage {
    const language = previous ?? {
        tag: "",
        name: "",
        isCustomName: false,
        fontName: defaultFontName,
        isRightToLeft: false,
        lineHeight: 0,
        breaksLinesOnlyAtSpaces: false,
        baseUIFontSizeInPoints: 0,
    };
    return {
        ...language,
        tag: chosen.LanguageTag!,
        name: chosen.DesiredName!,
        isCustomName: chosen.DesiredName !== chosen.DefaultName,
        // The chooser knows the direction of some scripts; otherwise keep what the user had.
        isRightToLeft: chosen.IsRtl ?? language.isRightToLeft,
    };
}

/**
 * The sign language the user picked in the chooser. A sign language has no text direction, so
 * the chooser's IsRtl is ignored.
 */
function signLanguageFromChooser(
    chosen: ILanguageData,
): ICollectionSettingsSignLanguage {
    return {
        tag: chosen.LanguageTag!,
        name: chosen.DesiredName!,
        isCustomName: chosen.DesiredName !== chosen.DefaultName,
    };
}

/**
 * The language row's control: the language's name and tag, a button that opens the language
 * chooser in a dialog of its own, and, for a language the collection need not have, Remove.
 */
const LanguageChooserControl = <
    T extends ICollectionSettingsLanguage | ICollectionSettingsSignLanguage,
>(props: {
    value: T | null;
    disabled?: boolean;
    onChange: (value: T | null) => void;
    // Whether the collection may be without this language (the third and the sign language).
    removable: boolean;
    // The language as it should be once the user picks one in the chooser.
    fromChooser: (chosen: ILanguageData, previous: T | null) => T;
}): React.ReactElement => {
    const changeLabel = useL10n(
        "Change...",
        "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink",
    );
    const setLabel = useL10n(
        "Set...",
        "CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink",
    );
    const removeLabel = useL10n(
        "Remove",
        "CollectionSettingsDialog.LanguageTab.RemoveLanguageLink",
    );
    const [choosing, setChoosing] = React.useState(false);
    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                gap: 12px;
            `}
        >
            {props.value && (
                <span data-testid="language-name">
                    {`${props.value.name} (${props.value.tag})`}
                </span>
            )}
            <Button
                variant="outlined"
                disabled={props.disabled}
                onClick={() => setChoosing(true)}
            >
                {props.value ? changeLabel : setLabel}
            </Button>
            {props.removable && props.value && (
                <Button
                    disabled={props.disabled}
                    onClick={() => props.onChange(null)}
                >
                    {removeLabel}
                </Button>
            )}
            {choosing && (
                <LanguageChooserDialog
                    dialogEnvironment={{
                        dialogFrameProvidedExternally: false,
                        initiallyOpen: true,
                    }}
                    initialLanguageTag={props.value?.tag}
                    initialCustomName={props.value?.name}
                    onChosen={(chosen) =>
                        props.onChange(props.fromChooser(chosen, props.value))
                    }
                    onClose={() => setChoosing(false)}
                />
            )}
        </div>
    );
};

// The first two languages are never removed, so they get the language they had back.
const RequiredLanguageControl: React.FunctionComponent<{
    value: ICollectionSettingsLanguage | null;
    disabled?: boolean;
    onChange: (value: ICollectionSettingsLanguage | null) => void;
}> = (props) => (
    <LanguageChooserControl
        value={props.value}
        disabled={props.disabled}
        onChange={props.onChange}
        removable={false}
        // A required language always has a previous value, so there are no defaults to need.
        fromChooser={(chosen, previous) =>
            languageFromChooser(chosen, previous, "")
        }
    />
);
const SignLanguageControl: React.FunctionComponent<{
    value: ICollectionSettingsSignLanguage | null;
    disabled?: boolean;
    onChange: (value: ICollectionSettingsSignLanguage | null) => void;
}> = (props) => (
    <LanguageChooserControl
        value={props.value}
        disabled={props.disabled}
        onChange={props.onChange}
        removable={true}
        fromChooser={signLanguageFromChooser}
    />
);

/**
 * Which written language (1 to 3) a group of rows is for, and the heading of the group.
 */
interface IWrittenLanguage {
    number: 1 | 2 | 3;
    heading: string;
}

/**
 * The values as they should be saved, as far as this page goes: an alphabet the user edited is
 * cleaned the way Reader Setup cleans it (commas and new lines become single spaces). One they
 * did not touch goes back exactly as it was, so saving never rewrites the reader tools' file
 * for nothing. `valuesWhenOpened` are the values as the dialog opened.
 */
export function withEditedLettersCleaned(
    values: ICollectionSettingsValues,
    valuesWhenOpened: ICollectionSettingsValues,
): ICollectionSettingsValues {
    if (values.readerTools.letters === valuesWhenOpened.readerTools.letters)
        return values;
    return {
        ...values,
        readerTools: {
            ...values.readerTools,
            letters: cleanSpaceDelimitedList(values.readerTools.letters),
        },
    };
}

/**
 * The Languages page of the Collection Settings dialog: the collection's written languages and
 * sign language, each language's font, its script settings on a "More" page of its own, and the
 * page numbering style.
 */
export function useLanguagesPage(props: {
    dialogOpen: boolean;
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
    // The values as the user has them now, which say which optional languages to show rows for.
    values: ICollectionSettingsValues | undefined;
}): React.ReactElement {
    const label = useL10n(
        "Languages",
        "CollectionSettingsDialog.LanguageTab.LanguageTabLabel",
    );
    const language1Heading = useL10n(
        "Local Language",
        "CollectionSettingsDialog.LanguageTab.VernacularLanguageLabel",
    );
    const language2Heading = useL10n(
        "Language 2 (e.g. National Language)",
        "CollectionSettingsDialog.LanguageTab._language2Label",
    );
    const language3Heading = useL10n(
        "Language 3 (e.g. Regional Language)   (Optional)",
        "CollectionSettingsDialog.LanguageTab._language3Label",
    );
    const signLanguageHeading = useL10n(
        "Sign Language   (Optional)",
        "CollectionSettingsDialog.LanguageTab.SignLanguageOptional",
    );
    const languageLabel = useL10n(
        "Language",
        "CollectionSettingsDialog.LanguagesPage.Language",
    );
    const defaultFontLabel = useL10n(
        "Default Font",
        "CollectionSettingsDialog.LanguagesPage.DefaultFont",
    );
    const moreLabel = useL10n(
        "More",
        "CollectionSettingsDialog.LanguagesPage.More",
    );
    const fontsLabel = useL10n(
        "Fonts",
        "CollectionSettingsDialog.LanguagesPage.Fonts",
    );
    const toolFontSizeLabel = useL10n(
        "Font size when displayed in tools",
        "CollectionSettingsDialog.LanguagesPage.ToolFontSize",
    );
    const toolFontSizeDescription = useL10n(
        "Use this to increase the font size when this language is shown in places like the Decodable Reader Tool.",
        "CollectionSettingsDialog.LanguagesPage.ToolFontSize.Description",
    );
    const lineSpacingLabel = useL10n(
        "Default line spacing",
        "ScriptSettingsDialog.DefaultLineSpacing",
    );
    const lineSpacingDescription = useL10n(
        "Use this to increase the default line spacing if this language has tall characters that tend to run into other lines of text.",
        "CollectionSettingsDialog.LanguagesPage.LineSpacing.Description",
    );
    const scriptLabel = useL10n(
        "Script",
        "CollectionSettingsDialog.LanguagesPage.Script",
    );
    const lineBreakLabel = useL10n(
        "Do not use special Asian script word breaking",
        "ScriptSettingsDialog.LineBreakCheckBox",
    );
    const rightToLeftLabel = useL10n(
        "This script is written right to left like Arabic, Hebrew, and Urdu",
        "ScriptSettingsDialog.RightToLeftScriptCheckBox",
    );
    const rightToLeftDescription = useL10n(
        "If you are not sure, leave this off. If your language is written starting on the right and moving left, you will know.",
        "CollectionSettingsDialog.LanguagesPage.RightToLeft.Description",
    );
    const readerToolsLabel = useL10n(
        "Reader Tools",
        "CollectionSettingsDialog.LanguagesPage.ReaderTools",
    );
    const lettersLabel = useL10n(
        "Letters and Letter Combinations",
        "ReaderSetup.Letters.Header",
    );
    const lettersHelp = useL10n(
        "Separate each letter or letter combination with a space. For example, here is what we might use for the English language:",
        "ReaderSetup.Letters.LetterHelp1",
    );
    const sentencePunctLabel = useL10n(
        "Special Sentence-Ending Punctuation",
        "ReaderSetup.SentencePunctuation.Header",
    );
    const sentencePunctHelp = useL10n(
        "Bloom already knows which characters in the world's languages always mark the end of sentences. To add others, enter the Unicode character escapes here. For example for Thai script languages, enter \\U0020 to tell Bloom that normal spaces are used to break sentences.",
        "ReaderSetup.SentencePunctuation.Help",
    );
    const readerToolsLanguage1Changed = useL10n(
        "These belong to the language you are replacing. To change them for the new language, click OK, then open these settings again.",
        "CollectionSettingsDialog.LanguagesPage.ReaderTools.Language1Changed",
    );
    const readerToolsUnreadable = useL10n(
        "Bloom cannot read the reader tools' settings file for this language, so these cannot be changed here.",
        "CollectionSettingsDialog.LanguagesPage.ReaderTools.Unreadable",
    );
    const pageNumberingLabel = useL10n(
        "Page Numbering Style",
        "CollectionSettingsDialog.BookMakingTab.PageNumberingStyle.PageNumberingStyleLabel",
    );

    // Asked for when the dialog opens, not when Config-R mounts this page, so that it is usually
    // in before anyone looks.
    const [fontMetadata, setFontMetadata] = React.useState<IFontMetaData[]>();
    React.useEffect(() => {
        if (!props.dialogOpen) return;
        get("fonts/metadata", (result) => {
            setFontMetadata(result.data as IFontMetaData[]);
        });
    }, [props.dialogOpen]);

    // The font picker needs the font list, so it is made again (and Config-R mounts it again)
    // only when the list arrives.
    const fontControls = React.useMemo(() => {
        function makeFontControl(
            languageNumber: number,
        ): React.FunctionComponent<{
            value: string;
            disabled?: boolean;
            onChange: (value: string) => void;
        }> {
            const FontControl: React.FunctionComponent<{
                value: string;
                disabled?: boolean;
                onChange: (value: string) => void;
            }> = (controlProps) => (
                // The picker brings margins from its place in the Format dialog; here it lines up
                // with the other rows' controls instead, with the font name centered in its box.
                <div
                    css={css`
                        .MuiFormControl-root {
                            margin: 0 !important;
                        }
                        // The font name is a paragraph, which picks up the page's paragraph
                        // spacing and so sits high in the box.
                        .font-display-bar p {
                            margin: 0;
                        }
                    `}
                >
                    <FontSelectComponent
                        languageNumber={languageNumber}
                        fontMetadata={fontMetadata}
                        currentFontName={controlProps.value}
                        onChangeFont={controlProps.onChange}
                    />
                </div>
            );
            return FontControl;
        }
        return [makeFontControl(1), makeFontControl(2), makeFontControl(3)];
    }, [fontMetadata]);

    // A third language the user adds starts with Bloom's default font, which C# tells us.
    const defaultFontName = props.settings?.defaultFontName ?? "";
    const Language3Control = React.useMemo(() => {
        const Control: React.FunctionComponent<{
            value: ICollectionSettingsLanguage | null;
            disabled?: boolean;
            onChange: (value: ICollectionSettingsLanguage | null) => void;
        }> = (controlProps) => (
            <LanguageChooserControl
                value={controlProps.value}
                disabled={controlProps.disabled}
                onChange={controlProps.onChange}
                removable={true}
                fromChooser={(chosen, previous) =>
                    languageFromChooser(chosen, previous, defaultFontName)
                }
            />
        );
        return Control;
    }, [defaultFontName]);

    // The reader tools' file is named for the first language, so its values cannot be edited
    // while the user is changing that language: they would go to the language being replaced.
    // (values is null for a user who may not edit the settings.)
    const savedLanguage1Tag = props.settings?.values?.languages.language1.tag;
    const language1TagChanged =
        savedLanguage1Tag !== undefined &&
        props.values?.languages.language1.tag !== savedLanguage1Tag;
    // Why the reader tools' values cannot be edited just now, if they cannot. A damaged file
    // would be written over, losing the stages and levels Bloom could not read.
    let readerToolsLockedReason: string | undefined;
    if (props.settings?.values?.readerTools.isUnreadable)
        readerToolsLockedReason = readerToolsUnreadable;
    else if (language1TagChanged)
        readerToolsLockedReason = readerToolsLanguage1Changed;

    // The rows of one written language. An optional language the collection does not have gets
    // only its language row, from which the user can set one: Config-R would otherwise give the
    // missing language's settings default values, so that it no longer counted as missing.
    function writtenLanguageGroup(language: IWrittenLanguage) {
        const path = `languages.language${language.number}`;
        const present =
            language.number !== 3 || !!props.values?.languages.language3;
        return (
            <ConfigrGroup key={path} label={language.heading}>
                <ConfigrCustomObjectInput<ICollectionSettingsLanguage | null>
                    path={path}
                    label={languageLabel}
                    control={
                        language.number === 3
                            ? Language3Control
                            : RequiredLanguageControl
                    }
                />
                {present && (
                    <ConfigrCustomStringInput
                        path={`${path}.fontName`}
                        label={defaultFontLabel}
                        control={fontControls[language.number - 1]}
                    />
                )}
                {present && (
                    <ConfigrPage
                        label={moreLabel}
                        pageKey={`language${language.number}More`}
                    >
                        <ConfigrGroup label={fontsLabel}>
                            <ConfigrCustomNumberInput
                                path={`${path}.baseUIFontSizeInPoints`}
                                label={toolFontSizeLabel}
                                description={toolFontSizeDescription}
                                control={ToolFontSizeSelect}
                            />
                            <ConfigrCustomNumberInput
                                path={`${path}.lineHeight`}
                                label={lineSpacingLabel}
                                description={lineSpacingDescription}
                                control={LineSpacingSelect}
                            />
                        </ConfigrGroup>
                        <ConfigrGroup label={scriptLabel}>
                            <ConfigrBoolean
                                path={`${path}.breaksLinesOnlyAtSpaces`}
                                label={lineBreakLabel}
                            />
                            <ConfigrBoolean
                                path={`${path}.isRightToLeft`}
                                label={rightToLeftLabel}
                                description={rightToLeftDescription}
                            />
                        </ConfigrGroup>
                        {/* The reader tools work only in the first language. */}
                        {language.number === 1 && (
                            <ConfigrGroup label={readerToolsLabel}>
                                <ConfigrInput
                                    path="readerTools.letters"
                                    label={lettersLabel}
                                    description={`${lettersHelp} ${kEnglishAlphabetExample}`}
                                    overrideDescription={
                                        readerToolsLockedReason
                                    }
                                    disabled={!!readerToolsLockedReason}
                                    allowNewLines={true}
                                    maxLinesToShowBeforeScrolling={3}
                                />
                                <ConfigrInput
                                    path="readerTools.sentencePunct"
                                    label={sentencePunctLabel}
                                    description={sentencePunctHelp}
                                    overrideDescription={
                                        readerToolsLockedReason
                                    }
                                    disabled={!!readerToolsLockedReason}
                                />
                            </ConfigrGroup>
                        )}
                    </ConfigrPage>
                )}
            </ConfigrGroup>
        );
    }

    return (
        <ConfigrPage label={label} pageKey="languages" topLevel={true}>
            {writtenLanguageGroup({ number: 1, heading: language1Heading })}
            {writtenLanguageGroup({ number: 2, heading: language2Heading })}
            {writtenLanguageGroup({ number: 3, heading: language3Heading })}
            <ConfigrGroup label={signLanguageHeading}>
                <ConfigrCustomObjectInput<ICollectionSettingsSignLanguage | null>
                    path="languages.signLanguage"
                    label={languageLabel}
                    control={SignLanguageControl}
                />
            </ConfigrGroup>
            <ConfigrGroup>
                <ConfigrSelect
                    label={pageNumberingLabel}
                    path="languages.pageNumberStyle"
                    options={props.settings?.pageNumberStyles ?? []}
                />
            </ConfigrGroup>
        </ConfigrPage>
    );
}
