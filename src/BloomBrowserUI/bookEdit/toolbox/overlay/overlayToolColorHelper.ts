import tinycolor = require("tinycolor2");
import { ISwatchDefn } from "../../../react_components/colorSwatch";
import {
    OverlayTextColorPalette,
    OverlayBackgroundColorSwatches,
    getSwatchFromBubbleSpecColor
} from "../../../react_components/colorPickerDialog";

export const defaultTextColors: ISwatchDefn[] = OverlayTextColorPalette.map(
    color => getSwatchFromBubbleSpecColor(color)
);
export const defaultBackgroundColors = OverlayBackgroundColorSwatches;

export const getRgbaColorStringFromColorAndOpacity = (
    color: string,
    opacity: number
): string => {
    const rgbColor = tinycolor(color).toRgb();
    rgbColor.a = opacity;
    return tinycolor(rgbColor).toRgbString(); // actually format is "rgba(r, g, b, a)"
};
