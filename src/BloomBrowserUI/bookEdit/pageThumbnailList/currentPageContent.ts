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
// Collecting it is cheap -- well under a millisecond, since getPageContentForSave() works on a
// clone and does no layout -- so there is no reason not to do it on every such request.
//
// If we cannot collect it we return undefined and leave it out of the request; C# then falls back
// to asking. That is the honest thing to do for the cases where there is nothing to collect (no
// page loaded yet) or where the page is in a state we should not be reading (mid-navigation),
// rather than sending something half-formed: this content is about to be written into the user's
// book.
export function collectCurrentPageContent(whatFor: string): string | undefined {
    try {
        return getEditablePageBundleExports()?.getPageContentForSave();
    } catch (error) {
        console.warn(
            `could not collect the current page's content for ${whatFor}; C# will ask the page frame for it instead.`,
            error,
        );
        return undefined;
    }
}
