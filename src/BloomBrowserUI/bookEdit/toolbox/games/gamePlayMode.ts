import { prepareActivity, undoPrepareActivity } from "bloom-player";

// Putting a game page into the Play tab's play mode, and taking it, or a copy of it, out again.
//
// Play mode belongs to the game page's tab control, not to the Game tool: the tester can switch
// to and from Play, and go on playing, with any tool open (Talking Book, say), or with the toolbox
// closed. So nothing here may depend on which tool is open. The toolbox cleans every copy of the
// page it saves (removePlayModeMarkupFromClone), and ends play mode when the page is unloaded
// (endPlayModeAsPageUnloads), whichever tool is open.
//
// Taking a page or a copy out of play mode is bloom-player's undoPrepareActivity(): it puts the
// draggables back where the author put them and removes everything playing added to the page, on
// whichever element it is given (bloom-player's tests, and undoPrepareActivityContract.spec.ts here,
// pin that down).
//
// Like the rest of the game tab handling, this must run in the toolbox bundle: bloom-player's
// runtime keeps its play state (including where the author put each draggable) in module variables,
// and so does this file, so every call has to reach the same copy of both.

// The live page that prepareActivity() has put into play mode, if any.
let pagePreparedForPlay: HTMLElement | undefined;

// Put the live page into play mode, unless it is already in it. Returns true if it did, so the caller
// knows whether to do its own one-time setup too.
//
// Calling prepareActivity() again on a page already in play mode is what this prevents. It records
// where the draggables are as where the author put them, so a second call while the tester has things
// dragged around makes every later save write the tester's positions into the book, and makes leaving
// Play put the draggables back there. It also adds a second set of order-sentence words. There are
// several ways to ask for Play twice: clicking the Play tab when it is already selected, and the page
// load and the toolbox's repeated newPageReady() each asking for the current tab.
export function preparePageForPlay(page: HTMLElement): boolean {
    if (page === pagePreparedForPlay) {
        return false;
    }
    prepareActivity(page, (_next) => {
        /* nothing to do */
    });
    pagePreparedForPlay = page;
    return true;
}

// Put the live page into the state the tabs other than Play want. This runs undoPrepareActivity()
// whether or not the page was in play mode, because that is also what shows the draggables' content
// in their targets, which those tabs need.
export function takePageOutOfPlayMode(page: HTMLElement): void {
    undoPrepareActivity(page);
    if (page === pagePreparedForPlay) {
        pagePreparedForPlay = undefined;
    }
}

// If the page is in play mode, take a copy of it that is about to be saved out of it, so the book
// records the draggables where the author put them and none of play's markup. The live page is left
// alone, so the tester can go on playing.
export function removePlayModeMarkupFromClone(pageClone: HTMLElement): void {
    if (pagePreparedForPlay) {
        undoPrepareActivity(pageClone);
    }
}

// The page is going away: end play mode on it, if it is in it (which, among other things, stops any
// video it is playing). The game tab stays on Play, so the next page load prepares the page afresh.
export function endPlayModeAsPageUnloads(): void {
    if (pagePreparedForPlay) {
        takePageOutOfPlayMode(pagePreparedForPlay);
    }
}
