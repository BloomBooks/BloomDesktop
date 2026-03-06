import { css } from "@emotion/react";
import BloomButton from "../../react_components/bloomButton";
import { get, getBloomApiPrefix, post, postJson } from "../../utils/bloomApi";
import { useCallback, useEffect, useState } from "react";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";
import {
    kBloomPurple,
    kDisabledTextOnPurple,
    kTextOnPurple,
    kUiFontStack,
} from "../../bloomMaterialUITheme";
import { ArrowDropDown } from "@mui/icons-material";
import { BookSettingsButton } from "../../react_components/BookSettingsButton";
import Menu from "@mui/material/Menu";
import Checkbox from "@mui/material/Checkbox";
import { useL10n } from "../../react_components/l10nHooks";
import { useParentFrameMenuPortal } from "../../react_components/TopBar/useParentFrameMenuPortal";
import { LocalizableMenuItem } from "../../react_components/localizableMenuItem";

interface IDropdownData {
    contentLanguagesEnabled: boolean;
    contentLanguagesNumber: number;
    layoutChoicesText: string;
}

interface ITopBarMenuItem {
    id: string;
    label: string;
    enabled: boolean;
    checked?: boolean;
}

const normalizeContentLanguageUsageItems = (
    languages: unknown,
): ITopBarMenuItem[] => {
    if (!Array.isArray(languages)) {
        return [];
    }

    const normalized = languages.map((language) => {
        const languageInfo = language as Record<string, unknown>;
        return {
            id: String(languageInfo.id ?? ""),
            label: String(languageInfo.label ?? ""),
            checked: Boolean(languageInfo.isUsedForContent),
        };
    });

    const selectedCount = normalized.filter(
        (language) => language.checked,
    ).length;
    return normalized.map((language) => ({
        id: language.id,
        label: language.label,
        checked: language.checked,
        enabled: !language.checked || selectedCount > 1,
    }));
};

const normalizeLayoutChoiceItems = (
    choices: unknown,
    currentLayoutChoiceId: unknown,
    noOtherLayoutsText: string,
): ITopBarMenuItem[] => {
    if (!Array.isArray(choices)) {
        return [];
    }

    const currentId = String(currentLayoutChoiceId ?? "");
    const normalizedChoices = choices.map((choice) => {
        const choiceInfo = choice as Record<string, unknown>;
        const id = String(choiceInfo.id ?? "");
        return {
            id,
            label: String(choiceInfo.label ?? ""),
            enabled: true,
            checked: id === currentId,
        };
    });

    if (normalizedChoices.length < 2) {
        normalizedChoices.push({
            id: "",
            label: noOtherLayoutsText,
            enabled: false,
            checked: false,
        });
    }

    return normalizedChoices;
};

export const EditTopBarControls: React.FunctionComponent = () => {
    const [buttonsEnabled, setButtonsEnabled] = useState({
        copy: true,
        cut: true,
        paste: true,
        undo: true,
    });
    useSubscribeToWebSocketForObject<{
        enabled: {
            copy: boolean;
            cut: boolean;
            paste: boolean;
            undo: boolean;
        };
    }>("editTopBarControls", "updateEditButtons", (results) => {
        setButtonsEnabled(results.enabled);
    });

    // Start Dropdowns
    const [contentLanguagesEnabled, setContentLanguagesEnabled] =
        useState(false);
    const [contentLanguagesNumber, setContentLanguagesNumber] = useState(1);

    const [layoutChoicesLocalizedText, setLayoutChoicesLocalizedText] =
        useState("");

    function setDropdowns(data: IDropdownData): void {
        setContentLanguagesEnabled(data.contentLanguagesEnabled);
        setContentLanguagesNumber(data.contentLanguagesNumber);
        setLayoutChoicesLocalizedText(data.layoutChoicesText);
    }

    // Ask the backend for the initial dropdown state on mount, because this state
    // is sourced from C# and cannot be derived from React state alone.
    useEffect(() => {
        post("editView/updateTopBarDropdownDisplay");
    }, []);
    useSubscribeToWebSocketForObject<{
        message: IDropdownData;
    }>("editTopBarControls", "updateDropdowns", (results) => {
        setDropdowns(results.message);
    });

    return (
        <div
            css={css`
                display: flex;
                justify-content: space-between;
                width: 100%;
            `}
        >
            <div
                css={css`
                    // We originally set this to ${kBloomPurple}, but on some displays,
                    // something caused a slightly different color to be displayed for the control
                    // background compared to the WinForms bar it sits on.
                    // This should improve things. Though now the buttons are probably slightly different
                    // on that display. We could, in theory, do something similar for the buttons,
                    // but that would probably break any hover/active/click effects.
                    // This problem will go away when the whole top bar is react.
                    background-color: transparent;
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
            <BookSettingsButton />
        </div>
    );
};

const imagesPrefix = getBloomApiPrefix(false);

const smallButtonCSSAttributes = `height: 18px;
    display: flex;
    justify-content: flex-start;
    span {
        padding-left: 5px;
    }`;

export const CopyButton: React.FunctionComponent<{
    enabled: boolean;
}> = (props) => {
    const enabledIcon = imagesPrefix + "images/copy16x16.png";
    const disabledIcon = imagesPrefix + "images/copyDisabled16x16.png";

    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.CopyButton"
            tooltipL10nKey="EditTab.CopyButton.ToolTip"
            disabledTooltipL10nKey="EditTab.CopyButton.ToolTipWhenDisabled"
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
}> = (props) => {
    const enabledIcon = imagesPrefix + "images/cut16x16.png";
    const disabledIcon = imagesPrefix + "images/cutDisabled16x16.png";
    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.CutButton"
            tooltipL10nKey="EditTab.CutButton.ToolTip"
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
}> = (props) => {
    const enabledIcon = imagesPrefix + "images/paste32x32.png";
    const disabledIcon = imagesPrefix + "images/pasteDisabled32x32.png";

    const cssAttributes = `${largeButtonCSSAttributes}
                padding-top: 6px;`;
    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.PasteButton"
            tooltipL10nKey="EditTab.PasteButton.ToolTip"
            disabledTooltipL10nKey="EditTab.PasteButton.ToolTipWhenDisabled"
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
}> = (props) => {
    const enabledIcon = imagesPrefix + "images/undo32x32.png";
    const disabledIcon = imagesPrefix + "images/undoDisabled32x32.png";

    return (
        <EditingControlButton
            enabled={props.enabled}
            l10nKey="EditTab.UndoButton"
            tooltipL10nKey="EditTab.UndoButton.ToolTip"
            disabledTooltipL10nKey="EditTab.UndoButton.ToolTipWhenDisabled"
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
    tooltipL10nKey: string;
    disabledTooltipL10nKey?: string;
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
}> = (props) => {
    return (
        <BloomTooltip
            tip={{ l10nKey: props.tooltipL10nKey }}
            tipWhenDisabled={
                props.disabledTooltipL10nKey
                    ? { l10nKey: props.disabledTooltipL10nKey }
                    : undefined
            }
            showDisabled={!props.enabled}
            placement="right"
            slotProps={{
                tooltip: { sx: { maxWidth: "167px", "font-size": "11px" } },
            }}
        >
            <BloomButton
                enabled={props.enabled}
                l10nKey={props.l10nKey}
                onMouseDown={(e) => {
                    // Keep focus in the main editable browser; otherwise this button takes focus
                    // first and copy/cut/paste/undo may run against the wrong context.
                    e.preventDefault();
                }}
                onClick={() => {
                    postJson("editView/topBarButtonClick", {
                        command: props.onClickAction,
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
}> = (props) => {
    const monolingualText = useL10n("Monolingual", "EditTab.Monolingual");
    const bilingualText = useL10n("Bilingual", "EditTab.Bilingual");
    const trilingualText = useL10n("Trilingual", "EditTab.Trilingual");

    let buttonText = "";
    switch (props.number) {
        case 1:
            buttonText = monolingualText;
            break;
        case 2:
            buttonText = bilingualText;
            break;
        case 3:
            buttonText = trilingualText;
            break;
    }

    const loadMenuItems = (onLoaded?: (itemCount: number) => void) => {
        get("editView/topBar/contentLanguageUsage", (result) => {
            const items = normalizeContentLanguageUsageItems(
                result.data?.languages,
            );
            onLoaded?.(items.length);
            setContentLanguageMenuItems(items);
        });
    };

    const [contentLanguageMenuItems, setContentLanguageMenuItems] = useState<
        ITopBarMenuItem[]
    >([]);

    const onMenuItemClick = (item: ITopBarMenuItem) => {
        postJson("editView/topBar/contentLanguageUsageChange", {
            languageTag: item.id,
            isUsedForContent: !item.checked,
        });
        post("editView/updateTopBarDropdownDisplay");
    };

    return (
        <EditingControlDropdown
            enabled={props.enabled}
            localizedText={buttonText}
            tooltipL10nKey="EditTab.ContentLanguagesDropdown.ToolTip"
            disabledTooltipL10nKey="EditTab.ContentLanguagesDropdown.DisabledTooltip"
            buttonId="contentLanguagesDropdownButton"
            menuItems={contentLanguageMenuItems}
            loadMenuItems={loadMenuItems}
            onMenuItemClick={onMenuItemClick}
            showChecks={true}
        />
    );
};

export const LayoutChoicesDropdown: React.FunctionComponent<{
    localizedText: string;
}> = (props) => {
    const [layoutChoiceMenuItems, setLayoutChoiceMenuItems] = useState<
        ITopBarMenuItem[]
    >([]);
    const [fallbackLocalizedText, setFallbackLocalizedText] = useState("");
    const noOtherLayoutsText = useL10n(
        "There are no other options for this template.",
        "EditTab.NoOtherLayouts",
        "Show in the size/orientation chooser dropdown of the edit tab, if there was only a single choice",
    );

    const loadLayoutChoiceData = useCallback(
        (onLoaded?: (itemCount: number) => void) => {
            get("editView/topBar/layoutChoiceData", (result) => {
                const items = normalizeLayoutChoiceItems(
                    result.data?.choices,
                    result.data?.currentLayoutChoiceId,
                    noOtherLayoutsText,
                );
                const currentChoice = items.find((item) => item.checked);
                if (currentChoice?.label) {
                    setFallbackLocalizedText(currentChoice.label);
                }
                onLoaded?.(items.length);
                setLayoutChoiceMenuItems(items);
            });
        },
        [noOtherLayoutsText],
    );

    const loadMenuItems = (onLoaded?: (itemCount: number) => void) => {
        loadLayoutChoiceData(onLoaded);
    };

    useEffect(() => {
        loadLayoutChoiceData();
    }, [loadLayoutChoiceData]);

    const onMenuItemClick = (item: ITopBarMenuItem) => {
        setFallbackLocalizedText(item.label);
        postJson("editView/topBar/layoutChoiceChange", {
            layoutChoiceId: item.id,
        });
        post("editView/updateTopBarDropdownDisplay");
    };

    return (
        <EditingControlDropdown
            enabled={true}
            localizedText={props.localizedText || fallbackLocalizedText}
            tooltipL10nKey={"EditTab.PageSizeAndOrientation.Tooltip"}
            buttonId="layoutChoicesDropdownButton"
            menuItems={layoutChoiceMenuItems}
            loadMenuItems={loadMenuItems}
            onMenuItemClick={onMenuItemClick}
            showChecks={false}
        />
    );
};

export const EditingControlDropdown: React.FunctionComponent<{
    enabled: boolean;
    localizedText: string;
    tooltipL10nKey: string;
    disabledTooltipL10nKey?: string;
    buttonId: string;
    menuItems: ITopBarMenuItem[];
    loadMenuItems: (onLoaded?: (itemCount: number) => void) => void;
    onMenuItemClick: (item: ITopBarMenuItem) => void;
    showChecks: boolean;
}> = (props) => {
    const {
        suppressTooltip,
        closeMenu,
        openMenuAtButtonWithItemsLoader,
        suppressTooltipUntilPointerReset,
        clearTooltipSuppression,
        releaseTooltipSuppressionIfMenuClosed,
        getRootMenuProps,
        renderMenuInParentFrame,
    } = useParentFrameMenuPortal();

    const onClose = () => {
        closeMenu();
    };

    const onOpen = () => {
        if (!props.enabled) {
            return;
        }
        suppressTooltipUntilPointerReset();

        const opened = openMenuAtButtonWithItemsLoader(
            props.buttonId,
            `${props.buttonId}-menu-parent`,
            props.menuItems.length,
            props.loadMenuItems,
        );
        if (!opened) {
            clearTooltipSuppression();
        }
    };

    const onMenuItemClick = (item: ITopBarMenuItem) => {
        if (!item.enabled) {
            return;
        }
        props.onMenuItemClick(item);
        onClose();
    };

    const menu = (
        <Menu {...getRootMenuProps(onClose)}>
            {props.menuItems.map((item) => (
                <LocalizableMenuItem
                    key={`${props.buttonId}-${item.id}-${item.label}`}
                    english={item.label}
                    l10nId={null}
                    onClick={() => onMenuItemClick(item)}
                    disabled={!item.enabled}
                    icon={
                        props.showChecks ? (
                            <Checkbox
                                checked={Boolean(item.checked)}
                                size="small"
                                disableRipple
                                css={css`
                                    padding: 0 6px 0 0;
                                `}
                            />
                        ) : undefined
                    }
                    dontGiveAffordanceForCheckbox={!props.showChecks}
                />
            ))}
        </Menu>
    );

    return (
        <BloomTooltip
            tip={
                suppressTooltip ? undefined : { l10nKey: props.tooltipL10nKey }
            }
            tipWhenDisabled={
                props.disabledTooltipL10nKey
                    ? { l10nKey: props.disabledTooltipL10nKey }
                    : undefined
            }
            showDisabled={!props.enabled}
            slotProps={{
                tooltip: { sx: { maxWidth: "167px", "font-size": "11px" } },
            }}
        >
            <>
                <BloomButton
                    id={props.buttonId}
                    onClick={() => onOpen()}
                    onMouseEnter={releaseTooltipSuppressionIfMenuClosed}
                    onMouseLeave={releaseTooltipSuppressionIfMenuClosed}
                    enabled={props.enabled}
                    l10nKey={props.buttonId}
                    alreadyLocalized={true}
                    iconBeforeText={<ArrowDropDown />}
                    size="small"
                    variant="text"
                    disableRipple
                    disableElevation
                    disableFocusRipple
                    disableTouchRipple
                    css={css`
                        font-size: 11px;
                        padding: 1px 5px 2px 5px;
                        text-transform: none;
                        width: fit-content;
                        min-width: unset;
                        background-color: ${kBloomPurple};
                        color: ${kTextOnPurple};
                        border: hidden;
                        flex-direction: row-reverse;

                        .MuiButton-startIcon {
                            margin-right: 0;
                            margin-left: 4px;
                        }
                    `}
                >
                    {props.localizedText}
                </BloomButton>
                {renderMenuInParentFrame(menu)}
            </>
        </BloomTooltip>
    );
};
