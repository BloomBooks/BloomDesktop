import { css, ThemeProvider } from "@emotion/react";
import BloomButton from "../../react_components/bloomButton";
import { get, getBloomApiPrefix, postJson } from "../../utils/bloomApi";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { useEffect, useState } from "react";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";
import {
    kBloomPurple,
    kDisabledTextOnPurple,
    kTextOnPurple,
    kUiFontStack,
    lightTheme
} from "../../bloomMaterialUITheme";
import { ArrowDropDown } from "@mui/icons-material";

interface IL10nSet {
    key: string;
    english: string;
    comment: string;
    localizedTip: string;
}
interface IButtonTips {
    copy: string;
    cut: string;
    paste: string;
    undo: string;
}
interface IDropdownData {
    contentLanguagesEnabled: boolean;
    contentLanguagesNumber: number;
    contentLanguagesTooltip: string;
    layoutChoicesText: string;
    layoutChoicesTooltip: string;
}

export const EditTopBarControls: React.FunctionComponent = () => {
    const [buttonsEnabled, setButtonsEnabled] = useState({
        copy: true,
        cut: true,
        paste: true,
        undo: true
    });
    const blankl10nSet = {
        key: "",
        english: "",
        comment: "",
        localizedTip: ""
    };
    const [buttonsl10nSet, setButtonsl10nSet] = useState({
        copy: blankl10nSet,
        cut: blankl10nSet,
        paste: blankl10nSet,
        undo: blankl10nSet
    });
    useSubscribeToWebSocketForObject<{
        message: {
            enabled: {
                copy: boolean;
                cut: boolean;
                paste: boolean;
                undo: boolean;
            };
            localizedTip: IButtonTips;
        };
    }>("editTopBarControls", "updateEditButtons", results => {
        setButtonsEnabled(results.message.enabled);
        resetButtonsl10n(results.message.localizedTip);
    });

    function resetButtonsl10n(localizedTip: IButtonTips) {
        setButtonsl10nSet({
            copy: blankl10nSet,
            cut: blankl10nSet,
            paste: blankl10nSet,
            undo: blankl10nSet
        });
        setButtonsl10nSet({
            copy: {
                key: "EditTab.CopyButton",
                english: "Copy",
                comment: "Button to copy what is selected",
                localizedTip: localizedTip.copy
            },
            cut: {
                key: "EditTab.CutButton",
                english: "Cut",
                comment: "",
                localizedTip: localizedTip.cut
            },
            paste: {
                key: "EditTab.PasteButton",
                english: "Paste",
                comment: "Button to paste what is on the Clipboard.",
                localizedTip: localizedTip.paste
            },
            undo: {
                key: "EditTab.UndoButton",
                english: "Undo",
                comment: "Button to undo last action",
                localizedTip: localizedTip.undo
            }
        });
    }

    // Start Dropdowns
    const [contentLanguagesEnabled, setContentLanguagesEnabled] = useState(
        false
    );
    const [contentLanguagesl10nSet, setContentLanguagesl10nSet] = useState(
        blankl10nSet
    );

    const [
        layoutChoicesLocalizedText,
        setLayoutChoicesLocalizedText
    ] = useState("");
    const [layoutChoicesTooltip, setLayoutChoicesTooltip] = useState("");

    function setDropdowns(data: IDropdownData): void {
        setContentLanguagesEnabled(data.contentLanguagesEnabled);
        // l10n has to be reset so that if language is changed, it will reevaluate the translation
        setContentLanguagesl10nSet(blankl10nSet);
        switch (data.contentLanguagesNumber) {
            case 1:
                setContentLanguagesl10nSet({
                    key: "EditTab.Monolingual",
                    english: "One Language",
                    comment:
                        "Shown in edit tab multilingualism chooser, for monolingual mode, one language per page",
                    localizedTip: data.contentLanguagesTooltip
                });
                break;
            case 2:
                setContentLanguagesl10nSet({
                    key: "EditTab.Bilingual",
                    english: "Two Languages",
                    comment:
                        "Shown in edit tab multilingualism chooser, for bilingual mode, 2 languages per page",
                    localizedTip: data.contentLanguagesTooltip
                });
                break;
            case 3:
                setContentLanguagesl10nSet({
                    key: "EditTab.Trilingual",
                    english: "Three Languages",
                    comment:
                        "Shown in edit tab multilingualism chooser, for trilingual mode, 3 languages per page",
                    localizedTip: data.contentLanguagesTooltip
                });
                break;
        }
        setLayoutChoicesLocalizedText(data.layoutChoicesText);
        setLayoutChoicesTooltip(data.layoutChoicesTooltip);
    }

    // for the first load after opening a collection - websocket will make updates after that
    useEffect(() => {
        get("editView/updateTopBarDropdownDisplay", results => {
            setDropdowns(results.data);
        });
    }, []);
    useSubscribeToWebSocketForObject<{
        message: IDropdownData;
    }>("editTopBarControls", "updateDropdowns", results => {
        setDropdowns(results.message);
    });

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                id="topBarControlsRoot"
                css={css`
                    background-color: ${kBloomPurple};
                    display: flex;
                    overflow-y: hidden;
                    padding-top: 4px;
                    padding-bottom: 2px;
                    padding-inline: 3px;
                `}
            >
                <PasteButton
                    enabled={buttonsEnabled.paste ?? true}
                    l10nSet={buttonsl10nSet.paste}
                />
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        justify-content: space-evenly;
                        width: 72px;
                    `}
                >
                    <CutButton
                        enabled={buttonsEnabled.cut ?? true}
                        l10nSet={buttonsl10nSet.cut}
                    />
                    <CopyButton
                        enabled={buttonsEnabled.copy ?? true}
                        l10nSet={buttonsl10nSet.copy}
                    />
                    <div
                        css={css`
                            height: 6px;
                        `}
                    />
                </div>
                <UndoButton
                    enabled={buttonsEnabled.undo ?? true}
                    l10nSet={buttonsl10nSet.undo}
                />
                <div
                    css={css`
                        width: 18px;
                    `}
                />
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        font-size: 11px;
                    `}
                >
                    <ContentLanguagesDropdown
                        enabled={contentLanguagesEnabled}
                        l10nSet={contentLanguagesl10nSet}
                    />
                    <div
                        css={css`
                            height: 3px;
                        `}
                    ></div>
                    <LayoutChoicesDropdown
                        localizedText={layoutChoicesLocalizedText}
                        localizedTooltip={layoutChoicesTooltip}
                    />
                </div>
            </div>
        </ThemeProvider>
    );
};

const imagesPrefix = getBloomApiPrefix(false);

const smallButtonCSSAttributes = `height: 18px;
    display: flex;
    justify-content: flex-start;
    span {
        padding-left: 5px;
    }`;

// We have to pass l10n in here instead of having it here directly, otherwise it won't update when the language is changed
export const CopyButton: React.FunctionComponent<{
    enabled: boolean;
    l10nSet: IL10nSet;
}> = props => {
    const enabledIcon = imagesPrefix + "images/copy16x16.png";
    const disabledIcon = imagesPrefix + "images/copyDisabled16x16.png";

    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nSet={props.l10nSet}
            onClickAction="copy"
            enabledIcon={enabledIcon}
            disabledIcon={disabledIcon}
            iconBeforeText={true}
            cssAttributes={smallButtonCSSAttributes}
        />
    );
};

export const CutButton: React.FunctionComponent<{
    enabled: boolean;
    l10nSet: IL10nSet;
}> = props => {
    const enabledIcon = imagesPrefix + "images/cut16x16.png";
    const disabledIcon = imagesPrefix + "images/cutDisabled16x16.png";

    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nSet={props.l10nSet}
            onClickAction="cut"
            enabledIcon={enabledIcon}
            disabledIcon={disabledIcon}
            iconBeforeText={false}
            cssAttributes={smallButtonCSSAttributes}
        />
    );
};

const largeButtonCSSAttributes = `width: 54px;
    height: 60px;
    display: grid;
    justify-items: center;
    align-content: space-between;`;

export const PasteButton: React.FunctionComponent<{
    enabled: boolean;
    l10nSet: IL10nSet;
}> = props => {
    const enabledIcon = imagesPrefix + "images/paste32x32.png";
    const disabledIcon = imagesPrefix + "images/pasteDisabled32x32.png";

    const cssAttributes = `${largeButtonCSSAttributes}
                padding-top: 6px;`;
    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nSet={props.l10nSet}
            onClickAction="paste"
            enabledIcon={enabledIcon}
            disabledIcon={disabledIcon}
            iconBeforeText={true}
            cssAttributes={cssAttributes}
        />
    );
};

export const UndoButton: React.FunctionComponent<{
    enabled: boolean;
    l10nSet: IL10nSet;
}> = props => {
    const enabledIcon = imagesPrefix + "images/undo32x32.png";
    const disabledIcon = imagesPrefix + "images/undoDisabled32x32.png";

    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nSet={props.l10nSet}
            onClickAction="undo"
            enabledIcon={enabledIcon}
            disabledIcon={disabledIcon}
            iconBeforeText={true}
            cssAttributes={largeButtonCSSAttributes}
        />
    );
};

export const EditingControlButton: React.FunctionComponent<{
    enabled: boolean;
    l10nSet: IL10nSet;
    onClickAction:
        | "copy"
        | "cut"
        | "paste"
        | "undo"
        | "contentLanguages"
        | "layoutChoices";
    enabledIcon?: string;
    disabledIcon?: string;
    iconBeforeText?: boolean;
    cssAttributes?: string;
}> = props => {
    return (
        <BloomTooltip
            tip={props.l10nSet.localizedTip}
            placement="right"
            slotProps={{
                tooltip: { sx: { maxWidth: "167px", "font-size": "11px" } }
            }}
        >
            <BloomButton
                enabled={props.enabled}
                l10nKey={props.l10nSet.key}
                l10nComment={props.l10nSet.comment}
                onClick={() => {
                    postJson("editView/topBarControls", {
                        command: props.onClickAction
                    });
                }}
                enabledImageFile={props.enabledIcon}
                disabledImageFile={props.disabledIcon}
                iconBeforeText={props.iconBeforeText}
                transparent={true}
                hasText={true}
                css={css`
                    background-color: ${kBloomPurple};
                    color: ${!props.enabled
                        ? kDisabledTextOnPurple
                        : kTextOnPurple};
                    border: hidden;
                    font-family: ${kUiFontStack};
                    font-size: 11px;

                    ${props.enabled
                        ? `:active {
                            transform: translate3d(1px, 1px, 0px);
                        }`
                        : ``}

                    ${props.cssAttributes}
                `}
            >
                {props.l10nSet.english}
            </BloomButton>
        </BloomTooltip>
    );
};

export const ContentLanguagesDropdown: React.FunctionComponent<{
    enabled: boolean;
    l10nSet: IL10nSet;
}> = props => {
    return (
        <EditingControlDropdown
            enabled={props.enabled}
            l10nSet={props.l10nSet}
            onClickAction="contentLanguages"
        />
    );
};

// typescript l10n was having trouble localizing layouts, so C# is doing this
export const LayoutChoicesDropdown: React.FunctionComponent<{
    localizedText: string;
    localizedTooltip: string;
}> = props => {
    return (
        <EditingControlDropdown
            enabled={true}
            l10nEnglish={props.localizedText}
            localizedTooltip={props.localizedTooltip}
            onClickAction="layoutChoices"
        />
    );
};

export const EditingControlDropdown: React.FunctionComponent<{
    enabled: boolean;
    l10nSet?: IL10nSet;
    l10nEnglish?: string;
    localizedTooltip?: string;
    onClickAction: "contentLanguages" | "layoutChoices";
}> = props => {
    return (
        <BloomTooltip
            tip={props.l10nSet?.localizedTip || props.localizedTooltip}
            showDisabled={!props.enabled}
            tipWhenDisabled={props.localizedTooltip}
            slotProps={{
                tooltip: { sx: { maxWidth: "167px", "font-size": "11px" } }
            }}
        >
            <BloomButton
                enabled={props.enabled}
                l10nKey={props.l10nSet?.key || ""}
                l10nComment={props.l10nSet?.comment || ""}
                onClick={() => {
                    postJson("editView/topBarDropdownClicked", {
                        command: props.onClickAction
                    });
                }}
                hasText={true}
                variant="text"
                endIcon={<ArrowDropDown />}
                css={css`
                    background-color: ${kBloomPurple};
                    color: ${kTextOnPurple};
                    border: hidden;
                    font-size: 11px;
                    padding-inline: 5px;
                    padding-top: 1px;
                    padding-bottom: 2px;
                    text-transform: none;
                    width: fit-content;
                `}
            >
                {props.l10nSet?.english || props.l10nEnglish}
            </BloomButton>
        </BloomTooltip>
    );
};

WireUpForWinforms(EditTopBarControls);
