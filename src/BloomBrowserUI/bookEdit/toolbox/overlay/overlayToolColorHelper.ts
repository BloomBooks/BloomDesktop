import tinycolor = require("tinycolor2");
import { IColorInfo } from "../../../react_components/colorSwatch";
import {
    OverlayTextColorPalette,
    OverlayBackgroundColors,
    getColorInfoFromSpecialNameOrColorString
} from "../../../react_components/colorPickerDialog";

export const defaultTextColors: IColorInfo[] = OverlayTextColorPalette.map(
    color => getColorInfoFromSpecialNameOrColorString(color)
);
export const defaultBackgroundColors = OverlayBackgroundColors;

export const getRgbaColorStringFromColorAndOpacity = (
    color: string,
    opacity: number
): string => {
    const rgbColor = tinycolor(color).toRgb();
    rgbColor.a = opacity;
    return tinycolor(rgbColor).toRgbString(); // actually format is "rgba(r, g, b, a)"
};
