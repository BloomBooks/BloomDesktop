import { css } from "@emotion/react";
import { kBloomGold } from "../../bloomMaterialUITheme";

// Common background color for both book and page choosers
export const chooserBackgroundColor = "transparent";

// Common item background color (dark gray for cards/thumbnails)
export const itemBackgroundColor = "#505050";

// Common selection styling
export const getSelectionOutline = (isSelected: boolean) =>
    isSelected ? `3px solid ${kBloomGold}` : "none";

// Common container styles for book and page chooser areas
export const chooserContainerStyles = css`
    overflow-y: scroll;
    background-color: ${chooserBackgroundColor};
    padding: 10px;
`;

// Common gap between items
export const itemGap = "8px";

// Common styles for clickable items
export const clickableItemStyles = css`
    cursor: pointer;
    background-color: ${itemBackgroundColor};
`;

// Common padding to separate chooser content from dialog buttons
export const chooserButtonPadding = "10px";

// Common heading style for section headings
export const headingStyle = css`
    font-weight: 600;
    font-size: 14px;
    margin-block-end: 0 !important; // we don't want a big gap below the heading and the box below it
`;
