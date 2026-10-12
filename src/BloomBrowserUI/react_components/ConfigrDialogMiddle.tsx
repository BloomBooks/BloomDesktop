import { css } from "@emotion/react";
import * as React from "react";
import { DialogMiddle } from "./BloomDialog/BloomDialog";
import { kBloomBlue } from "../bloomMaterialUITheme";

// The Book Settings and Collection Settings dialogs share this size, so they feel like one family.
const kConfigrDialogWidthPx = 900;
const kConfigrDialogHeightPx = 720;

// Put this in the BloomDialog's css to give it the settings dialogs' fixed size.
export const kConfigrDialogSizeCss = css`
    .MuiDialog-paper {
        width: ${kConfigrDialogWidthPx}px;
        height: ${kConfigrDialogHeightPx}px;
    }
`;

// Give the ConfigrPane this class so ConfigrDialogMiddle can make it fill the middle.
export const kConfigrPaneClassName = "configr-dialog-pane";

// Pass as the ConfigrPane's themeOverrides.
export const kConfigrThemeOverrides = {
    // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
    palette: {
        primary: { main: kBloomBlue },
    },
};

/**
 * The DialogMiddle of a settings dialog whose content is a Config-R form: only the form scrolls,
 * so the title and the button row stay put. The caller supplies the ConfigrPane (with
 * kConfigrPaneClassName and kConfigrThemeOverrides) as children.
 */
export const ConfigrDialogMiddle: React.FunctionComponent<{
    children?: React.ReactNode;
}> = (props) => (
    <DialogMiddle
        css={css`
            &:first-child {
                margin-top: 0; // override the default that sees a lack of a title and adds a margin
            }
            overflow-y: hidden;
            min-height: 0;

            .${kConfigrPaneClassName} {
                height: 100%;
                min-height: 0;
            }

            // Let config-r consume the available dialog height in both the page form and
            // the area-description states so the button row stays pinned to the bottom.
            form {
                overflow-y: auto;
                height: 100%;
                min-height: 0;
                width: 100%;
                box-sizing: border-box;
                #groups {
                    margin-right: 10px; // make room for the scrollbar
                }
            }

            a {
                color: ${kBloomBlue};
            }

            // In a disabled config-r checkbox row, the label and checkbox colors are already
            // faded; MUI also fades the whole row (opacity 0.38), so it was applied twice and the
            // row was barely visible. Limited to checkbox rows because those are the ones known
            // to fade their own contents. The extra class outranks MUI's own rule, which
            // otherwise wins whenever its stylesheet happens to load after ours.
            .MuiListItemButton-root.Mui-disabled:has(.MuiCheckbox-root) {
                opacity: 1;
                // Links in the description don't fade their own color, so fade them here as
                // MUI's row opacity used to.
                a {
                    opacity: 0.38;
                }
            }
        `}
    >
        {props.children}
    </DialogMiddle>
);
