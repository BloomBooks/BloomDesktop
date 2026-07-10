// This file is for any constants or functions that are common to all React tabs
// of the CollectionSettingsDialog.

import { css } from "@emotion/react";

export const tabMargins = { side: "26px", top: "7px", bottom: "0" };

// Vertical gap between each language's block (font + keyboard) in the Book Making
// tab, and between the last block and the Page Numbering Style below it.
export const kBookMakingSectionGap = "24px";

// Shared sizing for the select ("combo box") controls in the Book Making tab
// (font, keyboard, page numbering). Without this the boxes size to their content,
// so the plain-text keyboard/page-number boxes end up narrower than the font
// boxes. Pinning the width lines them up, and pinning a min-height on the inner
// display makes the font select (which shows a suitability icon) the same height
// as the plain-text ones.
export const kBookMakingSelectWidth = "220px";
export const bookMakingSelectCss = css`
    width: ${kBookMakingSelectWidth} !important;
    max-width: ${kBookMakingSelectWidth} !important;
    .MuiSelect-select {
        box-sizing: border-box;
        min-height: 1.9em;
        display: flex;
        align-items: center;
    }
`;
