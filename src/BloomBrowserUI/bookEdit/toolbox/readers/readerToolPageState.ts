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

/**
 * Put the level, the stage, and the number the xmatter shows onto the page body, where a
 * stylesheet can see them. A branding such as ABC-Reader draws its grade circle from
 * data-leveledreaderlevel, so the page must carry what the user has just chosen, not what the
 * book had when the page loaded. The book itself gets the same values from the server
 * (Book.SetIsLeveled, Book.AddReaderBodyAttributes and BookData).
 *
 * Take both numbers, because a book can be a leveled reader and a decodable reader at once, and
 * turning one of them off then has to fall back to the other. A kind the book is not records 0.
 * (BL-16775)
 */
export function updateReaderNumbersOnCurrentPage(
    levelNumber: number,
    stageNumber: number,
): void {
    const page = getPageIframeBody();
    if (!page) return;
    const leveledOn = isReaderToolEnabledOnCurrentPage(true);
    const decodableOn = isReaderToolEnabledOnCurrentPage(false);
    page.setAttribute(
        "data-leveledreaderlevel",
        (leveledOn ? levelNumber : 0).toString(),
    );
    page.setAttribute(
        "data-decodablestage",
        (decodableOn ? stageNumber : 0).toString(),
    );

    // The xmatter shows the number through this field. Keep the page in step, so the user sees
    // the change without having to leave the page and come back. The stage wins over the level
    // when the book is both, which is the order BookData.UpdateToolRelatedDataFromBookInfo
    // applies on the server.
    let text = "";
    if (leveledOn) text = levelNumber.toString();
    if (decodableOn) text = stageNumber.toString();
    page.querySelectorAll('[data-book="levelOrStageNumber"]').forEach((e) => {
        e.textContent = text;
    });
}
