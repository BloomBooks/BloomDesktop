import { ToolBox } from "../toolbox";

export function isReaderToolEnabledOnCurrentPage(
    isForLeveled: boolean,
): boolean {
    const prefix = isForLeveled ? "leveled" : "decodable";
    return !!ToolBox.getPage()?.classList.contains(`${prefix}-reader`);
}

export function setReaderToolEnabledOnCurrentPage(
    isForLeveled: boolean,
    enabled: boolean,
): void {
    const prefix = isForLeveled ? "leveled" : "decodable";
    const page = ToolBox.getPage();
    if (!page) return;

    page.classList.toggle(`${prefix}-reader`, enabled);
    page.classList.toggle(`${prefix}-reader-off`, !enabled);
}

export function isReaderToolTurnedOff(isForLeveled: boolean): boolean {
    return !isReaderToolEnabledOnCurrentPage(isForLeveled);
}
