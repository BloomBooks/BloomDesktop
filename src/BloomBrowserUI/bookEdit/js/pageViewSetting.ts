// Which page view the user chose in the Edit tab (One Page or All Pages), and the zoom the user last
// had in each view, kept across Bloom sessions.
//
// This module's state lives in the root window. Code in other frames (the page frame, the page
// list) must reach these functions through getWorkspaceBundleExports(), never by importing this
// module: an import would give that frame its own copy of the setting, which Bloom never fills in.

import { postString } from "../../utils/bloomApi";

export type PageView = "one" | "all";

// Which page view the user chose, and the zoom the user last had in each view (one the user has
// never left has none). Bloom keeps this as a user setting, as the JSON of this object: it hands it
// over with editView/frameSources, before the first page loads (see EditTabPane.tsx), and
// savePageView() gives it back after every change.
interface IPageViewSetting {
    showOtherPages: boolean;
    zoomOfView: { [view in PageView]?: number };
}
let pageViewSetting: IPageViewSetting = {
    showOtherPages: false,
    zoomOfView: {},
};

// Told when the page view setting arrives from Bloom, which can be after they first read it.
const pageViewSettingListeners = new Set<() => void>();

/** Take the page view setting Bloom kept, as editView/frameSources hands it over. */
export function takePageViewSettingFromBloom(setting: string): void {
    // Empty until the user first chooses a view.
    pageViewSetting = setting
        ? (JSON.parse(setting) as IPageViewSetting)
        : { showOtherPages: false, zoomOfView: {} };
    pageViewSettingListeners.forEach((listener) => listener());
}

/**
 * Call `listener` whenever the page view setting arrives from Bloom. Returns the function that
 * stops that.
 */
export function listenForPageViewSetting(listener: () => void): () => void {
    pageViewSettingListeners.add(listener);
    return () => {
        pageViewSettingListeners.delete(listener);
    };
}

// Send the whole setting to Bloom to keep.
function savePageView(): void {
    postString("editView/pageView", JSON.stringify(pageViewSetting));
}

/**
 * Whether the user wants to see the other pages of the book around the page being edited (see
 * bookGridView.ts). Remembered for every page, and across Bloom sessions.
 */
export function isShowingOtherPages(): boolean {
    return pageViewSetting.showOtherPages;
}

/** Remember whether the user wants to see the other pages of the book. */
export function storeShowingOtherPages(show: boolean): void {
    pageViewSetting.showOtherPages = show;
    savePageView();
}

/** The zoom the user last had in this page view, or undefined if they have never left it. */
export function getStoredZoomOfView(view: PageView): number | undefined {
    return pageViewSetting.zoomOfView[view];
}

/** Remember the zoom the user had in this page view, for when they come back to it. */
export function storeZoomOfView(view: PageView, zoom: number): void {
    pageViewSetting.zoomOfView[view] = zoom;
    savePageView();
}
