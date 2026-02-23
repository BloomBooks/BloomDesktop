import { css } from "@emotion/react";
import BloomButton from "../../react_components/bloomButton";
import { get, getBloomApiPrefix, post, postJson } from "../../utils/bloomApi";
import { useEffect, useState } from "react";
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
import Button from "@mui/material/Button";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Checkbox from "@mui/material/Checkbox";
import createCache, { EmotionCache } from "@emotion/cache";
import { CacheProvider } from "@emotion/react";
import { createPortal } from "react-dom";
import { Span } from "../../react_components/l10nComponents";

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

    return (
        <EditingControlDropdown
            enabled={props.enabled}
            l10nKey={l10nKey}
            tooltipL10nKey="EditTab.ContentLanguagesDropdown.ToolTip"
            disabledTooltipL10nKey="EditTab.ContentLanguagesDropdown.DisabledTooltip"
            menuName="contentLanguages"
            showChecks={true}
        />
    );
};

export const LayoutChoicesDropdown: React.FunctionComponent<{
    localizedText: string;
}> = (props) => {
    return (
        <EditingControlDropdown
            enabled={true}
            localizedText={props.localizedText}
            tooltipL10nKey={"EditTab.PageSizeAndOrientation.Tooltip"}
            menuName="layoutChoices"
            showChecks={false}
        />
    );
};

const normalizeTopBarMenuItems = (items: unknown): ITopBarMenuItem[] => {
    if (!Array.isArray(items)) {
        return [];
    }

    return items.map((item) => {
        const menuItem = item as any;
        return {
            id: String(menuItem.id ?? ""),
            label: String(menuItem.label ?? ""),
            enabled: menuItem.enabled !== false,
            checked:
                typeof menuItem.checked === "boolean"
                    ? menuItem.checked
                    : undefined,
        };
    });
};

export const EditingControlDropdown: React.FunctionComponent<{
    enabled: boolean;
    // Provide either l10nKey or localizedText
    l10nKey?: string;
    localizedText?: string;
    tooltipL10nKey: string;
    disabledTooltipL10nKey?: string;
    menuName: "contentLanguages" | "layoutChoices";
    showChecks: boolean;
}> = (props) => {
    const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
    const [parentAnchorEl, setParentAnchorEl] = useState<HTMLElement | null>(
        null,
    );
    const [parentContainer, setParentContainer] = useState<HTMLElement | null>(
        null,
    );
    const [parentEmotionCache, setParentEmotionCache] =
        useState<EmotionCache | null>(null);
    const [menuItems, setMenuItems] = useState<ITopBarMenuItem[]>([]);

    const buttonId = `${props.menuName}DropdownButton`;

    const loadMenuItems = (onLoaded?: (itemCount: number) => void) => {
        get(`editView/topBar/${props.menuName}Menu`, (result) => {
            const items = normalizeTopBarMenuItems(result.data?.items);
            setMenuItems(items);
            onLoaded?.(items.length);
        });
    };

    const onClose = () => {
        setAnchorEl(null);
        if (parentAnchorEl) {
            parentAnchorEl.remove();
            setParentAnchorEl(null);
        }
        setParentContainer(null);
        setParentEmotionCache(null);
    };

    const onOpen = (event: React.MouseEvent<HTMLButtonElement>) => {
        if (!props.enabled) {
            return;
        }

        const buttonElement = event.currentTarget;
        const parentWindow = window.parent;
        const parentDocument = parentWindow?.document;

        if (!parentDocument || parentDocument === document) {
            setAnchorEl(buttonElement);
            loadMenuItems();
            return;
        }

        const rect = buttonElement.getBoundingClientRect();
        const parentAnchor = parentDocument.createElement("div");
        parentAnchor.style.position = "fixed";
        parentAnchor.style.left = `${rect.left}px`;
        parentAnchor.style.top = `${rect.bottom}px`;
        parentAnchor.style.width = `${rect.width}px`;
        parentAnchor.style.height = "1px";
        parentAnchor.style.pointerEvents = "none";
        parentAnchor.style.zIndex = "2147483647";
        parentDocument.body.appendChild(parentAnchor);

        const cache = createCache({
            key: `${props.menuName}-menu-parent`,
            container: parentDocument.head,
            prepend: true,
        });

        setParentAnchorEl(parentAnchor);
        setParentContainer(parentDocument.body);
        setParentEmotionCache(cache);

        if (menuItems.length > 0) {
            setAnchorEl(parentAnchor);
            loadMenuItems();
            return;
        }

        loadMenuItems((itemCount) => {
            if (itemCount > 0) {
                setAnchorEl(parentAnchor);
            } else {
                parentAnchor.remove();
                setParentAnchorEl(null);
                setParentContainer(null);
                setParentEmotionCache(null);
            }
        });
    };

    const onMenuItemClick = (item: ITopBarMenuItem) => {
        if (!item.enabled) {
            return;
        }

        const payload: any = { id: item.id };
        if (props.menuName === "contentLanguages") {
            payload.checked = !item.checked;
        }

        postJson(`editView/topBar/${props.menuName}MenuAction`, payload);
        post("editView/updateTopBarDropdownDisplay");
        onClose();
    };

    const menu = (
        <Menu
            open={Boolean(anchorEl)}
            anchorEl={anchorEl}
            onClose={onClose}
            disablePortal={false}
            keepMounted={false}
            anchorOrigin={{ vertical: "bottom", horizontal: "left" }}
            transformOrigin={{ vertical: "top", horizontal: "left" }}
            container={parentContainer ?? undefined}
            slotProps={{
                paper: {
                    sx: {
                        minWidth: 220,
                        maxWidth: 440,
                    },
                },
            }}
        >
            {menuItems.map((item) => (
                <MenuItem
                    key={`${props.menuName}-${item.id}-${item.label}`}
                    onClick={() => onMenuItemClick(item)}
                    disabled={!item.enabled}
                    dense
                >
                    {props.showChecks ? (
                        <Checkbox
                            checked={Boolean(item.checked)}
                            size="small"
                            disableRipple
                            sx={{ padding: "0 6px 0 0" }}
                        />
                    ) : null}
                    {item.label}
                </MenuItem>
            ))}
        </Menu>
    );

    return (
        <BloomTooltip
            tip={{ l10nKey: props.tooltipL10nKey }}
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
                <Button
                    id={buttonId}
                    onClick={onOpen}
                    disabled={!props.enabled}
                    endIcon={<ArrowDropDown />}
                    size="small"
                    variant="text"
                    disableRipple
                    disableElevation
                    disableFocusRipple
                    disableTouchRipple
                    sx={{
                        fontSize: "11px",
                        p: "1px 5px 2px 5px",
                        textTransform: "none",
                        width: "fit-content",
                        minWidth: "unset",
                        backgroundColor: kBloomPurple,
                        color: kTextOnPurple,
                        border: "hidden",
                    }}
                >
                    {props.localizedText ? (
                        props.localizedText
                    ) : (
                        <Span l10nKey={props.l10nKey || ""} />
                    )}
                </Button>
                {parentContainer && parentEmotionCache
                    ? createPortal(
                          <CacheProvider value={parentEmotionCache}>
                              {menu}
                          </CacheProvider>,
                          parentContainer,
                      )
                    : menu}
            </>
        </BloomTooltip>
    );
};
