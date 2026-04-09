import { IColorInfo } from "../../../react_components/color-picking/colorSwatch";
import { getRgbaColorStringFromColorAndOpacity } from "../../../utils/colorUtils";

export function getPersistedCanvasColor(colorInfo: IColorInfo): string {
    const firstColor = colorInfo.colors[0];
    if (colorInfo.opacity >= 1) {
        return firstColor;
    }

    return getRgbaColorStringFromColorAndOpacity(firstColor, colorInfo.opacity);
}
