import { css } from "@emotion/react";
import * as React from "react";
import { DialogMiddle } from "./BloomDialog/BloomDialog";
import { kBloomBlue, kDialogPadding } from "../bloomMaterialUITheme";
import { BloomSubscriptionIndicatorIconAndText } from "./requiresSubscription";

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

// The text colors of Bloom's settings forms: near-black for headings and labels, grey for the
// descriptions under them.
const kConfigrTextColor = "#1f1f1f";
const kConfigrDescriptionColor = "#5f6368";

// Pass as the ConfigrPane's themeOverrides. Config-R merges this over its own theme, so these
// typography roles set the type scale of every settings form: h2 is the page heading, h3 a group
// heading, h4 a setting's label, and caption the description under a group or a setting.
export const kConfigrThemeOverrides = {
    // enhance: we'd like to just be passing `lightTheme` but at the moment that seems to clobber everything
    palette: {
        primary: { main: kBloomBlue },
        // Behind each search match.
        searchMatch: "#feeb5a",
    },
    typography: {
        h2: { fontSize: "20px", fontWeight: 600, color: kConfigrTextColor },
        h3: { fontSize: "16px", fontWeight: 600, color: kConfigrTextColor },
        h4: {
            fontSize: "14px",
            fontWeight: 500,
            color: kConfigrTextColor,
            lineHeight: 1.25,
        },
        caption: {
            fontSize: "12px",
            fontWeight: 400,
            color: kConfigrDescriptionColor,
            lineHeight: 1.5,
        },
    },
    components: {
        // The list of pages on the left.
        MuiTab: {
            styleOverrides: {
                root: {
                    fontSize: "14px",
                    fontWeight: 400,
                    "&.Mui-selected": { fontWeight: 600 },
                },
            },
        },
        // The value shown in a dropdown, the same size as the label beside it.
        MuiSelect: {
            styleOverrides: {
                select: { fontSize: "14px" },
            },
        },
        // Buttons inside the form, such as Change… and Remove.
        MuiButton: {
            styleOverrides: {
                root: { fontSize: "12px", fontWeight: 500 },
            },
        },
    },
};

/**
 * A line under a Config-R setting, showing the subscription badge for the feature that setting
 * needs. It ends at the same right edge as the control above it. Put it and the setting in one
 * element, which keeps Config-R from drawing a divider between them.
 */
export const ConfigrSubscriptionBadge: React.FunctionComponent<{
    // The feature name features/status knows it by, e.g. "TeamCollection".
    feature: string;
    // True when the control above is a checkbox. Config-R's rows inset their controls by 16px, but
    // a checkbox's drawn box ends 12px further in (MUI's 9px padding around it and the 3px inside
    // its icon).
    alignWithCheckbox?: boolean;
}> = (props) => (
    <div
        css={css`
            display: flex;
            padding-bottom: 5px;
            padding-right: ${props.alignWithCheckbox ? 28 : 16}px;
            font-size: 12px;
            font-weight: bold;
        `}
    >
        <BloomSubscriptionIndicatorIconAndText
            feature={props.feature}
            css={css`
                margin-left: auto;
            `}
        />
    </div>
);

/**
 * Puts Config-R's search box in a settings dialog's title bar. Put `searchBox` in the
 * DialogTitle's children and pass `searchBoxContainer` as the ConfigrPane's searchBoxContainer,
 * with searchBoxSize="small".
 */
export function useConfigrSearchBox(): {
    searchBox: React.ReactElement;
    searchBoxContainer: HTMLDivElement | null;
} {
    // State, not a ref, so the pane renders again once the element exists.
    const [searchBoxContainer, setSearchBoxContainer] =
        React.useState<HTMLDivElement | null>(null);
    return {
        searchBox: (
            <ConfigrSearchBoxContainer containerRef={setSearchBoxContainer} />
        ),
        searchBoxContainer,
    };
}

// The place in the title bar where Config-R shows its search box.
const ConfigrSearchBoxContainer: React.FunctionComponent<{
    containerRef: React.RefCallback<HTMLDivElement>;
}> = (props) => (
    <div
        ref={props.containerRef}
        css={css`
            // Grow to take the free space, so the box sits against the close
            // button rather than splitting the space with it.
            flex-grow: 1;
            margin-right: ${kDialogPadding};
            display: flex;
            justify-content: flex-end;
            align-items: center;
        `}
    />
);

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
