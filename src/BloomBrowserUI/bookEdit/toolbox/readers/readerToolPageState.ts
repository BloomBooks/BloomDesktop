import { ToolBox } from "../toolbox";

export function isReaderToolEnabledOnCurrentPage(
    isForLeveled: boolean,
): boolean {
    const prefix = isForLeveled ? "leveled" : "decodable";
    return !!ToolBox.getPage()?.classList.contains(`${prefix}-reader`);
}

export function isReaderToolTurnedOff(isForLeveled: boolean): boolean {
    return !isReaderToolEnabledOnCurrentPage(isForLeveled);
}
