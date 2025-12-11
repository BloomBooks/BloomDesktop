import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { get, postJson } from "../../../utils/bloomApi";
import WebSocketManager from "../../../utils/WebSocketManager";
import { lightTheme } from "../../../bloomMaterialUITheme";
import { ThemeProvider } from "@mui/material/styles";
import { WireUpForWinforms } from "../../../utils/WireUpWinform";
import { StyledEngineProvider, CssBaseline } from "@mui/material";
import { ZoomControl } from "./ZoomControl";
import { UiLanguageMenu } from "./UiLanguageMenu";
import { HelpMenu } from "./HelpMenu";

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
    const anchorRef = React.useRef<HTMLDivElement | null>(null);

    const refreshState = React.useCallback(() => {
        if (props.skipApi && props.initialState) {
            setState(props.initialState);
            return;
        }
        if (props.skipApi) {
            return;
        }
        get("workspace/topRight/state", (result) => {
            setState(result.data as TopRightState);
        });
    }, [props.initialState, props.skipApi]);

    // Refresh initial state from either props or the API when the component mounts.
    useEffect(() => {
        refreshState();
    }, [refreshState]);

    // Listen for websocket pushes so the UI stays in sync with backend state changes.
    useEffect(() => {
        if (props.skipApi) {
            return;
        }
        const listener = (e) => {
            if (e.id === "state") {
                const results = e as TopRightState;
                setState(results);
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

    const changeZoom = (newZoom: number) => {
        if (!state) {
            return;
        }
        setState({ ...state, zoom: newZoom });
        if (!props.skipApi) {
            postJson("workspace/topRight/zoom", { zoom: newZoom });
        }
    };

    if (!state) {
        return null;
    }

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                {/* CssBaseline injects MUI's base styles (it sets html/body to the theme typography,
                normalizes margins, etc.). Without it, the browser keeps default fonts and spacing,
                so our theme's font family/size and resets never reach this control. */}
                <CssBaseline />
                <div
                    ref={anchorRef}
                    css={css`
                        display: flex;
                        flex-direction: column;
                        align-items: end;
                    `}
                >
                    <UiLanguageMenu
                        state={state}
                        languages={languages}
                        anchorRef={anchorRef}
                        languageAnchor={languageAnchor}
                        onOpen={openLanguageMenu}
                        onClose={() => setLanguageAnchor(null)}
                        onApplyLanguage={applyLanguage}
                        onToggleShowUnapproved={toggleShowUnapproved}
                    />

                    <HelpMenu
                        helpItems={helpItems}
                        anchorRef={anchorRef}
                        helpAnchor={helpAnchor}
                        onOpen={openHelpMenu}
                        onClose={() => setHelpAnchor(null)}
                        onRunCommand={runHelpCommand}
                    />

                    {state.zoomEnabled && (
                        <ZoomControl
                            zoom={state.zoom}
                            minZoom={state.minZoom}
                            maxZoom={state.maxZoom}
                            onZoomChange={changeZoom}
                        />
                    )}
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

WireUpForWinforms(WorkspaceTopRightControls);
