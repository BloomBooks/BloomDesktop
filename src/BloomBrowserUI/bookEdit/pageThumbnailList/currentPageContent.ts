import { getEditablePageBundleExports } from "../js/workspaceFrames";

// Collect the content of the page the user is currently editing, to send along with a request
// that will make C# save it.
//
// C# has to save the current page before it can change pages, duplicate one, delete one, and so
// on. Sending the content with the request lets it do all of that in one step. Otherwise it has
// to ask the browser for the content and wait for the answer to arrive on a separate API, and
// while it waits it is in a state where a further request of the same kind is silently thrown
// away. (See EditingModel.SavePageInPlaceThen.)
//
// This is async because it must NOT read the page while asynchronous work whose results belong in
// the saved page is still running -- image sizing, canvas-element fitting, a clipboard paste. That
// is what the delay register in bloomEditing.ts tracks, and awaiting getPageContentForSaveWhenReady
// is how we stay behind it. The gathering itself is still cheap (well under a millisecond: it works
// on a clone and does no layout), and the wait is normally zero, and capped either way.
//
// Because the whole command waits on this, the command cannot start mid-change either: C# is not
// asked to duplicate, delete or reorder anything until the page has settled.
//
// If we cannot collect it we return undefined and leave it out of the request; C# then falls back
// to asking. That is the honest thing to do for the cases where there is nothing to collect (no
// page loaded yet) or where the page is in a state we should not be reading (mid-navigation),
// rather than sending something half-formed: this content is about to be written into the user's
// book.
export async function collectCurrentPageContent(
    whatFor: string,
): Promise<string | undefined> {
    try {
        return await getEditablePageBundleExports()?.getPageContentForSaveWhenReady();
    } catch (error) {
        console.warn(
            `could not collect the current page's content for ${whatFor}; C# will ask the page frame for it instead.`,
            error,
        );
        return undefined;
    }
}
