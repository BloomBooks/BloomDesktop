export enum GameType {
    Unknown,
    DragLetterToTarget,
    DragSortSentence,
    DragImageToTarget,
    ChooseImageFromWord,
    ChooseWordFromImage,
    CheckboxQuiz
}

export function getGameType(activityType: string, page: HTMLElement): GameType {
    switch (activityType) {
        case "drag-letter-to-target":
            return GameType.DragLetterToTarget;
        case "drag-sort-sentence":
            return GameType.DragSortSentence;
        case "drag-image-to-target":
            return GameType.DragImageToTarget;
        case "simple-dom-choice":
            if (page?.getElementsByClassName("wordThenChoices")?.length > 0) {
                return GameType.ChooseImageFromWord;
            }
            return GameType.ChooseWordFromImage;
        case "simple-checkbox-quiz":
            return GameType.CheckboxQuiz;
        default:
            return GameType.Unknown;
    }
}

export function isPageBloomGame(page: HTMLElement): boolean {
    const activityType = page.getAttribute("data-activity") ?? "";
    return activityTypesForGames.indexOf(activityType) >= 0;
}

export function isInDragActivity(element: HTMLElement): boolean {
    const page = element.closest(".bloom-page");
    return page?.getAttribute("data-activity")?.startsWith("drag-") ?? false;
}

const activityTypesForGames = [
    "drag-sort-sentence",
    "drag-letter-to-target",
    "drag-image-to-target",

    "simple-dom-choice",
    "simple-checkbox-quiz",

    // these two are not currently enabled
    "drag-word-chooser-slider",
    "drag-to-destination"
];
