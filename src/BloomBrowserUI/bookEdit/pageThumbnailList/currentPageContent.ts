import { getEditablePageBundleExports } from "../js/workspaceFrames";

// Collect the content of the page the user is currently editing, to send along with a request
// that will make C# save it.
//
// C# has to save the current page before it can change pages, duplicate one, delete one, and so
// on. Sending the content with the request lets it do that from the freshest possible copy: C#
// otherwise uses the last snapshot the browser volunteered, which can be up to the debounce
// interval old (see pageSnapshot.ts). Bloom used to have to ASK the browser and wait for the answer
// on a separate API, and that is what this replaced; there is no longer any such wait.
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
// If we cannot collect it we return undefined and leave it out of the request; C# then uses the
// snapshot the browser last volunteered. That is the honest thing to do for the cases where there
// is nothing to collect (no page loaded yet) or where the page is in a state we should not be
// reading (mid-navigation), rather than sending something half-formed: this content is about to be
// written into the user's book.
//
// The promise we await belongs to the PAGE frame. If that frame navigates while we are waiting,
// its timers and microtask queue go with it and the promise simply never settles -- and since the
// whole command is waiting on us, the command would be dropped without a trace, which is worse
// than doing it without the content. So we give up after a while and let the command go ahead on
// the snapshot C# already holds. The timer is ours, in this frame, precisely so that it survives
// the page frame going away.
const kGiveUpWaitingMs = 6000; // comfortably past the page frame's own 4s cap

export async function collectCurrentPageContent(
    whatFor: string,
): Promise<string | undefined> {
    try {
        const content =
            getEditablePageBundleExports()?.getPageContentForSaveWhenReady();
        if (!content) return undefined;
        let giveUp: number | undefined;
        const abandoned = new Promise<undefined>((resolve) => {
            giveUp = window.setTimeout(() => {
                console.warn(
                    `gave up waiting for the current page's content for ${whatFor} (the page frame ` +
                        `may have navigated away mid-wait); C# will ask the page frame for it instead.`,
                );
                resolve(undefined);
            }, kGiveUpWaitingMs);
        });
        try {
            return await Promise.race([content, abandoned]);
        } finally {
            if (giveUp !== undefined) window.clearTimeout(giveUp);
        }
    } catch (error) {
        console.warn(
            `could not collect the current page's content for ${whatFor}; C# will ask the page frame for it instead.`,
            error,
        );
        return undefined;
    }
}
