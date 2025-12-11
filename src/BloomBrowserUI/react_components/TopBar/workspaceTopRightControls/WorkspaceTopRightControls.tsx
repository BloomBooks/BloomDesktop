import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { get, postJson } from "../../../utils/bloomApi";
import WebSocketManager from "../../../utils/WebSocketManager";
import { lightTheme } from "../../../bloomMaterialUITheme";
import { createTheme, ThemeProvider } from "@mui/material/styles";
import { WireUpForWinforms } from "../../../utils/WireUpWinform";
import { CssBaseline } from "@mui/material";
import { ZoomControl } from "./ZoomControl";
import { UiLanguageMenu } from "./UiLanguageMenu";
import { HelpMenu } from "./HelpMenu";

interface TopRightState {
    uiLanguageLabel: string;
    zoom: number;
    zoomEnabled: boolean;
    minZoom: number;
    maxZoom: number;
}

interface TopRightProps {
    initialState?: TopRightState;
}

export const WorkspaceTopRightControls: React.FunctionComponent<
    TopRightProps
> = (props) => {
    const [state, setState] = useState<TopRightState | undefined>(undefined);
    const anchorRef = React.useRef<HTMLDivElement | null>(null);
    const lightThemeOverride = React.useMemo(
        () =>
            createTheme(lightTheme, {
                components: {
                    // Use default text color (almost black) -- rather than Bloom blue
                    MuiButton: {
                        styleOverrides: {
                            root: {
                                color: "inherit",
                            },
                            text: {
                                color: "inherit",
                            },
                        },
                    },
                },
            }),
        [],
    );

    const refreshState = React.useCallback(() => {
        if (props.initialState) {
            setState(props.initialState);
            return;
        }
        get("workspace/topRight/state", (result) => {
            setState(result.data as TopRightState);
        });
    }, [props.initialState]);

    // Refresh initial state from either props or the API when the component mounts.
    useEffect(() => {
        refreshState();
    }, [refreshState]);

    // Listen for websocket pushes so the UI stays in sync with backend state changes.
    useEffect(() => {
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
    }, []);

    const changeZoom = (newZoom: number) => {
        if (!state) {
            return;
        }
        setState({ ...state, zoom: newZoom });
        postJson("workspace/topRight/zoom", { zoom: newZoom });
    };

    if (!state) {
        return null;
    }

    return (
        <ThemeProvider theme={lightThemeOverride}>
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
                <UiLanguageMenu text={state.uiLanguageLabel} />
                <HelpMenu />
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
    );
};

WireUpForWinforms(WorkspaceTopRightControls);
