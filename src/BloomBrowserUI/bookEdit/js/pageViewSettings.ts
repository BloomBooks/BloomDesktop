// Which page view mode the user chose in the Edit tab (One Page or All Pages), and the zoom the user
// last had in each mode, kept across Bloom sessions.
//
// This module's state lives in the root window. Code in other frames (the page frame, the page
// list) must reach these functions through getWorkspaceBundleExports(), never by importing this
// module: an import would give that frame its own copy of the setting, which Bloom never fills in.

import { postString } from "../../utils/bloomApi";

export type PageViewMode = "one" | "all";

// The page view mode the user chose, and the zoom the user last had in each mode (a mode the user
// has never left has none). Bloom keeps all of this in one user setting, a string holding the JSON
// of this object, which Bloom never looks inside: it hands the string over with
// editView/frameSources, before the first page loads (see EditTabPane.tsx), and
// savePageViewSettings() gives it back after every change.
interface IPageViewSettings {
    mode: PageViewMode;
    zoomOfMode: { [mode in PageViewMode]?: number };
}
let pageViewSettings: IPageViewSettings = {
    mode: "one",
    zoomOfMode: {},
};

// Told when the page view settings arrive from Bloom, which can be after they first read it.
const pageViewSettingsListeners = new Set<() => void>();

/** Take the page view settings that Bloom kept, as editView/frameSources hands them over. */
export function takePageViewSettingsFromBloom(setting: string): void {
    // Empty until the user first chooses a mode.
    pageViewSettings = setting
        ? (JSON.parse(setting) as IPageViewSettings)
        : { mode: "one", zoomOfMode: {} };
    pageViewSettingsListeners.forEach((listener) => listener());
}

/**
 * Call `listener` whenever the page view settings arrive from Bloom. Returns the function that
 * stops that.
 */
export function listenForPageViewSettings(listener: () => void): () => void {
    pageViewSettingsListeners.add(listener);
    return () => {
        pageViewSettingsListeners.delete(listener);
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

// Send all the settings (the mode, and the zoom of each mode) to Bloom to keep.
function savePageViewSettings(): void {
    postString("editView/pageViewSettings", JSON.stringify(pageViewSettings));
}

/**
 * Whether the user wants to see the other pages of the book around the page being edited (see
 * bookGridView.ts): the All Pages mode. Remembered for every page, and across Bloom sessions.
 */
export function isShowingOtherPages(): boolean {
    return pageViewSettings.mode === "all";
}

/** Remember whether the user wants to see the other pages of the book. */
export function storeShowingOtherPages(show: boolean): void {
    pageViewSettings.mode = show ? "all" : "one";
    savePageViewSettings();
}

/** The zoom the user last had in this page view mode, or undefined if they have never left it. */
export function getStoredZoomOfMode(mode: PageViewMode): number | undefined {
    return pageViewSettings.zoomOfMode[mode];
}

/** Remember the zoom the user had in this page view mode, for when they come back to it. */
export function storeZoomOfMode(mode: PageViewMode, zoom: number): void {
    pageViewSettings.zoomOfMode[mode] = zoom;
    savePageViewSettings();
}
