// The user's choice between editing one page by itself and seeing all the pages of the book around
// it (see bookGridView.ts). The chooser is in the shell's top bar and the pages are drawn in the page
// frame; both are served from the same origin, so they share this localStorage entry. It is
// remembered for every page, and across Bloom sessions.
//
// This module must stay free of imports, because both the shell and the page frame bundle it.

const kShowOtherPagesKey = "bloom-edit-showOtherPages";

/** Whether the user wants to see the other pages of the book around the page being edited. */
export function isShowingOtherPages(): boolean {
    try {
        return localStorage.getItem(kShowOtherPagesKey) === "true";
    } catch {
        return false;
    }
}

/** Remember whether the user wants to see the other pages of the book. */
export function storeShowingOtherPages(show: boolean): void {
    try {
        localStorage.setItem(kShowOtherPagesKey, show ? "true" : "false");
    } catch {
        // Without storage the choice lasts only until the next page is loaded.
    }
}
