// This file is for any constants or functions that are common to all React tabs
// of the CollectionSettingsDialog.

import { css } from "@emotion/react";

export const tabMargins = { side: "26px", top: "7px", bottom: "0" };

// Colors for the Book Making tab's font/keyboard/page-number panel, from the
// "Settings Panel" design (design project "Spacing and layout improvements",
// option 1A "Refined rhythm").
const kPanelBackground = "#f4f5f6";
const kPanelBorder = "#dfe2e6";
const kSelectBorder = "#c8ccd1";

// The light rounded panel that groups the font, keyboard, and page-numbering
// controls on the left of the Book Making tab.
export const bookMakingPanelCss = css`
    background: ${kPanelBackground};
    border: 1px solid ${kPanelBorder};
    border-radius: 10px;
    padding: 16px 22px 18px;
    box-sizing: border-box;
`;

// Thin horizontal rule between language blocks (and before Page Numbering)
// inside the panel. flex-shrink: 0 stops a flex column from squeezing it below
// 1px. We use the (darker) control-border color rather than the panel-border
// color: at HiDPI a 1px line whose position lands between device pixels gets
// antialiased across two rows at ~half strength, and against the panel
// background the very-low-contrast panel-border color then nearly vanishes, so
// dividers looked unequal. The higher-contrast color keeps them consistent.
export const bookMakingDividerCss = css`
    flex-shrink: 0;
    height: 1px;
    background: ${kSelectBorder};
    margin: 16px 0;
`;

// Shared sizing/appearance for the select ("combo box") controls in the panel
// (font, keyboard, page numbering) so they line up: same width, same height, and
// the softer rounded look from the design. Without the width the plain-text
// keyboard/page-number boxes size narrower than the font boxes; the min-height
// makes the font select (which shows a suitability icon) match the plain ones.
export const kBookMakingSelectWidth = "220px";
export const bookMakingSelectCss = css`
    width: ${kBookMakingSelectWidth} !important;
    max-width: ${kBookMakingSelectWidth} !important;
    & > div {
        border-radius: 6px !important;
    }
    fieldset {
        border-color: ${kSelectBorder} !important;
    }
    .MuiSelect-select {
        box-sizing: border-box;
        min-height: 1.9em;
        display: flex;
        align-items: center;
    }
`;
