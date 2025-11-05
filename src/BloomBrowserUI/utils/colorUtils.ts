import tinycolor from "tinycolor2";

// Corresponds to the colors defined in bloomUI.less
// These can be useful for CSS-in-JS, where it's hard to get at the color definitions in the .less files
export const kBloomBlue = "#1d94a4"; // See @bloom-blue
export const kBloomLightBlue = "#b0dee4"; // See @bloom-lightblue
export const kBloomYellow = "#FEBF00"; // See @bloom-yellow

export const kBloomLightGray = "lightgray"; // See @bloom-lightgray
export const kBloomRed = "#d65649"; // See @bloom-red
export const kBloomGray = "#575757"; // See @bloom-gray
export const kBloomDisabledOpacity = 0.38;
export const kBloomDisabledText = `rgba(0, 0, 0, ${kBloomDisabledOpacity})`;
export const kBloomUnselectedTabBackground = "#404040"; // @See bloom-unselectedTabBackground
export const kBloomPanelBackground = "#2e2e2e"; // See @bloom-panelBackground
export const kBloomDarkestBackground = "#1a1a1a"; // See @bloom-darkestBackground
export const kBloomBuff = "#d2d2d2"; // See @bloom-buff
export const kBloomToolboxWhite = "#ffffff88"; //See @bloom-toolboxWhite
export const kBloomPurple = "#96668f"; // See @bloom-purple
export const kBloomWarning = "#FEBF00"; // darker looked bad on Export to Spreadsheet. See https://issues.bloomlibrary.org/youtrack/issue/BL-10769
export const kBloomDarkTextOverWarning = "##000000cc"; // black with 20% transparency

export const kFormBackground = "#f0f0f0"; // See @form-background;

export const getRgbaColorStringFromColorAndOpacity = (
    color: string,
    opacity: number,
): string => {
    const rgbColor = tinycolor(color).toRgb();
    rgbColor.a = opacity;
    return tinycolor(rgbColor).toRgbString(); // actually format is "rgba(r, g, b, a)"
};
