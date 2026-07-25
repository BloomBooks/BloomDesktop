import { getPageIframeBody } from "../../../utils/shared";

export function isReaderToolEnabledOnCurrentPage(
    isForLeveled: boolean,
): boolean {
    const prefix = isForLeveled ? "leveled" : "decodable";
    return !!getPageIframeBody()?.classList.contains(`${prefix}-reader`);
}

export function isReaderToolTurnedOff(isForLeveled: boolean): boolean {
    return !isReaderToolEnabledOnCurrentPage(isForLeveled);
}
