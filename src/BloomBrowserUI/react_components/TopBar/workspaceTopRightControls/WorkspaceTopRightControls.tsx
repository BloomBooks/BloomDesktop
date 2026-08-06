import { css } from "@emotion/react";
import * as React from "react";
import { lightTheme } from "../../../bloomMaterialUITheme";
import { createTheme, ThemeProvider } from "@mui/material/styles";
import { ZoomControl } from "./ZoomControl";
import { UiLanguageMenu } from "./UiLanguageMenu";
import { HelpMenu } from "./HelpMenu";
import { AccountMenu } from "./AccountMenu";

// Every affordance in this group -- text, menu-button labels, the help icon, and all
// the dropdown arrows -- is drawn in black at 80% opacity. (The avatar image is not
// affected by color.) The tab-bar background these sit on isn't always the same color,
// and black-at-80% reads well across those backgrounds.
const kTopRightControlColor = "rgba(0, 0, 0, 0.8)";

export const WorkspaceTopRightControls: React.FunctionComponent = () => {
    const lightThemeOverride = React.useMemo(
        () =>
            createTheme(lightTheme, {
                components: {
                    // Without this override, MUI buttons in this group would be Bloom blue.
                    MuiButton: {
                        styleOverrides: {
                            root: {
                                color: kTopRightControlColor,
                                fontWeight: "normal",
                            },
                            text: {
                                color: kTopRightControlColor,
                                fontWeight: "normal",
                            },
                        },
                    },
                },
            }),
        [],
    );

    return (
        <ThemeProvider theme={lightThemeOverride}>
            <div
                css={css`
                    display: flex;
                    flex-direction: row;
                    align-items: flex-start;
                    gap: 12px;
                    font-size: 12px;

                    // See comment on kTopRightControlColor above.
                    color: ${kTopRightControlColor};
                    button {
                        color: ${kTopRightControlColor};
                    }
                    // Make SVG icons (help icon, dropdown arrows) match the text color.
                    svg {
                        fill: currentColor;
                    }
                `}
            >
                {/* The language chooser, help menu, and zoom control sit together on the
                    left so that the account menu can stand by itself on the right. */}
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        gap: 1px;
                        align-items: end;
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
                {/* Top-align the account menu with the language/help/zoom stack. Because
                    the avatar is shorter than that stack, it never drives this group's
                    height, and so it cannot affect the position or size of anything to
                    its left. */}
                <div
                    css={css`
                        align-self: flex-start;
                    `}
                >
                    <AccountMenu />
                </div>
            </div>
        </ThemeProvider>
    );
};
