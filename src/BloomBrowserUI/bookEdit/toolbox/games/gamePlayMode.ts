import { prepareActivity, undoPrepareActivity } from "bloom-player";
import { kDraggableIdAttribute } from "../canvas/canvasElementDraggables";

// Putting a game page into the Play tab's play mode, and taking it, or a copy of it, out again.
//
// Play mode belongs to the game page's tab control, not to the Game tool: the tester can switch
// to and from Play, and go on playing, with any tool open (Talking Book, say), or with the toolbox
// closed. So nothing here may depend on which tool is open. The toolbox cleans every copy of the
// page it saves (removePlayModeMarkupFromClone), and ends play mode when the page is unloaded
// (endPlayModeAsPageUnloads), whichever tool is open.
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
        removePlayMarkupUndoPrepareActivityMisses(page);
        pagePreparedForPlay = undefined;
    }
}

// If the page is in play mode, take a copy of it that is about to be saved out of it, so the book
// records the draggables where the author put them and none of play's markup. The live page is left
// alone, so the tester can go on playing.
export function removePlayModeMarkupFromClone(pageClone: HTMLElement): void {
    if (!pagePreparedForPlay) {
        return;
    }
    undoPrepareActivity(pageClone);
    removePlayMarkupUndoPrepareActivityMisses(pageClone);
}

// The page is going away: end play mode on it, if it is in it (which, among other things, stops any
// video it is playing). The game tab stays on Play, so the next page load prepares the page afresh.
export function endPlayModeAsPageUnloads(): void {
    if (pagePreparedForPlay) {
        takePageOutOfPlayMode(pagePreparedForPlay);
    }
}

// Things bloom-player does to the page during play that its undoPrepareActivity() leaves behind. None
// of them is ever part of an authored page: Bloom's own editing uses none of these classes, and does
// not set touch-action on draggables.
const kPlayOnlyClasses = [
    // on a draggable while the tester drags it (so in a copy taken in the middle of a drag)
    "bloom-ui-dragging",
    // undoPrepareActivity only takes this off draggables whose starting position it recorded
    "bloom-draggedToTarget",
    // narration highlighting, from playing the page's audio
    "ui-audioCurrent",
    "ui-audioCurrentImg",
    "ui-suppressHighlight",
    // a video that the browser would not start playing
    "autoplayBlocked",
];
// shown over a video that cannot be played
const kVideoErrorMessageClass = "video-error-message";

function removePlayMarkupUndoPrepareActivityMisses(
    pageOrClone: HTMLElement,
): void {
    // One pass over the page, since this runs on every save while a page is being played.
    const selector = [
        ...kPlayOnlyClasses.map((c) => "." + c),
        "." + kVideoErrorMessageClass,
        `[${kDraggableIdAttribute}]`,
    ].join(",");
    pageOrClone.querySelectorAll<HTMLElement>(selector).forEach((element) => {
        if (element.classList.contains(kVideoErrorMessageClass)) {
            element.remove();
            return;
        }
        element.classList.remove(...kPlayOnlyClasses);
        // prepareActivity() sets this on each draggable, so that dragging works on touch screens.
        if (element.hasAttribute(kDraggableIdAttribute)) {
            element.style.removeProperty("touch-action");
        }
    });
}
