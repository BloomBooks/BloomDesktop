// Which page view mode the user chose in the Edit tab (One Page or All Pages), and the zoom the user
// had in each mode when they last left it, kept across Bloom sessions. The zoom in effect is not
// kept here: it is the Edit tab's zoom, which Bloom keeps as it always has (its PageZoom setting,
// read and set through workspace/zoom).
//
// This module's state lives in the root window. Code in other frames (the page frame, the page
// list) must reach these functions through getWorkspaceBundleExports(), never by importing this
// module: an import would give that frame its own copy of the setting, which Bloom never fills in.

import { postString } from "../../utils/bloomApi";

export type PageViewMode = "one" | "all";

// The page view mode the user chose, and in zoomOfMode the zoom the user had in each mode when they
// last left it (a mode the user has never left has none). Changing mode stores the zoom being left
// and makes the other mode's stored zoom Bloom's zoom (see switchToZoomOfMode() in
// PageViewChooser.tsx). The entry for the mode that is showing is only read after the user leaves
// that mode, which stores it afresh. Bloom keeps all of this in one user setting, a string holding the JSON
// of this object, which Bloom never looks inside: it hands the string over with
// editView/frameSources, before the first page loads (see EditTabPane.tsx), and
// savePageViewSettings() gives it back after every change.
interface IPageViewSettings {
    mode: PageViewMode;
    zoomOfMode: { [mode in PageViewMode]?: number };
}
// The settings before the user first chooses a mode. A fresh object each time, because the settings
// are changed in place.
function makeDefaultPageViewSettings(): IPageViewSettings {
    return { mode: "one", zoomOfMode: {} };
}
let pageViewSettings: IPageViewSettings = makeDefaultPageViewSettings();

// A set of listeners: `listen` adds one and returns the function that removes it again, and
// `notify` calls each of them.
function makeListeners(): {
    listen: (listener: () => void) => () => void;
    notify: () => void;
} {
    const listeners = new Set<() => void>();
    return {
        listen: (listener) => {
            listeners.add(listener);
            return () => {
                listeners.delete(listener);
            };
        },
        notify: () => listeners.forEach((listener) => listener()),
    };
}

// Told when the page view settings arrive from Bloom, which can be after they first read it.
const pageViewSettingsListeners = makeListeners();

/** Take the page view settings that Bloom kept, as editView/frameSources hands them over. */
export function takePageViewSettingsFromBloom(setting: string): void {
    // Empty until the user first chooses a mode.
    pageViewSettings = setting
        ? (JSON.parse(setting) as IPageViewSettings)
        : makeDefaultPageViewSettings();
    pageViewSettingsListeners.notify();
}

/**
 * Call `listener` whenever the page view settings arrive from Bloom. Returns the function that
 * stops that.
 */
export const listenForPageViewSettings = pageViewSettingsListeners.listen;

// Told each time a page has been shown with the other pages of the book around it.
const allPagesShownListeners = makeListeners();

/** The page frame calls this once a page is showing with the other pages of the book around it. */
export const noteAllPagesShown = allPagesShownListeners.notify;

/**
 * Call `listener` each time a page has been shown with the other pages of the book around it.
 * Returns the function that stops that.
 */
export const listenForAllPagesShown = allPagesShownListeners.listen;

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

/**
 * The zoom the user had when they last left this page view mode, or undefined if they have never
 * left it.
 */
export function getStoredZoomOfMode(mode: PageViewMode): number | undefined {
    return pageViewSettings.zoomOfMode[mode];
}

/** Remember the zoom the user had in the page view mode they are leaving, for when they come back. */
export function storeZoomOfMode(mode: PageViewMode, zoom: number): void {
    pageViewSettings.zoomOfMode[mode] = zoom;
    savePageViewSettings();
}
