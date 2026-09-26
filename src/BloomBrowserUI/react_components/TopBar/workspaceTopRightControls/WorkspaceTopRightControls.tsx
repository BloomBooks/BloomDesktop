import { css } from "@emotion/react";
import * as React from "react";
import { lightTheme } from "../../../bloomMaterialUITheme";
import { createTheme, ThemeProvider } from "@mui/material/styles";
import { ZoomControl } from "./ZoomControl";
import { UiLanguageMenu } from "./UiLanguageMenu";
import { HelpMenu } from "./HelpMenu";
import { AccountMenu } from "./AccountMenu";
import { PageViewChooser } from "./PageViewChooser";
import { WorkspaceTabId } from "../TopBar";

// Every affordance in this group -- text, menu-button labels, the help icon, and all
// the dropdown arrows -- is drawn in black at 80% opacity. (The avatar image is not
// affected by color.) The tab-bar background these sit on isn't always the same color,
// and black-at-80% reads well across those backgrounds.
const kTopRightControlColor = "rgba(0, 0, 0, 0.8)";

export const WorkspaceTopRightControls: React.FunctionComponent<{
    activeTab: WorkspaceTabId;
}> = (props) => {
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
            {/* Two columns. The language chooser, help menu, and zoom control are stacked on the
                left, right-aligned. The account menu stands by itself on the right, top-aligned
                beside the first two rows; in the Edit tab the page view chooser sits under it, on
                the zoom control's row. The rows have fixed heights, so the avatar never drives
                this group's height and cannot affect the position or size of anything to its
                left. */}
            <div
                css={css`
                    display: grid;
                    grid-template-columns: auto auto;
                    grid-template-rows: 20px 20px 22px;
                    row-gap: 3px;
                    column-gap: 10px;
                    padding-top: 6px;
                    justify-items: end;
                    align-items: center;
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
                <div
                    css={css`
                        grid-column: 1;
                        grid-row: 1;
                        justify-self: stretch;
                        // Stretch the menu button too, so the language and help buttons are the
                        // same width and their down arrows line up.
                        display: grid;
                    `}
                >
                    <UiLanguageMenu />
                </div>
                <div
                    css={css`
                        grid-column: 1;
                        grid-row: 2;
                        justify-self: stretch;
                        // Stretch the menu button too, so the language and help buttons are the
                        // same width and their down arrows line up.
                        display: grid;
                    `}
                >
                    <HelpMenu />
                </div>
                <div
                    css={css`
                        grid-column: 1;
                        grid-row: 3;
                    `}
                >
                    <ZoomControl />
                </div>
                <div
                    css={css`
                        grid-column: 2;
                        grid-row: 1 / span 2;
                        align-self: start;
                    `}
                >
                    <AccountMenu />
                </div>
                {props.activeTab === "edit" && (
                    <div
                        css={css`
                            grid-column: 2;
                            grid-row: 3;
                            margin-right: 3px;
                        `}
                    >
                        <PageViewChooser />
                    </div>
                )}
            </div>
        </ThemeProvider>
    );
};
