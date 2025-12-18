import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { lightTheme } from "../../../bloomMaterialUITheme";
import { createTheme, ThemeProvider } from "@mui/material/styles";
import { CssBaseline } from "@mui/material";
import { ZoomControl } from "./ZoomControl";
import { UiLanguageMenu } from "./UiLanguageMenu";
import { HelpMenu } from "./HelpMenu";
import { kTextOnPurple } from "../../../bloomMaterialUITheme";
import { useSubscribeToWebSocketForStringMessage } from "../../../utils/WebSocketManager";

export const WorkspaceTopRightControls: React.FunctionComponent = () => {
    const lightThemeOverride = React.useMemo(
        () =>
            createTheme(lightTheme, {
                components: {
                    // kTextOnPurple: The background isn't always purple,
                    // but this matches what the original winforms control was doing.
                    // Without the override, we get Bloom blue.
                    MuiButton: {
                        styleOverrides: {
                            root: {
                                color: kTextOnPurple,
                                fontWeight: "normal",
                            },
                            text: {
                                color: kTextOnPurple,
                                fontWeight: "normal",
                            },
                        },
                    },
                },
            }),
        [],
    );

    // Forces a refresh. Currently used for localization changes.
    const [generation, setGeneration] = useState(0);
    useSubscribeToWebSocketForStringMessage("app", "uiLanguageChanged", () => {
        setGeneration((current) => current + 1);
    });

    return (
        <ThemeProvider theme={lightThemeOverride}>
            {/* CssBaseline injects MUI's base styles (it sets html/body to the theme typography,
                normalizes margins, etc.). Without it, the browser keeps default fonts and spacing,
                so our theme's font family/size and resets never reach this control. */}
            <CssBaseline />
            <div
                key={`workspace-top-right-controls-${generation}`}
                css={css`
                    display: flex;
                    flex-direction: column;
                    gap: 1px;
                    align-items: end;
                    font-size: 12px;

                    // kTextOnPurple: see comment above
                    color: ${kTextOnPurple};
                    button {
                        color: ${kTextOnPurple};
                    }
                `}
            >
                {/* This grid keeps the two menu buttons the same width which keeps the down arrows aligned horizontally */}
                <div
                    css={css`
                        display: grid;
                        grid-template-columns: 1fr;
                        grid-auto-rows: auto;
                        width: max-content;
                        row-gap: 1px;
                    `}
                >
                    <UiLanguageMenu />
                    <HelpMenu />
                </div>
                <ZoomControl />
            </div>
        </ThemeProvider>
    );
};
