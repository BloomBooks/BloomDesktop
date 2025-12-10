/** @jsxImportSource @emotion/react */
import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import BloomButton from "../../bloomButton";
import { BloomTooltip } from "../../BloomToolTip";
import { get, postJson } from "../../../utils/bloomApi";
import WebSocketManager from "../../../utils/WebSocketManager";
import { lightTheme } from "../../../bloomMaterialUITheme";
import { WireUpForWinforms } from "../../../utils/WireUpWinform";
import { Menu, MenuItem, ListItemText } from "@mui/material";
import { ArrowDropDown, HelpOutline } from "@mui/icons-material";

interface TopRightState {
    uiLanguageLabel: string;
    showUnapprovedText: string;
    showUnapprovedChecked: boolean;
    zoom: number;
    zoomEnabled: boolean;
    minZoom: number;
    maxZoom: number;
}

interface UiLanguageItem {
    langTag: string;
    menuText: string;
    tooltip: string;
    isCurrent: boolean;
}

interface HelpMenuItemModel {
    id: string;
    text: string;
    isSeparator: boolean;
    enabled: boolean;
}
interface TopRightProps {
    initialState?: TopRightState;
    initialLanguages?: UiLanguageItem[];
    initialHelpItems?: HelpMenuItemModel[];
    skipApi?: boolean;
}

export const WorkspaceTopRightControls: React.FunctionComponent<
    TopRightProps
> = (props) => {
    const [state, setState] = useState<TopRightState | undefined>(undefined);
    const [languageAnchor, setLanguageAnchor] = useState<HTMLElement | null>(
        null,
    );
    const [helpAnchor, setHelpAnchor] = useState<HTMLElement | null>(null);
    const [languages, setLanguages] = useState<UiLanguageItem[]>([]);
    const [helpItems, setHelpItems] = useState<HelpMenuItemModel[]>([]);
    const [zoomField, setZoomField] = useState<string>("");
    const anchorRef = React.useRef<HTMLDivElement | null>(null);

    const refreshState = React.useCallback(() => {
        if (props.skipApi && props.initialState) {
            setState(props.initialState);
            setZoomField(`${props.initialState.zoom}%`);
            return;
        }
        if (props.skipApi) {
            return;
        }
        get("workspace/topRight/state", (result) => {
            setState(result.data as TopRightState);
            setZoomField(`${(result.data as TopRightState).zoom}%`);
        });
    }, [props.initialState, props.skipApi]);

    useEffect(() => {
        refreshState();
    }, [refreshState]);

    useEffect(() => {
        if (props.skipApi) {
            return;
        }
        const listener = (e) => {
            if (e.id === "state") {
                const results = e as TopRightState;
                setState(results);
                setZoomField(`${results.zoom}%`);
            }
        };
        WebSocketManager.addListener("workspaceTopRightControls", listener);
        return () =>
            WebSocketManager.removeListener(
                "workspaceTopRightControls",
                listener,
            );
    }, [props.skipApi]);

    const openLanguageMenu = (target?: HTMLElement | null) => {
        if (!target) {
            return;
        }
        if (props.skipApi && props.initialLanguages) {
            setLanguages(props.initialLanguages);
            setLanguageAnchor(target);
            return;
        }
        if (props.skipApi) {
            return;
        }
        get("workspace/topRight/languages", (result) => {
            setLanguages(result.data as UiLanguageItem[]);
            setLanguageAnchor(target);
        });
    };

    const openHelpMenu = (target?: HTMLElement | null) => {
        if (!target) {
            return;
        }
        if (props.skipApi && props.initialHelpItems) {
            setHelpItems(props.initialHelpItems);
            setHelpAnchor(target);
            return;
        }
        if (props.skipApi) {
            return;
        }
        get("workspace/topRight/helpItems", (result) => {
            setHelpItems(result.data as HelpMenuItemModel[]);
            setHelpAnchor(target);
        });
    };

    const applyLanguage = (langTag: string) => {
        if (!props.skipApi) {
            postJson("workspace/topRight/setLanguage", { langTag });
        }
        setLanguageAnchor(null);
    };

    const toggleShowUnapproved = () => {
        if (!props.skipApi) {
            postJson("workspace/topRight/toggleShowUnapproved", {});
        }
        setLanguageAnchor(null);
    };

    const runHelpCommand = (id: string) => {
        if (!props.skipApi) {
            postJson("workspace/topRight/helpCommand", { id });
        }
        setHelpAnchor(null);
    };

    const changeZoom = (delta: number) => {
        if (!state || !state.zoomEnabled) {
            return;
        }
        const clamped = Math.min(
            Math.max(state.zoom + delta, state.minZoom),
            state.maxZoom,
        );
        setState({ ...state, zoom: clamped });
        setZoomField(`${clamped}%`);
        if (!props.skipApi) {
            postJson("workspace/topRight/zoom", { zoom: clamped });
        }
    };

    const renderLanguageMenuItems = () => {
        if (!state) {
            return null;
        }
        return (
            <>
                {languages.map((lang) => (
                    <MenuItem
                        key={lang.langTag}
                        selected={lang.isCurrent}
                        onClick={() => applyLanguage(lang.langTag)}
                    >
                        <ListItemText primary={lang.menuText} />
                    </MenuItem>
                ))}
                <MenuItem onClick={toggleShowUnapproved}>
                    <ListItemText
                        primary={state.showUnapprovedText}
                        sx={{
                            fontStyle: state.showUnapprovedChecked
                                ? "italic"
                                : "normal",
                        }}
                    />
                </MenuItem>
            </>
        );
    };

    const renderHelpMenuItems = () => {
        return helpItems.map((item, index) => {
            if (item.isSeparator) {
                return <MenuItem key={`sep-${index}`} divider disabled />;
            }
            return (
                <MenuItem
                    key={item.id}
                    onClick={() => runHelpCommand(item.id)}
                    disabled={!item.enabled}
                >
                    <ListItemText primary={item.text} />
                </MenuItem>
            );
        });
    };

    if (!state) {
        return null;
    }

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                ref={anchorRef}
                css={css`
                    display: flex;
                    flex-direction: column;
                    align-items: end;
                    background-color: transparent;
                    color: white;
                `}
            >
                <BloomTooltip
                    tip={{ l10nKey: "CollectionTab.LanguageMenu.Tooltip" }}
                >
                    <BloomButton
                        l10nKey="CollectionTab.LanguageMenu"
                        enabled={true}
                        hasText={true}
                        transparent={true}
                        onClick={() => openLanguageMenu(anchorRef.current)}
                        css={css`
                            background-color: transparent;
                            color: inherit;
                            padding-inline: 8px;
                            text-transform: none;
                            border: hidden;
                            font-size: 12px;
                        `}
                    >
                        <span
                            css={css`
                                display: inline-flex;
                                align-items: center;
                                gap: 6px;
                            `}
                        >
                            <span>{state.uiLanguageLabel}</span>
                            <ArrowDropDown />
                        </span>
                    </BloomButton>
                </BloomTooltip>

                <BloomTooltip tip={{ l10nKey: "HelpMenu.Help Menu" }}>
                    <BloomButton
                        l10nKey="HelpMenu.HelpButton"
                        enabled={true}
                        transparent={true}
                        onClick={() => openHelpMenu(anchorRef.current)}
                        hasText={false}
                        css={css`
                            background-color: transparent;
                            color: inherit;
                            border: hidden;
                            min-width: 36px;
                            padding: 6px;
                        `}
                    >
                        <HelpOutline />
                    </BloomButton>
                </BloomTooltip>

                <div
                    css={css`
                        display: flex;
                        align-items: center;
                        gap: 4px;
                        background-color: transparent;
                        padding: 4px 6px;
                        border-radius: 4px;
                        color: ${state.zoomEnabled
                            ? "white"
                            : "rgba(255,255,255,0.6)"};
                    `}
                >
                    <BloomButton
                        l10nKey="Workspace.Zoom.Decrease"
                        enabled={state.zoomEnabled}
                        transparent={true}
                        hasText={true}
                        onClick={() => changeZoom(-10)}
                        css={css`
                            color: inherit;
                            min-width: 28px;
                            padding: 4px;
                            border: hidden;
                            background-color: transparent;
                        `}
                    >
                        -
                    </BloomButton>
                    <input
                        aria-label="Zoom value"
                        value={zoomField}
                        readOnly={true}
                        disabled={!state.zoomEnabled}
                        css={css`
                            width: 56px;
                            text-align: center;
                            border: none;
                            background: transparent;
                            color: inherit;
                            outline: none;
                            font-size: 12px;
                        `}
                    />
                    <BloomButton
                        l10nKey="Workspace.Zoom.Increase"
                        enabled={state.zoomEnabled}
                        transparent={true}
                        hasText={true}
                        onClick={() => changeZoom(10)}
                        css={css`
                            color: inherit;
                            min-width: 28px;
                            padding: 4px;
                            border: hidden;
                            background-color: transparent;
                        `}
                    >
                        +
                    </BloomButton>
                </div>

                <Menu
                    anchorEl={languageAnchor}
                    open={Boolean(languageAnchor)}
                    onClose={() => setLanguageAnchor(null)}
                >
                    {renderLanguageMenuItems()}
                </Menu>

                <Menu
                    anchorEl={helpAnchor}
                    open={Boolean(helpAnchor)}
                    onClose={() => setHelpAnchor(null)}
                >
                    {renderHelpMenuItems()}
                </Menu>
            </div>
        </ThemeProvider>
    );
};

WireUpForWinforms(WorkspaceTopRightControls);
