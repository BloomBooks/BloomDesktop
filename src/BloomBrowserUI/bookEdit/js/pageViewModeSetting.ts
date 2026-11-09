// Which page view mode the user chose in the Edit tab (One Page or All Pages), and the zoom the user
// last had in each mode, kept across Bloom sessions.
//
// This module's state lives in the root window. Code in other frames (the page frame, the page
// list) must reach these functions through getWorkspaceBundleExports(), never by importing this
// module: an import would give that frame its own copy of the setting, which Bloom never fills in.

import { postString } from "../../utils/bloomApi";

export type PageViewMode = "one" | "all";

// The page view mode the user chose, and the zoom the user last had in each mode (a mode the user
// has never left has none). Bloom keeps this as a user setting, as the JSON of this object: it hands
// it over with editView/frameSources, before the first page loads (see EditTabPane.tsx), and
// savePageViewMode() gives it back after every change.
interface IPageViewModeSetting {
    mode: PageViewMode;
    zoomOfMode: { [mode in PageViewMode]?: number };
}
let pageViewModeSetting: IPageViewModeSetting = {
    mode: "one",
    zoomOfMode: {},
};

// Told when the page view mode setting arrives from Bloom, which can be after they first read it.
const pageViewModeSettingListeners = new Set<() => void>();

/** Take the page view mode setting Bloom kept, as editView/frameSources hands it over. */
export function takePageViewModeSettingFromBloom(setting: string): void {
    // Empty until the user first chooses a mode.
    pageViewModeSetting = setting
        ? (JSON.parse(setting) as IPageViewModeSetting)
        : { mode: "one", zoomOfMode: {} };
    pageViewModeSettingListeners.forEach((listener) => listener());
}

/**
 * Call `listener` whenever the page view mode setting arrives from Bloom. Returns the function that
 * stops that.
 */
export function listenForPageViewModeSetting(listener: () => void): () => void {
    pageViewModeSettingListeners.add(listener);
    return () => {
        pageViewModeSettingListeners.delete(listener);
    };
}

// Told each time a page has been shown with the other pages of the book around it.
const allPagesShownListeners = new Set<() => void>();

/** The page frame calls this once a page is showing with the other pages of the book around it. */
export function noteAllPagesShown(): void {
    allPagesShownListeners.forEach((listener) => listener());
}

/**
 * Call `listener` each time a page has been shown with the other pages of the book around it.
 * Returns the function that stops that.
 */
export function listenForAllPagesShown(listener: () => void): () => void {
    allPagesShownListeners.add(listener);
    return () => {
        allPagesShownListeners.delete(listener);
    };
}

// Send the whole setting to Bloom to keep.
function savePageViewMode(): void {
    postString("editView/pageViewMode", JSON.stringify(pageViewModeSetting));
}

/**
 * Whether the user wants to see the other pages of the book around the page being edited (see
 * bookGridView.ts): the All Pages mode. Remembered for every page, and across Bloom sessions.
 */
export function isShowingOtherPages(): boolean {
    return pageViewModeSetting.mode === "all";
}

/** Remember whether the user wants to see the other pages of the book. */
export function storeShowingOtherPages(show: boolean): void {
    pageViewModeSetting.mode = show ? "all" : "one";
    savePageViewMode();
}

/** The zoom the user last had in this page view mode, or undefined if they have never left it. */
export function getStoredZoomOfMode(mode: PageViewMode): number | undefined {
    return pageViewModeSetting.zoomOfMode[mode];
}

/** Remember the zoom the user had in this page view mode, for when they come back to it. */
export function storeZoomOfMode(mode: PageViewMode, zoom: number): void {
    pageViewModeSetting.zoomOfMode[mode] = zoom;
    savePageViewMode();
}
