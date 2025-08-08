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
import { useL10n2 } from "../../react_components/l10nHooks";

interface IDropdownData {
    contentLanguagesEnabled: boolean;
    contentLanguagesNumber: number;
    layoutChoicesText: string;
}

export const EditTopBarControls: React.FunctionComponent = () => {
    const [buttonsEnabled, setButtonsEnabled] = useState({
        copy: true,
        cut: true,
        paste: true,
        undo: true
    });
    useSubscribeToWebSocketForObject<{
        enabled: {
            copy: boolean;
            cut: boolean;
            paste: boolean;
            undo: boolean;
        };
    }>("editTopBarControls", "updateEditButtons", results => {
        setButtonsEnabled(results.enabled);
    });

    // Start Dropdowns
    const [contentLanguagesEnabled, setContentLanguagesEnabled] = useState(
        false
    );
    const [contentLanguagesNumber, setContentLanguagesNumber] = useState(1);

    const [
        layoutChoicesLocalizedText,
        setLayoutChoicesLocalizedText
    ] = useState("");

    function setDropdowns(data: IDropdownData): void {
        setContentLanguagesEnabled(data.contentLanguagesEnabled);
        setContentLanguagesNumber(data.contentLanguagesNumber);
        setLayoutChoicesLocalizedText(data.layoutChoicesText);
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
                <PasteButton enabled={buttonsEnabled.paste ?? true} />
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        justify-content: space-evenly;
                        width: 72px;
                    `}
                >
                    <CutButton enabled={buttonsEnabled.cut ?? true} />
                    <CopyButton enabled={buttonsEnabled.copy ?? true} />
                    <div
                        css={css`
                            height: 6px;
                        `}
                    />
                </div>
                <UndoButton enabled={buttonsEnabled.undo ?? true} />
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
                        number={contentLanguagesNumber}
                    />
                    <div
                        css={css`
                            height: 3px;
                        `}
                    ></div>
                    <LayoutChoicesDropdown
                        localizedText={layoutChoicesLocalizedText}
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
}> = props => {
    const enabledIcon = imagesPrefix + "images/copy16x16.png";
    const disabledIcon = imagesPrefix + "images/copyDisabled16x16.png";
    const localizedTooltip = useL10n2({
        key: "EditTab.CopyButton.ToolTip"
    });
    const localizedDisabledTooltip = useL10n2({
        key: "EditTab.CopyButton.ToolTipWhenDisabled"
    });
    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.CopyButton"
            localizedTooltip={
                props.enabled ? localizedTooltip : localizedDisabledTooltip
            }
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
}> = props => {
    const enabledIcon = imagesPrefix + "images/cut16x16.png";
    const disabledIcon = imagesPrefix + "images/cutDisabled16x16.png";
    const localizedTooltip = useL10n2({
        key: "EditTab.CutButton.ToolTip"
    });
    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.CutButton"
            localizedTooltip={props.enabled ? localizedTooltip : undefined}
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
}> = props => {
    const enabledIcon = imagesPrefix + "images/paste32x32.png";
    const disabledIcon = imagesPrefix + "images/pasteDisabled32x32.png";

    const localizedTooltip = useL10n2({
        key: "EditTab.PasteButton.ToolTip"
    });
    const localizedDisabledTooltip = useL10n2({
        key: "EditTab.PasteButton.ToolTipWhenDisabled"
    });
    const cssAttributes = `${largeButtonCSSAttributes}
                padding-top: 6px;`;
    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.PasteButton"
            localizedTooltip={
                props.enabled ? localizedTooltip : localizedDisabledTooltip
            }
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
}> = props => {
    const enabledIcon = imagesPrefix + "images/undo32x32.png";
    const disabledIcon = imagesPrefix + "images/undoDisabled32x32.png";

    const localizedTooltip = useL10n2({
        key: "EditTab.UndoButton.ToolTip"
    });
    const localizedDisabledTooltip = useL10n2({
        key: "EditTab.UndoButton.ToolTipWhenDisabled"
    });
    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.UndoButton"
            localizedTooltip={
                props.enabled ? localizedTooltip : localizedDisabledTooltip
            }
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
    l10nKey: string;
    localizedTooltip?: string;
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
            tip={props.localizedTooltip}
            placement="right"
            slotProps={{
                tooltip: { sx: { maxWidth: "167px", "font-size": "11px" } }
            }}
        >
            <BloomButton
                enabled={props.enabled}
                l10nKey={props.l10nKey}
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
            ></BloomButton>
        </BloomTooltip>
    );
};

export const ContentLanguagesDropdown: React.FunctionComponent<{
    enabled: boolean;
    number: number;
}> = props => {
    let l10nKey;
    switch (props.number) {
        case 1:
            l10nKey = "EditTab.Monolingual";
            break;
        case 2:
            l10nKey = "EditTab.Bilingual";
            break;
        case 3:
            l10nKey = "EditTab.Trilingual";
            break;
    }

    const localizedTooltip = useL10n2({
        key: "EditTab.ContentLanguagesDropdown.ToolTip"
    });
    const localizedDisabledTooltip = useL10n2({
        key: "EditTab.ContentLanguagesDropdown.DisabledTooltip"
    });
    return (
        <EditingControlDropdown
            enabled={props.enabled}
            l10nKey={l10nKey}
            localizedTooltip={
                props.enabled ? localizedTooltip : localizedDisabledTooltip
            }
            onClickAction="contentLanguages"
        />
    );
};

export const LayoutChoicesDropdown: React.FunctionComponent<{
    localizedText: string;
}> = props => {
    const localizedTooltip = useL10n2({
        key: "EditTab.PageSizeAndOrientation.Tooltip"
    });
    return (
        <EditingControlDropdown
            enabled={true}
            localizedText={props.localizedText}
            localizedTooltip={localizedTooltip}
            onClickAction="layoutChoices"
        />
    );
};

export const EditingControlDropdown: React.FunctionComponent<{
    enabled: boolean;
    // Provide either l10nKey or localizedText
    l10nKey?: string;
    localizedText?: string;
    localizedTooltip?: string;
    onClickAction: "contentLanguages" | "layoutChoices";
}> = props => {
    return (
        <BloomTooltip
            tip={props.localizedTooltip}
            showDisabled={!props.enabled}
            tipWhenDisabled={props.localizedTooltip}
            slotProps={{
                tooltip: { sx: { maxWidth: "167px", "font-size": "11px" } }
            }}
        >
            <BloomButton
                enabled={props.enabled}
                l10nKey={props.l10nKey || ""}
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
                {props.localizedText}
            </BloomButton>
        </BloomTooltip>
    );
};

WireUpForWinforms(EditTopBarControls);
