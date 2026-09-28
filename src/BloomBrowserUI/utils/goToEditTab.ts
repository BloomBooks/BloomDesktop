import { post, postJson } from "./bloomApi";

/**
 * The one way the front end takes the user to the Edit tab.
 *
 * Every route in — the Edit tab button, double-clicking a book, the "Edit this book" /
 * "Make a book from this source" button — ends here, so they all get the same two steps in the
 * same order: make the selected book ready, and only then select the tab.
 *
 * The first step matters because an old book may never have been through the per-page browser
 * fix-up, which loads every page off-screen and can take a while (BL-16852). Doing it before the
 * tab is showing means its progress dialog is not on the activation path. Putting it after was
 * what hung Bloom in BL-16877: the Edit tab came up with no page selected, asked for its frame
 * sources, that threw, and the resulting problem dialog opened inside the progress dialog's
 * message pump where neither could be dismissed.
 *
 * For a book that is already up to date — the normal case — the first step is a quick no-op.
 */
export function goToEditTab(): void {
    post("app/ensureBookReady", () => {
        postJson("workspace/selectTab", { tab: "edit" });
    });
}
