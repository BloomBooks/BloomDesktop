/**
 * Keeping the current tool's markup (e.g. decodable-reader highlighting, or the talking
 * book's sentence spans) intact while the user edits the page: the keypress/paste handlers
 * on the .bloom-editable divs, and the CKEditor bookmark coordination that lets a tool
 * change the html out from under the user without losing the insertion point.
 * This is not toolbox management; all it needs from the toolbox is which tool, if any,
 * currently wants its markup updated.
 */
import $ from "jquery";
import { isLongPressEvaluating } from "../longPressShared";
import type { ToolBox } from "./toolbox";
import { EditableDivUtils } from "../js/editableDivUtils";

// The one toolbox, whose current tool we ask to update its markup. toolbox.ts hands it to
// us in configurePageEditingHandlers rather than us importing it, because toolbox.ts
// imports this module to install the handlers, and importing it back would be a cycle.
let toolbox: ToolBox;

let keypressTimer: ReturnType<typeof setTimeout> | null = null;

// The pending markup update asked for by an undo or redo, if any. It deliberately does NOT
// share keypressTimer: every keystroke cancels that one, and Ctrl+Z ends with a keyup, which
// would therefore throw away the very update the undo just asked for.
let undoRedoMarkupTimer: ReturnType<typeof setTimeout> | null = null;

// When the last undo/redo markup pass that actually got as far as doing the markup began, and
// how close together we will let them be.
// A single undo asks for its markup at once, because the whole point is to repaint the
// highlights it just detached before anyone sees them missing. But holding Ctrl+Z down
// auto-repeats the undo, and each repeat asks for another pass; with no delay to coalesce
// them, that is a full re-markup of the page per repeat, which is what the 500ms typing
// throttle exists to avoid. So the first one runs immediately and any that pile in behind it
// wait their turn. (A burst is bounded anyway - ckeditor's undo stack is 20 deep - but 20
// re-markups in half a second while ckeditor is mid-burst is still worth not doing.)
let lastUndoRedoMarkupStartTime = 0;
const minMillisecondsBetweenUndoRedoMarkups = 150;

/**
 * Installs on the .bloom-editable elements of the given container the handlers that keep
 * the current tool's markup up to date as the user types and pastes. Called by
 * ToolBox.configureElementsForTools, which passes the toolbox so we can find its current tool.
 */
export function configurePageEditingHandlers(
    container: HTMLElement,
    theToolbox: ToolBox,
): void {
    toolbox = theToolbox;
    // the toolbox itself handles keypresses in order to manage the process
    // of giving each tool a chance to update things when the user stops typing
    // (while maintaining the selection if at all possible).
    /* Note: BL-3900: "Decodable & Talking Book tools delete text after longpress".
                In that bug, longpress.replacePreviousLetterWithText() would delete back
                to the start of the current markup span (e.g. a sentence in
                Talking Book, or a non-decodable word in Decodable Reader).
                A past fix was to trigger markup on keydown, rather than keyup or keypress.
                Keeping the comment in case it recurs:
                ****This is exactly the opposite of what we would expect****

                If we trigger on keyup here, the sequence looks right but longpress will eat up the span.
                Here's the sequence:
                        longpress: replacePreviousLetterWithText()
                        Toolbox: setting timer markup
                        Toolbox: doing markup
                        Toolbox: Restoring Selection after markup

                So the mystery in the above case is, what is going on with the dom and longpress.replacePreviousLetterWithText()
                such that replacePreviousLetterWithText() replaces a bunch of characters instead of 1 character?


                Counterintuitively, if we instead trigger on keydown here, the settimeout()
                doesn't fire until longpress is all done and all is well:
                        1) Toolbox: setting timer markup
                        2) longpress: replacePreviousLetterWithText()
                        3) Toolbox: doing markup
                        4) Toolbox: Restoring Selection after markup

                (3) is delayed presumably because (2) is still in the event-handling loop. That's fine. But the
                mystery then was: why does it help longpress.replacePreviousLetterWithText() to not eat up a whole span?

                It turns out that when longpress goes to get the selection,
                in the keyup or keypress senarios, the selection's startContainer is the markup span (which has the #text
                node inside of it). So then a deleteContents() wiped out *all* the text in the span (I've added a check for
                that scenario so that if it happens again, longpress will fail instead of deleting text).
                However in the keydown case, we get a #text node for the selection, as expected. My hypothesis is that by doing
                the work during the keyDown event, some code somewhere runs when the key goes up, restoring a good selection.
                So when longpress is used, it doesn't trip over the span.

                For now I'm just going to commit the fix and if someday we revisit this, maybe another piece of the
                puzzle will emerge.
                ----end of BL-3900 comment
                Using Keydown had its own problems (BL-12889). If the user holds down a key (e.g., for longpress), it will
                fire repeatedly. I made various further attempts to get handleKeyboardInput to abort if longpress was
                doing something, but it was fragile and I never got it entirely right. Keyup is much better, though
                watch out for a keyup from the extra keystroke that is one way to select a key in longpress. And BL-3900
                does not seem to have recurred. Not sure whether this is because at some point we got a newer version of
                CkEditor, or because of improvements we've made to bookmark handling (including in the PR for BL-12889),
                or because of the switch to WebView2, or something else. But as far as I can tell, using keyup helps
                solve BL-12889 and does not cause BL-3900 to recur.
                -----and then as part of dealing with BL-15334, we found that keyup was not enough to catch all ctrl-V events,
                even when combined with handling paste events as such, so we added a keydown event for that.
                This should be safe because ctrl-V should not interact with longpress.
        */

    $(container)
        .find(".bloom-editable")
        .keydown((event) => {
            // Repair a split paragraph BEFORE this keystroke lands in it. Chromium drops
            // glyphs from a ligature when the paragraph's text is in two adjacent text
            // nodes and something then edits one of them - and that "something" is normally
            // just this keystroke, so by the time we hear the keyup the letters have already
            // stopped being painted, and the caret has stopped moving through them
            // (BL-16717). Doing it here means the character arrives in a box whose paragraph
            // is one whole text node again, which Chromium shapes correctly.
            // The splits get there without our help: backspacing in the middle of a word
            // leaves the paragraph in two pieces, and so does long-press inserting the
            // character it composed. So the sweep at the end of handlePageEditing's mainTask
            // is not enough on its own: it runs half a second after the box goes quiet, and
            // the damaging keystroke is the one that comes BEFORE that.
            // NB: do NOT gate this on window.top[isLongPressEvaluating] the way mainTask
            // does. Long-press sets that flag in its own keydown handler, so it is true
            // during every keydown, and gating on it here does nothing but turn the repair
            // off completely (measured: it never ran). What we actually have to stay out of
            // is the window while the long-press popup is up and the user is choosing a
            // character, because long-press composes into a text node of its own and takes
            // that character out again if they choose another one. Its own test for "the
            // popup is showing" is the presence of .long-press-popup in the page, so we use
            // that. An IME composition is the same kind of hazard, hence isComposing.
            // This deliberately runs for EVERY key, not just the ones that insert text.
            // Skipping the arrow keys the way the keyup handler below does would look like
            // a free saving and would quietly give back half the bug: a split paragraph is
            // also what makes the caret stop moving, so it is the arrow key's own keydown
            // that has to repair it. Measured in a running Bloom: with "waffle" split as
            // "waf"|"fle", three consecutive ArrowLefts left the caret drawn at the same x;
            // repairing on the first of them restores one-character-per-press exactly.
            const editableBeingTypedIn = event.currentTarget as HTMLElement;
            const longPressPopupIsUp =
                !!editableBeingTypedIn.ownerDocument.querySelector(
                    ".long-press-popup",
                );
            if (
                !longPressPopupIsUp &&
                !(event.originalEvent as KeyboardEvent)?.isComposing
            ) {
                EditableDivUtils.mergeAdjacentTextNodes(editableBeingTypedIn);
            }

            // Ctrl/Cmd+V doesn't always produce a keyup we can rely on in all environments.
            // Schedule the same markup-update side effects explicitly when paste is requested.
            // This should not interact with longpress, which doesn't handle keypresses with ctrl.
            // In theory, this should be dead code: the keydown should be followed by a keyup
            // which will cancel the timeout started by this call and then schedule a new one.
            // However, actual users report that the side effects of pasting sometimes don't happen.
            // CoPilot suggested that the keydown event might be fired more reliably. For example,
            // it's possible that a CkEditor event handler intercepts the keyup and stops
            // propagation. So as a desperation attempt, I'm adding a keydown handler. I can't
            // reproduce the problem, so the only way to test is to release to testers.
            const isPasteShortcut =
                (event.ctrlKey || event.metaKey) && event.keyCode === 86;
            if (isPasteShortcut) {
                setTimeout(() => handlePageEditing("paste"), 0);
            }
        })
        .keyup((event) => {
            //don't do markup on cursor keys
            if (event.keyCode >= 37 && event.keyCode <= 40) {
                // this is check is another workaround for one scenario of BL-3490, but one that, as far as I can tell makes sense.
                // if all they did was move the cursor, we don't need to look at markup.
                //console.log("skipping markup on arrow key");
                return;
            }
            handlePageEditing();
        })
        .on("compositionend", (_argument) => {
            // Keyman (and other IME's?) don't send keydown events, but do send compositionend events
            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-5440.
            handlePageEditing();
        })
        // These next two were added to try to catch paste events that are not caught by the keyup
        // on Ctrl+V. They don't catch paste caused by the toolbar button, which is caught elsewhere.
        // I'm not sure how a paste can be triggered in current Bloom without causing a keyup,
        // but just possibly the paste might take longer than the standard keyup delay to finish
        // modifying the DOM? AI suggested adding these and I decided it was safest to keep them.
        .on("input", (event) => {
            const inputEvent = event.originalEvent as InputEvent;
            if (
                inputEvent?.inputType &&
                inputEvent.inputType.startsWith("insertFromPaste")
            ) {
                handlePageEditing();
            }
        })
        .on("paste", () => {
            // Wait a tick so the DOM reflects the pasted content.
            setTimeout(() => handlePageEditing(), 0);
        });
}

let keydownEventCounter = 0;
const retryDelayForMarkupUpdateInMilliseconds = 100;
const maxMarkupUpdateRetries = 3;

export function scheduleMarkupUpdateAfterPaste(): void {
    // AI thinks we might need this to "allow the DOM to settle" even before we do the
    // little bit that handlePageEditing does before setting up its own delay.
    // I could not understand its explanation and am not convinced we need this.
    // However, I'm trying to fix a race condition that results in a problem
    // that is hard to reproduce reliably. I'd rather have a timeout that we don't
    // need than have the markup occasionally not update, let alone somehow have
    // the markup update somehow mess up the paste. So I decided to leave it in.
    setTimeout(() => handlePageEditing("paste"), 0);
}

// Call this whenever an Undo or Redo has replaced the content of an editable, from wherever
// that Undo was initiated (Ctrl+Z in the text, the Undo button in the top bar, the reader
// tools' own undo stack).
//
// It matters more than it looks. Undo does not edit the text in place: it writes a whole
// saved snapshot over the editable, which builds new text nodes for everything in the box.
// The tools' highlights are ::highlight() pseudo-elements painted over live Ranges into
// those text nodes (see textHighlightManager.ts), so every highlight in that box dies at
// that moment - it stays in the registry but paints nothing, and only a markup pass can put
// it back. Left to itself, the markup pass either doesn't happen at all (a click on the top
// bar's Undo button produces no keystroke) or happens and gives up (see the three places
// below where "undoOrRedo" is treated differently), which is why the Leveled and Decodable
// Reader highlights could stay gone until the user typed something (BL-16558).
export function updateMarkupAfterUndoOrRedo(): void {
    handlePageEditing("undoOrRedo");
}

// What asked for a markup update. It decides several things below, because an undo is not
// just another edit: it is a single deliberate action, and it can leave the page in states
// that we would rather not do the markup in but have no choice about.
type MarkupUpdateTrigger = "editing" | "paste" | "undoOrRedo";

// Handle edits to the page: mainly triggered by key up, but also by paste and by undo/redo.
// For various reasons a single paste may cause this to get called several times, but the 500ms
// delay should prevent us from doing the markup more than once per paste.
// Similarly, since updating the markup is fairly costly, it's good not to do it on every keystroke
// while the user is typing rapidly.
function handlePageEditing(trigger: MarkupUpdateTrigger = "editing"): void {
    const isUndoOrRedo = trigger === "undoOrRedo";
    // Typing gets no retries because the next keystroke brings another update along anyway.
    // The other two have no such fallback: a paste can leave the selection temporarily in a
    // state we can't mark up in, and an undo can leave it with no selection at all (e.g.
    // readerToolsModel.undo() restores the html but returns before makeSelectionIn() when it
    // has no saved offset). An undo from the top bar button has no keystroke behind it, so
    // giving up on the first look would leave the highlights dead with no second chance.
    const remainingRetriesForInvalidSelectionState =
        trigger === "editing" ? 0 : maxMarkupUpdateRetries;
    // BL-599: "Unresponsive script" while typing in text.
    // The function setTimeout() returns an integer, not a timer object, and therefore it does not have a member
    // function called "clearTimeout." Because of this, the jQuery method $.isFunction(keypressTimer.clearTimeout)
    // will always return false (since "this.keypressTimer.clearTimeout" is undefined) and the result is a new 500
    // millisecond timer being created every time the doKeypress method is called, but none of the pre-existing timers
    // being cleared. The correct way to clear a timeout is to call clearTimeout(), passing it the integer returned by
    // the function setTimeout().

    //if (this.keypressTimer && $.isFunction(this.keypressTimer.clearTimeout)) {
    //  this.keypressTimer.clearTimeout();
    //}
    const counterValueThatIdentifiesThisKeyDown = ++keydownEventCounter;
    // An undo waits on its own timer rather than the shared one, because every keystroke
    // cancels the shared one - and Ctrl+Z ends with a keyup, which would therefore throw away
    // the very update the undo just asked for. These two put the choice in one place so the
    // rest of the method doesn't have to care which timer it is. Replacing whatever that same
    // timer was already waiting for is what makes a burst of events do the markup just once.
    function cancelPendingUpdate(): void {
        if (isUndoOrRedo) {
            if (undoRedoMarkupTimer) clearTimeout(undoRedoMarkupTimer);
            undoRedoMarkupTimer = null;
            return;
        }
        if (keypressTimer) clearTimeout(keypressTimer);
        keypressTimer = null;
    }
    function scheduleMainTask(
        task: () => void,
        delayMilliseconds: number,
    ): void {
        cancelPendingUpdate();
        if (isUndoOrRedo) {
            undoRedoMarkupTimer = setTimeout(task, delayMilliseconds);
        } else {
            keypressTimer = setTimeout(task, delayMilliseconds);
        }
    }
    cancelPendingUpdate();
    // Not sure we need this now the method is triggered by keyup. If it is triggered by keydown,
    // we have a problem:
    // If we don't do this check, then the last keydown from autorepeat during longpress will
    // start the timer, and by the time the timer goes off, keyup has cleared the flag. Then we can
    // get unexpected cursor movements that I haven't fully understood.
    // On the other hand, if we DO this check, the flag gets set by the keydown handler in longpress
    // even for ordinary keystrokes, and that handler seems to fire first, and so this NEVER executes.
    // I'm leaving it in for now because the method might get called on a keyup connected with using
    // a key in longpress to select one of the options, and in that case, we don't want to do the markup
    // (until the keyup from the original key, of course).
    // Undo/redo is exempt: longpress sets that flag on EVERY keydown (see its onKeyDown) and
    // clears it on keyup, so Ctrl+Z, which does its work on the keydown, always finds it set,
    // and honoring it here meant the undo never got its markup update at all. Ctrl+Z can't be
    // a longpress in any case: longpress is about holding down a letter key on its own.
    if (!isUndoOrRedo && window?.top?.[isLongPressEvaluating]) {
        return;
    }
    // If this was making DOM changes that we want to save, we would want to try to use
    // addRequestPageContentDelay and removeRequestPageContentDelay or wrapWithRequestPageContentDelay
    // to prevent the user from trying to save while we're in the middle of making changes.
    // Care would be needed to keep the calls matched up: if there's a timer already running, that
    // would mean we already have a page content delay in place and should not add another.
    // However, the markup changes that we are making here are stripped out by Save anyway,
    // so I don't believe we need to worry about suppressing saves while we're doing this.
    // We'll initially try this mainTask after 500ms. If the user types another key before that,
    // the code above will cancel that and start a new 500ms timer, so we won't do the mainTask
    // until 500ms after the user stops typing. Also, if we find that the selection state is
    // (perhaps temporarily) invalid for doing the markup, we'll try again a few times with a
    // shorter delay, and if it still isn't valid, we'll just give up until the next keyup or paste.
    const mainTask = async (remainingRetries: number) => {
        const page: HTMLIFrameElement = <HTMLIFrameElement>(
            parent.window.document.getElementById("page")
        );
        if (!page || !page.contentWindow) return; // unit testing?

        const selection: Selection | null = page.contentWindow.getSelection();
        const anchor: Node | null = selection ? selection.anchorNode : null;
        const active = anchor
            ? <HTMLDivElement>$(anchor).closest("div").get(0)
            : null;
        // A selection that covers a range of text, rather than being a simple insertion point,
        // normally makes us leave the markup alone until the user does something simpler.
        // Undo/redo is the exception: it restores whatever selection its snapshot was taken
        // with, and the markup (and with it the highlights) has to be brought up to date
        // regardless. The bookmark machinery below preserves a range just as it does an
        // insertion point.
        const selectionIsRange = !!(
            selection &&
            (selection.rangeCount > 1 ||
                (selection.rangeCount === 1 &&
                    !selection.getRangeAt(0).collapsed))
        );
        const selectionStateIsInvalidForMarkup =
            !active || (selectionIsRange && !isUndoOrRedo);

        if (selectionStateIsInvalidForMarkup) {
            // Copilot suggested that there are some cases after a paste where the selection
            // is only temporarily a range, so it's worth trying again a few times.
            // This callback can also be canceled by a new keypress etc.
            if (remainingRetries > 0) {
                scheduleMainTask(
                    () => mainTask(remainingRetries - 1),
                    retryDelayForMarkupUpdateInMilliseconds,
                );
            }
            return; // don't even try to adjust markup while there is some complex selection
        }

        // This is improbable, but it prevents Typescript from complaining about the next conditional.
        if (!window || !window.top) {
            return;
        }

        // Now we're triggering this on keyup, I don't think we'll ever find this flag true.
        // Just possibly it might be following a keyup from a choose-option keypress in longpress.
        // I'm leaving the previous comment because it captures considerable history that might still be relevant.
        // If longpress is currently engaged trying to determine what, if anything, it needs
        // to do, we postpone the markup. Inexplicably, longpress and handleKeyboardInput (formerly handleKeydown)
        // started interfering again even after the fix for BL-3900 (see comments for
        // that elsewhere in this file). This code was added for BL-5215.
        // It would be great if we didn't have settle for using window.top,
        // but the other player here (jquery.longpress.js) is in a totally different
        // context currently, so my other attempts to share a boolean failed.
        // (Undo/redo is exempt, for the reason given where this flag is tested above.)
        if (!isUndoOrRedo && window.top[isLongPressEvaluating]) {
            return;
        }

        // the hard thing about all this is preserving the user's insertion point while we change the actual
        // html out from under them to add/remove markup.
        // ckeditor specific discussion: http://stackoverflow.com/questions/16835365/set-cursor-to-specific-position-in-ckeditor
        // This "bookmark" approach makes that easy:
        // We insert a dummy element where the insert point is. Later when we do the markup,
        // we'll find the bookmark again, put the selection there, and remove this element.
        // The problem with this approach is that when the user is fixing an existing word, the markup
        // will see our bookmark as a word-breaking element. For example, if I type "houze" and go
        // to fix that z, the markup routine is going to see "hous"-bookmark-"e". When the user
        // clicks away, the markup will be redone and fixed. So this is a known tradeoff; we get
        // more reliable insertion-point-preservation, at the cost of some temporarily inaccurate
        // markup.
        const selNode = selection ? selection.anchorNode : null;
        const editableDiv = selNode
            ? $(selNode).parents(".bloom-editable")[0]
            : null;
        // In 3.9, this is null when you press backspace in an empty box; the selection.anchorNode is itself a .bloom-editable, so
        // presumably we could adjust the above query to still get the div it's looking for.
        if (editableDiv) {
            const ckeditorOfThisBox = (
                editableDiv as HTMLElement & { bloomCkEditor?: CKEDITOR.editor }
            ).bloomCkEditor;
            // Normally every editable box has a ckeditor attached. But some arithmetic template boxes are
            // intended to contain numbers not needing translation and don't get one...because the logic
            // that invokes WireToCKEditor is looking for classes like bloom-content1 that are not present
            // in ArithmeticTemplate. Here we're presuming that if a block didn't get one attached,
            // it's not true vernacular text and doesn't need markup. So all the code below is skipped
            // if we don't have one.
            if (ckeditorOfThisBox) {
                let ckeditorSelection = ckeditorOfThisBox.getSelection();
                if (!ckeditorSelection) {
                    return; // may be changing pages?
                }
                // We are now certainly going to do the markup, which is the work the next
                // undo/redo has to wait behind. Anything that gave up before this point - an
                // unusable selection and each of its retries, no editable under the caret, a
                // box with no ckeditor - did none of that work and must not make one wait.
                // (Below this point we always at least remove comments and clean up nbsps,
                // whether or not a tool is active, so the work is real either way.)
                if (isUndoOrRedo) {
                    lastUndoRedoMarkupStartTime = Date.now();
                }

                // Which tool (if any) is current is the toolbox's business; we just ask it.
                // Nothing below reads it after an await, so reading it once here behaves the
                // same as reading the toolbox's own variable at each use, as we used to.
                const currentTool = toolbox.getCurrentTool();
                // If there's no tool active, we don't need to update the markup.
                const activeTool =
                    currentTool && toolbox.toolboxIsShowing()
                        ? currentTool
                        : undefined;

                // Creating a bookmark inserts a hidden span at the insertion point, which SPLITS
                // the text node the user is typing in. That is destructive enough to be worth
                // avoiding: even though removing the bookmark and rejoining the text leaves the
                // DOM exactly as it was, Chromium goes on painting the paragraph's old glyphs
                // where a ligature straddled the split, so letters the user typed stop being
                // drawn until something else forces a repaint (BL-16717).
                // Nothing below rewrites this box unless a tool is active, or there is actually
                // a comment or an nbsp to clean up - and if nothing rewrites the box, there is no
                // selection to preserve. So only pay for a bookmark when one of those is true,
                // which for ordinary typing is never.
                const needsBookmarks =
                    !!activeTool || editableMightBeRewritten(editableDiv);

                // there is also createBookmarks2(), which avoids actually inserting anything. That has the
                // advantage that changing a character in the middle of a word will allow the entire word to
                // be evaluated by the markup routine. However, testing shows that the cursor then doesn't
                // actually go back to where it was: it gets shifted to the right.
                let bookmarks = needsBookmarks
                    ? ckeditorSelection.createBookmarks(true)
                    : undefined;

                // For some reason, we have cases, mostly (always?) on paste, where
                // ckeditor is inserting tons of comments which are messing with our parsing
                // See http://issues.bloomlibrary.org/youtrack/issue/BL-4775
                removeCommentsFromEditableHtml(editableDiv);
                if (activeTool) {
                    if (activeTool.isUpdateMarkupAsync()) {
                        // It's possible that removeCommentsFromEditableHtml moved the selection, typically
                        // to the start of the editableDiv. This doesn't matter on the synchronous branch,
                        // because we restore it at the end of this method, after the other updates, and no
                        // keystroke can occur in the meantime.
                        // But on this branch, with an await, the 'rest of this method' may execute much
                        // later, possibly after the next keystroke is processed. If we wait till then to fix
                        // the selection, the selection may be briefly visible in the wrong place. Much worse,
                        // any intervening keystrokes go to that incorrect position (BL-10133). So fix
                        // it now, and then again after actually changing the markup, which might move the selection again.
                        // (This is why we don't allow updateMarkupAsync to modify the DOM, except by means of
                        // the function it returns, which is executed synchronously with fixing the selection.)
                        ckeditorOfThisBox
                            .getSelection()
                            .selectBookmarks(bookmarks);
                        ckeditorSelection = ckeditorOfThisBox.getSelection();
                        bookmarks = ckeditorSelection.createBookmarks(true);

                        const actualUpdateFunc =
                            await activeTool.updateMarkupAsync();
                        if (
                            keydownEventCounter ===
                            counterValueThatIdentifiesThisKeyDown
                        ) {
                            // go ahead and make the change. (If the counts are different,
                            // we got another keystroke, and initiated a new updatemarkup,
                            // while processing this one. We don't want to save the results
                            // of updating for the earlier keystroke.)
                            actualUpdateFunc();
                        }
                    }
                }

                cleanUpNbsps(editableDiv);

                // The synchronous branch is used only by the decodable and leveled reader tools,
                // and their markup no longer changes the DOM: it paints violations with
                // ::highlight() over live Ranges (BL-16558). Those Ranges must therefore be
                // created AFTER the last thing that rewrites this editable's content.
                // cleanUpNbsps() ends with an unconditional `editableDiv.innerHTML = ...`, which
                // replaces every text node in the box, so any Range pointing into them collapses.
                // While this call came before it, the reader highlights were painted and then
                // immediately detached on every pause in typing, and nothing reappeared until
                // some other path redid the markup (e.g. changing the level).
                //
                // Not covered by a unit test: the pieces are (cleanUpNbsps in
                // pageEditingMarkupSpec.ts, which now checks that it leaves the text nodes alone
                // when it has nothing to convert; the highlight primitives in
                // textHighlightManagerSpec.ts), but the *ordering* between them is only exercised
                // by running handlePageEditing, and that needs a live ckeditor instance on the
                // editable div plus the parent window's "page" iframe and a real Selection - none
                // of which we can stand up in jsdom. So if you reorder anything in here, test it
                // by typing in a Leveled Reader book and watching the over-long sentences stay
                // highlighted.
                if (activeTool && !activeTool.isUpdateMarkupAsync()) {
                    // Note, the updateMarkup routine must be sure to use the result of
                    // ckEditor's getData() method, not the raw HTML of the editableDivs.
                    // See EditableDivUtils.doCkEditorCleanup() and .restoreSelectionFromCkEditorBookmarks().
                    // Unfortunately, we can't easily do that in a top-level (general for all tools) way because of
                    // our current architecture. Namely, the reader tools have a lower-level
                    // doMarkup() which gets called more than just from here.
                    activeTool.updateMarkup();
                }

                //set the selection to wherever our bookmark node ended up
                //NB: in BL-3900: "Decodable & Talking Book tools delete text after longpress", it was here,
                //restoring the selection, that we got interference with longpress's replacePreviousLetterWithText(),
                // in some way that is still not understood. This was fixed by changing all this to trigger on
                // a different event (keydown instead of keypress).
                // Note: causing the bookmarks to be selected actually removes the bookmark spans.
                if (bookmarks) {
                    ckeditorOfThisBox.getSelection().selectBookmarks(bookmarks);
                }

                // Removing a bookmark leaves the text that was on either side of it as two
                // adjacent text nodes, which makes Chromium drop glyphs from ligatures near the
                // join (BL-16717). But bookmarks are only one of the things that split a
                // paragraph's text - Chromium's own backspace and long-press's inserted
                // character do it too - so this is not conditional on our having made one. It
                // is the box-is-quiet sweep that catches whatever the per-keystroke repair in
                // ToolBox's keydown handler could not (a paste from the menu, an IME, an undo).
                // It costs a walk of the box's text nodes and does nothing at all unless it
                // finds a split. See mergeAdjacentTextNodes().
                EditableDivUtils.mergeAdjacentTextNodes(editableDiv);
            }
        }
        // clear this value to prevent unnecessary calls to clearTimeout() for timeouts that have already expired.
        if (isUndoOrRedo) {
            undoRedoMarkupTimer = null;
        } else {
            keypressTimer = null;
        }
    };
    scheduleMainTask(
        () => mainTask(remainingRetriesForInvalidSelectionState),
        // An undo has already put the restored text in the DOM, and unlike typing it is a
        // single deliberate action, so there is nothing to wait for. Doing it at once (rather
        // than after the usual 500ms of quiet) keeps the highlights, which the undo has just
        // detached from the text, from visibly blinking off. The only reason to wait at all is
        // to keep an auto-repeating Ctrl+Z from re-marking the page on every repeat.
        isUndoOrRedo
            ? Math.max(
                  0,
                  minMillisecondsBetweenUndoRedoMarkups -
                      (Date.now() - lastUndoRedoMarkupStartTime),
              )
            : 500,
    );
}

function RemoveNonPTags(editableDivHtml: string): string {
    return editableDivHtml
        .replace(/<[^p\/].*?>/g, "")
        .replace(/<\/[^p].*?>/g, "");
}

// Check if the &nbsp; is at the start or end of a paragraph, regardless of any other tags in between (e.g. the empty talking book spans)
function NbspIsOnEdgeOfParagraph(
    editableDivHtml: string,
    nbspIndex: number,
): boolean {
    const beforeNbsp = editableDivHtml.substring(0, nbspIndex);
    const afterNbsp = editableDivHtml.substring(nbspIndex + "&nbsp;".length);

    const beforeNbspWithoutNonPTags = RemoveNonPTags(beforeNbsp).trim();
    const afterNbspWithoutNonPTags = RemoveNonPTags(afterNbsp).trim();

    return (
        beforeNbspWithoutNonPTags.match(/<p[^>]*>$/) !== null ||
        afterNbspWithoutNonPTags.substring(0, 4) === "</p>"
    );
}

// Starting with webview2, we were getting scenarios where nbsps were could remain in the div when not wanted.
// One way to cause this: type two spaces, not at the end of the text box. Then, delete one of them.
// We want to remove nbsps unless
// 1. they are at the start or end of the div or paragraph
// 2. they are adjacent to a regular space (the browser collapses regular spaces but not other whitespace)
// 3. they are possibly wanted for French-style punctuation
// See BL-12391.
// (exported for testing)
export function cleanUpNbsps(editableDiv: HTMLElement) {
    // Remove the &nbsp; from the bookmarks so they don't interfere with the algorithm below.
    // We'll put them back in at the end.
    const originalBookMarkContent = setCkeditorBookmarkContent(editableDiv, "");

    let editableDivHtml = editableDiv.innerHTML;
    // innerText does not include hidden text; innerHTML does.
    // So we use textContent -- which includes hidden text -- to ensure the html and text are in sync.
    let editableDivText = editableDiv.textContent;
    if (!editableDivText) return;

    const preserveNbspAfter = [" ", "«", "—"];
    const preserveNbspBefore = [" ", "»", ":", ";", "!", "?"];

    // Whether we actually converted anything. Assigning innerHTML rebuilds every node in the box
    // even when the string is unchanged, which loses the selection and collapses any Range
    // pointing into the old text nodes -- and the reader tools' highlights and the Talking Book
    // tool's audio highlights are live Ranges. Almost every keystroke leaves nothing to convert,
    // so only write when there is something to write.
    let replacedAnNbsp = false;

    let i = -1;
    let j = -1;
    // Simultaneously loop through the text and the html, finding each corresponding nbsp.
    // The text shows us what the adjacent characters are so we can make a replacement decision, but
    // the actual replacement is done in the html so as to keep the markup.
    // We also make the replacements in the text as we go so that, for example,
    // if we change one nbsp to a regular space, that space prevents converting an adjacent nbsp.

    while (true) {
        i = editableDivHtml.indexOf("&nbsp;", i + 1); // i+1 works whether or not we replaced the previous nbsp
        if (i === -1) break;
        j = editableDivText.indexOf("\u00A0", j + 1);
        if (j === -1) {
            // Pathological case; nbsp in attribute?
            // (We do put &nbsp; in a data-original attribute when replacing it with a symbol as part of showing
            // invisibles, but if it is still there at this point something must have gone wrong)
            // Follow do no harm principle and just leave the div unaltered.
            console.error(
                "Unexpected situation discovered in cleanUpNbsps. Html has nbsp but text doesn't.",
            );
            console.error("editableDivHtml: " + editableDivHtml);
            console.error("editableDivText: " + editableDivText);

            // Restore the bookmarks. See comment above.
            if (originalBookMarkContent)
                setCkeditorBookmarkContent(
                    editableDiv,
                    originalBookMarkContent,
                );
            return;
        }
        if (j === 0 || j === editableDivText.length - 1) continue;
        // If the nbsp is the first or last character in a paragraph, don't replace or the space will get lost in whitespace collapse
        if (NbspIsOnEdgeOfParagraph(editableDivHtml, i)) continue;

        if (
            !preserveNbspAfter.includes(editableDivText[j - 1]) &&
            !preserveNbspBefore.includes(editableDivText[j + 1])
        ) {
            editableDivHtml =
                editableDivHtml.substring(0, i) +
                " " +
                editableDivHtml.substring(i + "&nbsp;".length);
            editableDivText =
                editableDivText.substring(0, j) +
                " " +
                editableDivText.substring(j + 1);
            replacedAnNbsp = true;
        }
    }
    if (replacedAnNbsp) editableDiv.innerHTML = editableDivHtml;

    // Restore the bookmarks. See comment above.
    if (originalBookMarkContent)
        setCkeditorBookmarkContent(editableDiv, originalBookMarkContent);
}

// For the given div, replace the content of any ckeditor bookmarks with the given content.
// We actually only expect one bookmark for our case, but we're being safe.
// Return the original content (of the last one... we have to pick one...) so we can restore it later.
function setCkeditorBookmarkContent(
    editableDiv: HTMLElement,
    content: string,
): string | undefined {
    let existingContent: string | undefined = undefined;

    const ckeBookmarks = editableDiv.querySelectorAll("[id^='cke_bm_']");
    ckeBookmarks.forEach((bm) => {
        existingContent = bm.innerHTML;
        bm.innerHTML = content;
    });

    return existingContent;
}

// Could the clean-up steps in handlePageEditing's mainTask (removeCommentsFromEditableHtml and
// cleanUpNbsps) actually change this box? Both rewrite innerHTML only when they find something
// to fix, so this looks for the two things they look for, in the same place and the same way
// they look for them: their own searches are over innerHTML too, so this cannot miss an nbsp
// that cleanUpNbsps would go on to find, whatever the serializer does with U+00A0. It
// deliberately over-estimates - an nbsp that cleanUpNbsps would decide to keep still counts -
// because the only cost of a false yes is that we take a ckeditor bookmark we didn't need,
// which is what the code did unconditionally before. A false NO would be a bug: we'd lose the
// user's insertion point when one of them did rewrite the box.
// (exported for testing)
export function editableMightBeRewritten(editable: HTMLElement): boolean {
    const html = editable.innerHTML;
    return html.includes("<!--") || html.includes("&nbsp;");
}

// exported for testing
// Warning: if the current selection is inside the element we're fixing,
// and there are comments to remove, the selection will contract to an
// insertion point at the start.
export function removeCommentsFromEditableHtml(editable: HTMLElement) {
    // [\s\S] is a hack representing every character (including newline)
    const fixedHtml = editable.innerHTML.replace(/<!--[\s\S]*?-->/g, "");
    // This test makes it less likely we will move the selection. But you should still allow for
    // the possibility.
    if (fixedHtml !== editable.innerHTML) {
        editable.innerHTML = fixedHtml;
    }
}
