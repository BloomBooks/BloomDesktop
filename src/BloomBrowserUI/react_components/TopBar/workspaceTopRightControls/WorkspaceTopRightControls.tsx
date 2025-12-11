import { css } from "@emotion/react";
import * as React from "react";
import { lightTheme } from "../../../bloomMaterialUITheme";
import { createTheme, ThemeProvider } from "@mui/material/styles";
import { WireUpForWinforms } from "../../../utils/WireUpWinform";
import { CssBaseline } from "@mui/material";
import { ZoomControl } from "./ZoomControl";
import { UiLanguageMenu } from "./UiLanguageMenu";
import { HelpMenu } from "./HelpMenu";

export const WorkspaceTopRightControls: React.FunctionComponent = () => {
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
                <UiLanguageMenu />
                <HelpMenu />
                <ZoomControl />
            </div>
        </ThemeProvider>
    );
};

WireUpForWinforms(WorkspaceTopRightControls);
