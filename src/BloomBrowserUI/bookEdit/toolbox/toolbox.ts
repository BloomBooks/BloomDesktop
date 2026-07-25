import $ from "jquery";
import axios from "axios";
import { get, postString, wrapAxios } from "../../utils/bloomApi";
import { hookupLinkHandler } from "../../utils/linkHandler";
import {
    ckeditableSelector,
    getPageIFrame,
    getPageIframeBody,
} from "../../utils/shared";
import { GameTool } from "./games/GameTool";
import { isLongPressEvaluating } from "../longPressShared";
import {
    callOnBlur,
    setExtraFunctionToHandleBlurTasks,
} from "../../utils/menuCloseOnBlur";
import {
    getToolboxReactAdapter,
    whenToolboxReactAdapterReady,
} from "./toolboxReactAdapter";
import {
    kSettingsToolId,
    kTalkingBookToolId,
    toCanonicalToolId,
    toEnabledSettingName,
    toPersistedToolName,
} from "./toolIds";
export { isLongPressEvaluating };
export { callOnBlur as registerMenuCloseOnBlur };

/**
 * The toolbox settings for the current book, as the server sends them (GET
 * /bloom/api/toolbox/settings; see ToolboxView.HandleSettings()). Apart from "current" and
 * "visibility", it has one "<toolId>State" property for each tool that has saved state in
 * this book; the value is whatever opaque string that tool chose to save, and only that tool
 * knows how to interpret it (e.g. settings["decodableReaderState"]).
 */
export interface IToolboxSettings {
    // The tool the book was last using, in the historical persisted spelling (e.g.
    // "talkingBookTool"). Missing or empty for a new book.
    current?: string;
    // "visible" if the toolbox was open in this book, otherwise an empty string.
    visibility?: string;
    // The per-tool state properties described above, keyed "<toolId>State".
    [stateKey: string]: string | undefined;
}

let savedSettings: IToolboxSettings = {};

let keypressTimer: ReturnType<typeof setTimeout> | null = null;

// This variable stores the canonical ids of all the enabled tools, so
// that the React toolbox settings can initially check the
// checkboxes that correspond to the enabled tools
let enabledToolIds = new Set<string>();

// Is the tool with this canonical id currently enabled?
export function isToolEnabledInToolbox(toolId: string): boolean {
    return enabledToolIds.has(toolId);
}

// a function to update the state of the checkboxes in the toolbox settings,
// whenever a tool is enabled and activated using setToolEnabledFromSettings(). This
// function starts out unimplemented, but is later implemented by SettingsToolControls.tsx
// when it gets mounted.
let changeToolboxSettingsState:
    | ((which: string, value: boolean) => void)
    | undefined;

export function setToolboxSettingsChangeHandler(
    handler: ((which: string, value: boolean) => void) | undefined,
): void {
    changeToolboxSettingsState = handler;
}

// Each tool implements this interface and adds an instance of its implementation to the
// list maintained here. The methods support the different things individual tools
// can be asked to do by the rest of the system. Everything the toolbox needs to know
// about a tool, including the metadata it shows in the tool's section header, comes from
// here (or is derived from id(); see toolIds.ts).
// See ToolboxView.cs class comment for a summary of how to add a new tool.
export interface ITool {
    // For tools that require a subscription. This will trigger an indicator communicating that this
    // featureName requires a subscription.
    readonly featureName?: string;
    // Gives the tool a chance to restore whatever it saved in the book's toolbox settings
    // (its own "<toolId>State" property, if any) before it is shown. Called each time the
    // tool becomes the current tool, so it also serves to make the tool's state track the
    // current book. The returned promise must resolve when the tool is ready to be shown.
    beginRestoreSettings(settings: IToolboxSettings): Promise<void>;
    configureElements(container: HTMLElement);
    showTool(); // called when a new tool is chosen, but not necessarily when a new page is displayed.
    hideTool(); // called when changing tools or hiding the toolbox.
    // Note, new implementations of updateMarkup may need to call EditableDivUtils.doCkEditorCleanup() like readerToolsModel.doMarkup() does.
    updateMarkup(); // called on most keypresses (but notably, not on arrow navigation, also not Ctrl+C). It is called on typing letters (obviously), Ctrl+X, Ctrl+V, Ctrl+Z, Ctrl+Y etc... or even just pressing and releasing Ctrl or Shift.
    // like updateMarkup, but expected to be async. Implement instead of updateMarkup if you need to use async functions.
    // Because it is async, it is not guaranteed that all the async processing will complete before another keystroke is received.
    // To guard against this, it should make no changes to the document; rather, it returns a function which will,
    // synchronously, make the changes. Toolbox will call this returned function iff no more keystrokes have been received.
    // Note, new implementations of updateMarkupAsync may need to implement something like cleanUpCkEditorHtml() in audioRecording.ts.
    updateMarkupAsync(): Promise<() => void>;
    isUpdateMarkupAsync(): boolean; // should return true if updateMarkupAsync should be called and awaited instead of updateMarkup.
    // called when a new page is displayed or tool is activated (called after showTool completes).
    // To guard against certain race conditions, we currently call this again after 600ms. Tools should
    // allow for this possibility and not repeat any work that was already done.
    newPageReady();
    detachFromPage(); // called when a page is going away AND before hideTool
    id(): string; // the canonical id, without trailing "Tool"!
    isAlwaysEnabled(): boolean;
    // If this is true, the tool may only be selected on pages that have data-tool-id matching this tool's id.
    requiresToolId(): boolean;

    // It should return the main content of the tool, which must be a single div.
    // ToolboxRoot renders the section header (label, icon, subscription badge) around it;
    // this method is however responsible to localize the content of the div.
    makeRootElement(): HTMLDivElement;
    // notifies the tool that an image has been changed on the page.
    // If the change only affects one image, it may be passed; otherwise, all should be fixed.
    imageUpdated(img: HTMLImageElement | undefined): void;
    // The URL of the icon to show in this tool's toolbox section header, e.g.
    // "/bloom/images/microphone-white.svg". Undefined for the few sections that don't
    // have an icon.
    iconPath(): string | undefined;
}

// Class that represents the whole toolbox. Gradually we will move more functionality in here.
export class ToolBox {
    public toolboxIsShowing() {
        return (<HTMLInputElement>(
            $(parent.window.document).find("#pure-toggle-right").get(0)
        )).checked;
    }
    public toggleToolbox() {
        (<HTMLInputElement>(
            $(parent.window.document).find("#pure-toggle-right").get(0)
        )).click();
    }
    private builtToolbox: boolean = false;
    /**
     * Adds or removes the tools that are only offered on pages that ask for them
     * (see ITool.requiresToolId()), according to this page's data-tool-id, and makes
     * the required tool current.
     */
    public adjustToolListForPage(page: HTMLElement) {
        let requiredToolId = page.getAttribute("data-tool-id");
        // Books made from the Leveled/Decodable Reader templates have pages that carry
        // data-tool-id="leveledReader" or "decodableReader". Unlike the Game tool, these
        // reader tools don't actually require a particular page type, and honoring the
        // attribute here would force the reader tool open and keep the book "stuck" to its
        // original type, preventing the user from switching to (and staying on) another
        // tool. So we ignore those values and leave the last tool shown (stored in the
        // book's metadata) as the current tool. (BL-16615)
        if (
            requiredToolId === "leveledReader" ||
            requiredToolId === "decodableReader"
        ) {
            requiredToolId = null;
        }
        newToolId = requiredToolId || undefined;

        // This function is the main task of adjustToolListForPage. It may have to be postponed
        // until we've finished otherwise setting up the toolbox.
        // It's possible there will be a tiny bit of flicker if the book opens on a page that
        // has a required tool as we first initialize the toolbox without that tool and then
        // add it. But this is fairly rare and I have not found it noticeable.
        const doAdjustment = () => {
            const adapter = getToolboxReactAdapter();
            if (!this.builtToolbox || !adapter) {
                setTimeout(doAdjustment, 100);
                return;
            }
            let toolsAdjusted = false;
            for (const tool of masterToolList) {
                if (!tool.requiresToolId()) {
                    continue;
                }
                // We may need to add or remove this tool.
                const haveTool = adapter.hasTool(tool.id());
                const wantTool = requiredToolId === tool.id();
                if (haveTool !== wantTool) {
                    // add or remove as needed. (Required tools don't have check boxes.)
                    showOrHideTool(tool.id(), wantTool);
                    toolsAdjusted = wantTool;
                }
            }
            // We haven't called showOrHideTool, so the active tool hasn't changed.
            // See the later comments on BL-14434 (after the first PR link).
            if (requiredToolId && !toolsAdjusted) {
                setCurrentTool(requiredToolId);
            }
        };
        doAdjustment();
    }
    public configureElementsForTools(container: HTMLElement) {
        for (let i = 0; i < masterToolList.length; i++) {
            masterToolList[i].configureElements(container);
        }
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
                    setTimeout(
                        () => handlePageEditing(maxPasteMarkupUpdateRetries),
                        0,
                    );
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

    public getTheOneGameTool(): GameTool | undefined {
        return GameTool.theOneDragActivityTool;
    }

    // Generally prefer to use the standalone function detachCurrentTool() instead of this method.
    // In some contexts where we want to detach, we may not be able to get the toolbox instance,
    // and that function has some fallback behavior in that case.
    public detachCurrentTool(): void {
        for (const task of this.doWhenClosingTool) {
            task();
        }
        this.doWhenClosingTool = [];
        if (currentTool && isToolInitialized(currentTool)) {
            currentTool.detachFromPage();
        }
    }
    // A list of tasks to do when the current tool is closed. This is currently used to
    // keep track of popups and dialogs that need to be closed when the tool goes away.
    // We could make each tool responsible for this in its own detachFromPage() method,
    // but I think it would make for some duplication, as well as some complexity for
    // components that are used by particular (or multiple) tools and would need to find
    // the right tool to notify. This gives us one place to track such cleanup tasks.
    // (Of course the popup may get closed before we move away from the page or tool, so
    // the task passed must be OK to call even after the popup is closed.)
    private doWhenClosingTool: (() => void)[] = [];
    public static addWhenClosingToolTask(task: () => void): void {
        // This is used to add a task that should be run when the current tool is closed.
        // It is used by the Talking Book tool to clean up the CkEditor markup.
        getTheOneToolbox().doWhenClosingTool.push(task);
    }

    public static registerTool(tool: ITool) {
        masterToolList.push(tool);
    }

    /**
     * The tools the book has enabled, as a comma-separated list of tool names. This is the
     * one place we ask; the answer drives which sections the toolbox offers.
     */
    private getEnabledTools() {
        // Using axios directly because we want the promise.
        return axios.get<string>("/bloom/api/toolbox/enabledTools");
    }

    // Called from document.ready, initializes the whole toolbox.
    public initialize(): void {
        // It seems (see BL-5330) that the toolbox code is loaded into the edit document as well as the
        // toolbox one. Nothing outside toolbox imports it directly, so it must be some indirect link.
        // It's important that this function is only hooked up to the real toolbox instance.
        $(parent.window.document).ready(() => {
            $(parent.window.document)
                .find("#pure-toggle-right")
                .change(function () {
                    showToolboxChanged(!this.checked);
                });
        });
        hookupLinkHandler();

        wrapAxios(
            this.getEnabledTools().then((enabledTools) => {
                // TODO: give each experimental tool its own setting once we have any
                // experimental tools again. Presumably use the tool id as the keyword in
                // the list of experimental features.
                // The names in this list come from the book's meta.json, so they may have
                // the historical "Tool" suffix; from here on we work in canonical ids.
                const toolsToLoad = enabledTools.data
                    .split(",")
                    .map((toolName: string) => toolName.trim())
                    .filter((toolName: string) => toolName.length > 0)
                    .map((toolName: string) => toCanonicalToolId(toolName));
                // remove any tools we don't know about. This might happen where settings were saved in a later version of Bloom.
                for (let i = toolsToLoad.length - 1; i >= 0; i--) {
                    if (
                        !masterToolList.some(
                            (mod) => mod.id() === toolsToLoad[i],
                        )
                    ) {
                        toolsToLoad.splice(i, 1);
                    }
                }

                enabledToolIds = new Set(toolsToLoad);

                for (let j = 0; j < masterToolList.length; j++) {
                    // add any tools we always show
                    if (
                        masterToolList[j].isAlwaysEnabled() &&
                        !toolsToLoad.includes(masterToolList[j].id())
                    ) {
                        toolsToLoad.push(masterToolList[j].id());
                    }
                }

                // The "More..." section, which is how the user enables the other tools,
                // is always offered.
                toolsToLoad.push(kSettingsToolId);
                const loadNextTool = () => {
                    if (toolsToLoad.length === 0) {
                        this.builtToolbox = true;
                        // loaded them all, now we can deal with settings.
                        restoreToolboxSettings();
                    } else {
                        // optimize: maybe we can overlap these?
                        const nextToolId = toolsToLoad.pop()!;
                        beginAddTool(nextToolId, false, () => loadNextTool());
                    }
                };
                // Adding the tools requires the toolbox UI, which mounts asynchronously.
                whenToolboxReactAdapterReady(() => loadNextTool());
            }),
        );
    }

    /**
     * Is the toolbox currently offering this tool (canonical id) a section? (Despite the
     * name, this does not mean the tool is the *current* tool; it never did.)
     */
    public isToolActive(toolId: string): boolean {
        return !!getToolboxReactAdapter()?.hasTool(toolId);
    }

    // Enables a tool (canonical id) from an in-page action, ensuring the toolbox is visible.
    public enableToolFromPage(toolId: string): void {
        if (!this.toolboxIsShowing()) {
            this.toggleToolbox();
        }
        setToolEnabledFromSettings(toolId, true);
    }

    /**
     * Makes the given tool (canonical id) the current tool, enabling it first if
     * necessary. Called in response to in-page actions, e.g. clicking a video placeholder
     * to get the Sign Language tool.
     */
    public activateToolFromId(toolId: string) {
        if (!getITool(toolId)) {
            // Every tool we know about is registered unconditionally, so this means the
            // caller asked for a tool that doesn't exist.
            console.error(`activateToolFromId: there is no tool "${toolId}".`);
            return;
        }
        // Making it visible first allows the simulated click to actually activate the tool.
        // We don't seem to get flicker seeing some other tool first, and if we do it after
        // the simulated click and the tool we're activating wasn't previously enabled,
        // it somehow ends up enabled but not active.
        const toolboxWasShowing = this.toolboxIsShowing();
        if (!toolboxWasShowing) {
            this.toggleToolbox();
        }

        // The tool may be present without being in enabledToolIds if it is a
        // required-for-this-page tool (see adjustToolListForPage).
        if (isToolEnabledInToolbox(toolId) || this.isToolActive(toolId)) {
            setCurrentTool(toolId);
        } else {
            // Genuinely disabled: enable it, which persists the state and updates
            // enabledToolIds, then activates it (showOrHideTool opens it by default).
            setToolEnabledFromSettings(toolId, true);
        }
    }

    public getCurrentTool() {
        return currentTool;
    }

    public setCurrentTool(toolId: string): void {
        setCurrentTool(toolId);
    }
}

const toolbox = new ToolBox();
setExtraFunctionToHandleBlurTasks(ToolBox.addWhenClosingToolTask);

export function getTheOneToolbox() {
    return toolbox;
}
export function getMasterToolList() {
    return masterToolList;
}

// Array of ITool objects, typically one for each tool. The code for each tool inserts an appropriate ITool
// into this array in order to interact with the overall toolbox code.
const masterToolList: ITool[] = [];
let currentTool: ITool | undefined = undefined;
let toolboxReactActivationHooked = false;

// This primarily calls the detachFromPage method of the current tool, if any.
// It also tries to find the current toolbox instance (in the right iframe, wherever it is called),
// and runs any cleanup tasks that have been registered for when closing the tool.
// It's important to get the right toolbox (in the right iframe) beacause that's the one
// that has the valid list of tasks to run when closing the tool.
function detachCurrentTool() {
    const toolbox = getTheOneToolbox();
    if (toolbox) {
        toolbox.detachCurrentTool();
    } else if (currentTool && isToolInitialized(currentTool)) {
        // If the toolbox is not available, we still may be able to detach the current tool.
        // This is what we used to do before we had some extra behavior in the toolbox.
        currentTool.detachFromPage();
    }
}

let newToolId: string | undefined = undefined;
export function getActiveToolId(): string | undefined {
    return newToolId ? newToolId : currentTool?.id();
}

// How long, after a tool is turned on in the "More..." settings section, we wait
// before adding/opening it. The open collapses the "More..." section, so we delay
// it just long enough for the user to see the checkbox they ticked. (BL-16501)
const kShowToolAfterEnableDelayMs = 300;

// Pending deferred "open this tool" timers, keyed by canonical tool id, so a later toggle
// of the same tool can cancel an open that hasn't fired yet.
// We deliberately don't clear this map on toolbox teardown/navigation: each timer
// is ~300ms and removes its own entry when it fires, so at most a couple of very
// short-lived entries ever exist and nothing can accumulate. (BL-16501)
const pendingShowToolTimeouts = new Map<
    string,
    ReturnType<typeof setTimeout>
>();

// modifies the enabledToolIds set, the saved active
// state of the tool in question (canonical id), and the presence of
// the tool in the toolbox, whenever the tool is checked
// or unchecked in the toolbox settings.
// deferShowToRevealCheckbox is set only by the "More..." settings checkboxes:
// when turning a tool on from there, opening it collapses the settings section,
// so we briefly delay the open (see below) to let the user see the checkbox they
// ticked. Other callers (e.g. activating a tool from an in-page action) leave it
// false so the tool opens immediately. (BL-16501)
export function setToolEnabledFromSettings(
    toolId: string,
    turnOn: boolean,
    deferShowToRevealCheckbox: boolean = false,
): void {
    if (turnOn) {
        enabledToolIds.add(toolId);
    } else {
        enabledToolIds.delete(toolId);
    }

    postString(
        "editView/saveToolboxSetting",
        "active\t" + toEnabledSettingName(toolId) + "\t" + (turnOn ? "1" : "0"),
    );

    if (changeToolboxSettingsState !== undefined) {
        changeToolboxSettingsState(toolId, turnOn);
    }

    // A pending deferred open (below) reflects an earlier state; this call
    // supersedes it, so cancel it. Without this, ticking a tool on and then off
    // again within the delay would let the stale timer re-add the disabled tool
    // (the disable runs synchronously and would otherwise be overtaken).
    const pendingTimeout = pendingShowToolTimeouts.get(toolId);
    if (pendingTimeout !== undefined) {
        clearTimeout(pendingTimeout);
        pendingShowToolTimeouts.delete(toolId);
    }

    if (turnOn && deferShowToRevealCheckbox) {
        // Turning a tool on adds it to the accordion and makes it the active
        // section, which collapses the "More..." settings section. If we do that
        // immediately, the "More..." section closes before the user perceives the
        // checkbox they just ticked. Briefly delay so the checkmark is visible
        // before the section collapses to reveal the newly-enabled tool. (BL-16501)
        const timeout = setTimeout(() => {
            pendingShowToolTimeouts.delete(toolId);
            // Guard against the tool having been turned off again during the delay.
            if (enabledToolIds.has(toolId)) {
                showOrHideTool(toolId, true);
            }
        }, kShowToolAfterEnableDelayMs);
        pendingShowToolTimeouts.set(toolId, timeout);
    } else {
        showOrHideTool(toolId, turnOn);
    }
}

function showOrHideTool(
    toolId: string,
    turnOn: boolean,
    openTool: boolean = true,
) {
    if (turnOn) {
        beginAddTool(toolId, openTool);
    } else {
        getToolboxReactAdapter()?.removeTool(toolId);
    }
}

export function restoreToolboxSettings() {
    get("toolbox/settings", (result) => {
        savedSettings = result.data;
        const pageFrame = getPageIFrame();
        const contentWin = pageFrame.contentWindow;
        if (contentWin && contentWin.document.readyState === "loading") {
            // We can't finish restoring settings until the main document is loaded, so arrange to call the next stage when it is.
            $(contentWin.document).ready((_e) =>
                restoreToolboxSettingsWhenPageReady(result.data),
            );
            return;
        }
        restoreToolboxSettingsWhenPageReady(result.data); // not loading, we can proceed immediately.
    });
}

export function applyToolboxStateToUpdatedPage() {
    get("toolbox/settings", (result) => {
        savedSettings = result.data;
        // savedSettings["current"] is always set to the last active tool for the book,
        // except for new books where it is null. In that case, the default value
        // should be the talking book tool.  (BL-16026)
        const currentFromBook = toCanonicalToolId(
            (savedSettings && savedSettings["current"]) || kTalkingBookToolId,
        );
        const currentInToolbox = currentTool ? currentTool.id() : "";
        const shouldBeVisible = !!(
            savedSettings && savedSettings["visibility"]
        );
        const isVisible = toolbox.toolboxIsShowing();

        // When switching books, sync visibility/current tool first.
        if (
            currentFromBook !== currentInToolbox ||
            shouldBeVisible !== isVisible
        ) {
            restoreToolboxSettingsWhenPageReady(savedSettings);
            return;
        }

        if (currentTool && toolbox.toolboxIsShowing()) {
            doWhenPageReady(() => {
                const activeTool = currentTool;
                if (activeTool && isToolInitialized(activeTool)) {
                    activeTool.beginRestoreSettings(savedSettings).then(() => {
                        if (currentTool !== activeTool) {
                            return;
                        }

                        // Re-run tool UI setup on page/book switches. Some tools
                        // (for example reader toggle controls) are initialized in showTool().
                        Promise.resolve(activeTool.showTool()).then(() => {
                            if (
                                currentTool === activeTool &&
                                isToolInitialized(activeTool)
                            ) {
                                activeTool.newPageReady();
                                scheduleDelayedNewPageReady(activeTool);
                            }
                        });
                    });
                    // We used to call updateMarkup() here
                    // Now we don't because it would mess up the Talking Book Tool
                    // if you really need it, add call to updateMarkup to currentTool's implementation of newPageReady.
                }
            });
        }
    });
}

function scheduleDelayedNewPageReady(tool: ITool): void {
    window.setTimeout(() => {
        if (
            currentTool !== tool ||
            !toolbox.toolboxIsShowing() ||
            !isToolInitialized(tool)
        ) {
            return;
        }

        Promise.resolve(tool.newPageReady());
    }, 600);
}

function doWhenPageReady(action: () => void) {
    const page = getPageIframeBody();
    if (!page || !getPageIFrame()) {
        // Somehow, despite firing this function when the document is supposedly ready,
        // it may not really be ready when this is first called. If it doesn't even have a body yet,
        // we need to try again later.
        setTimeout(() => doWhenPageReady(action), 100);
        return;
    }
    doWhenCkEditorReady(action, page);
}

// Do this action ONCE when all ckeditors are ready.
// I'm not absolutely sure all the care to do it only once is necessary...the bug
// I was trying to fix turned out to be caused by multiple calls to doWhenCkEditorReady...
// but it seems a precaution worth keeping.
function doWhenCkEditorReady(action: () => void, page: HTMLElement) {
    const removers = [];
    doWhenCkEditorReadyCore(
        {
            removers: removers,
            done: false,
            action: action,
        },
        page,
    );
}

function doWhenCkEditorReadyCore(
    arg: {
        // The initial call to this function passes an empty array of removers. When we make a
        // delayed recursive call, the on() call returns a remover object that we add to the array.
        // When we finally do the action, we call removeListener() on each of them to try to prevent
        // future callbacks.
        removers: Array<{ removeListener: () => void }>;
        done: boolean;
        action: () => void;
    },
    page: HTMLElement,
): void {
    const contentWindow = getPageIFrame().contentWindow as
        | (Window & { CKEDITOR?: typeof CKEDITOR })
        | null;
    if (contentWindow?.CKEDITOR) {
        const editorInstances = contentWindow.CKEDITOR.instances;
        // Somewhere in the process of initializing ckeditor, it resets content to what it was initially.
        // This wipes out (at least) our page initialization.
        // To prevent this we hold our initialization until CKEditor has done initializing.
        // If any instance on the page (e.g., one per div) is not ready, wait until all are.
        // Enhance: this logic is roughly duplicated in StyleEditor.ts function AttachToBox.
        // There may be some way to refactor it into a common place, but I don't know where.
        // (The instances property leads to an object in which each property is an instance of CkEditor)
        let gotOne = false;
        for (const property in editorInstances) {
            const instance = editorInstances[property] as CKEDITOR.editor & {
                instanceReady?: boolean;
                on: (
                    event: string,
                    callback: (eventInfo: unknown) => void,
                ) => { removeListener: () => void } | void;
            };
            gotOne = true;
            if (!instance.instanceReady) {
                const remover = instance.on("instanceReady", (_e) => {
                    doWhenCkEditorReadyCore(arg, page);
                });
                const typedRemover = remover as
                    | { removeListener: () => void }
                    | undefined;
                if (
                    typedRemover &&
                    typeof typedRemover.removeListener === "function"
                ) {
                    arg.removers.push(typedRemover);
                }
                return;
            }
        }
        if (!gotOne) {
            if (page.querySelector(ckeditableSelector)) {
                // If any editable divs exist, call us again once the page gets set up with ckeditor.
                // See BL-12381.
                const ckEditorGlobal =
                    contentWindow.CKEDITOR as typeof CKEDITOR & {
                        on?: (
                            event: string,
                            callback: (eventInfo: unknown) => void,
                        ) => { removeListener: () => void } | void;
                    };
                const remover = ckEditorGlobal.on?.("instanceReady", (_e) => {
                    doWhenCkEditorReadyCore(arg, page);
                });
                if (remover && typeof remover.removeListener === "function") {
                    arg.removers.push(remover);
                }
                return;
            }
        }
    }
    // OK, all CKEditors are ready (or page doesn't use it), we can finally do the action.
    if (!arg.done) {
        // We are the first call-back to find all ready! Any other editors invoking this should be ignored.
        arg.done = true; // ensures action only done once
        arg.removers.map((r) => r.removeListener()); // try to prevent future callbacks for this action
        arg.action();
    }
}

function restoreToolboxSettingsWhenPageReady(settings: IToolboxSettings) {
    doWhenPageReady(() => {
        // OK, CKEditor is done (or page doesn't use it), we can finally do the real initialization.
        const opts = settings;
        // currentTool is always set except for new books. For new books, it is undefined and we want
        // to treat that the same as if it were set to the talking book tool so that the tool will
        // display the first time the user opens the toolbox. (BL-16026)
        const currentTool = opts["current"] || kTalkingBookToolId;
        const shouldBeVisible = !!opts["visibility"];

        if (toolbox.toolboxIsShowing() !== shouldBeVisible) {
            toolbox.toggleToolbox();
        }

        // Before we set stage/level, as it initializes them to 1.
        setCurrentTool(currentTool);

        // Note: the bulk of restoring the settings (everything but which if any tool is active)
        // is done when a tool becomes current.
    });
}

// Remove any markup the toolbox is inserting. Called by a RunJavaScript() in EditingView
// before saving the page.
export function removeToolboxMarkup() {
    detachCurrentTool();
}

/**
 * Called when the toolbox UI reports that a different section is now the active one.
 * newToolId is a canonical tool id (the toolbox UI only ever reports tools it is offering,
 * and it was told about them by their canonical ids).
 */
function switchTool(newToolId: string): void {
    // Have Bloom remember which tool is active. (Might be none.) The book's meta.json
    // has always stored this with the historical "Tool" suffix.
    postString(
        "editView/saveToolboxSetting",
        "current\t" + toPersistedToolName(newToolId),
    );
    let newTool: ITool | null = null;
    if (newToolId) {
        newTool =
            masterToolList.find((tool) => tool.id() === newToolId) ?? null;
    }
    const canActivateNewTool = !!newTool && isToolInitialized(newTool);
    const shouldSwitchAwayFromCurrent =
        currentTool !== newTool || (!!newTool && !canActivateNewTool);

    if (shouldSwitchAwayFromCurrent) {
        if (currentTool && isToolInitialized(currentTool)) {
            detachCurrentTool();
            currentTool.hideTool();
        }
        if (canActivateNewTool && newTool) {
            activateTool(newTool);
        }
        // Without recording that currentTool isn't defined, then returning from
        // More... to the same tool doesn't activate that tool.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-6720.
        currentTool = canActivateNewTool && newTool ? newTool : undefined;
    }
    newToolId = undefined;
}

function activateTool(newTool: ITool) {
    if (newTool && toolbox.toolboxIsShowing()) {
        if (!isToolInitialized(newTool)) {
            return;
        }
        // Always re-restore settings so tool state tracks the current book.
        newTool.beginRestoreSettings(savedSettings).then(() => {
            activateToolInternalAsync(newTool);
        });
    }
}

// Does the toolbox have a section for this tool? Only then does it have somewhere to
// display itself and does it make sense to run its lifecycle methods.
function isToolInitialized(tool: ITool): boolean {
    return toolbox.isToolActive(tool.id());
}

async function activateToolInternalAsync(newTool: ITool): Promise<void> {
    // Await it so that we can guarantee that newPageReady() happens after showTool.
    await newTool.showTool();

    postString("logger/writeEvent", `Toolbox activated: ${newTool.id()}`);

    // Note: Allowed to begin some async work too, and we will await its result.
    // (This apparently solves the single flash mentioned in BL-10471.)
    await newTool.newPageReady();
    scheduleDelayedNewPageReady(newTool);
}

/**
 * Attempts to make the given tool the current one (normally the tool the book was last
 * using). If the toolbox isn't offering that tool, falls back to the first tool it does
 * offer. Passing an empty id also means "whatever tool is first".
 * The id may arrive in either spelling, because one caller passes the book's saved
 * "current" tool name straight from meta.json.
 */
function setCurrentTool(toolId: string) {
    toolId = toCanonicalToolId(toolId);

    const adapter = getToolboxReactAdapter();
    if (!adapter) {
        // ToolboxRoot has not mounted yet, so there is no toolbox UI to activate
        // anything in. We don't expect this: see getToolboxReactAdapter().
        return;
    }

    if (!toolboxReactActivationHooked) {
        adapter.onActiveToolChanged((newToolId: string) => {
            switchTool(newToolId);
        });
        toolboxReactActivationHooked = true;
    }

    // NOTE: the More (settings) section cannot be the "currentTool", so getFirstToolId()
    // never returns it.
    if (!toolId) {
        toolId = adapter.getFirstToolId() ?? "";
    }

    if (toolId) {
        const tool = masterToolList.find(
            (possibleTool) => possibleTool.id() === toolId,
        );
        if (tool && !isToolInitialized(tool)) {
            // The tool we were asked for isn't in the toolbox (e.g., it was disabled
            // since we saved the setting), so fall back to whatever is first.
            toolId = adapter.getFirstToolId() ?? "";
        }
    }

    if (toolId) {
        adapter.setActiveToolByToolId(toolId);
    }
}

// Parameter 'toolId' may be spelled with or without the 'Tool' suffix, since it may have
// come from persisted data.
// Returns undefined if we know of no such tool, e.g. because the book's settings were
// saved by a version of Bloom that had a tool this one doesn't.
function getITool(toolId: string): ITool {
    const canonicalToolId = toCanonicalToolId(toolId);
    return masterToolList.find((tool) => tool.id() === canonicalToolId)!;
}

/**
 * Tells the toolbox UI to offer a section for this tool, and optionally to open it.
 * These tools are the tools enabled by the user, tools that are always enabled
 * (like the talking book tool), and the settings ("More...") tool.
 */
function beginAddTool(
    toolId: string,
    openTool: boolean,
    whenLoaded?: () => void,
): void {
    const tool = getITool(toolId);
    if (!tool) {
        console.error(
            `Tool ${toolId} not found, assuming that was from a different version of Bloom.`,
        );
        return;
    }

    const adapter = getToolboxReactAdapter();
    // Adding a tool that is already there does nothing, so it is safe to do this
    // whether or not the toolbox is already offering it.
    adapter?.addTool(tool.id());

    if (openTool && toolbox.toolboxIsShowing()) {
        adapter?.setActiveToolByToolId(tool.id());
    }

    if (whenLoaded) {
        whenLoaded();
    }
}

let keydownEventCounter = 0;
const retryDelayForPasteMarkupUpdateInMilliseconds = 100;
const maxPasteMarkupUpdateRetries = 3;

export function scheduleMarkupUpdateAfterPaste(): void {
    // AI thinks we might need this to "allow the DOM to settle" even before we do the
    // little bit that handlePageEditing does before setting up its own delay.
    // I could not understand its explanation and am not convinced we need this.
    // However, I'm trying to fix a race condition that results in a problem
    // that is hard to reproduce reliably. I'd rather have a timeout that we don't
    // need than have the markup occasionally not update, let alone somehow have
    // the markup update somehow mess up the paste. So I decided to leave it in.
    setTimeout(() => handlePageEditing(maxPasteMarkupUpdateRetries), 0);
}

// Handle edits to the page: mainly triggered by key up, but also by paste.
// For various reasons a single paste may cause this to get called several times, but the 500ms
// delay should prevent us from doing the markup more than once per paste.
// Similarly, since updating the markup is fairly costly, it's good not to do it on every keystroke
// while the user is typing rapidly.
function handlePageEditing(
    remainingRetriesForInvalidSelectionState: number = 0,
): void {
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
    if (keypressTimer) clearTimeout(keypressTimer);
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
    if (window?.top?.[isLongPressEvaluating]) {
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
        const selectionStateIsInvalidForMarkup =
            !active ||
            (selection &&
                (selection.rangeCount > 1 ||
                    (selection.rangeCount === 1 &&
                        !selection.getRangeAt(0).collapsed)));

        if (selectionStateIsInvalidForMarkup) {
            // Copilot suggested that there are some cases after a paste where the selection
            // is only temporarily a range, so it's worth trying again a few times.
            // This callback can also be canceled by a new keypress etc.
            if (remainingRetries > 0) {
                keypressTimer = setTimeout(
                    () => mainTask(remainingRetries - 1),
                    retryDelayForPasteMarkupUpdateInMilliseconds,
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
        if (window.top[isLongPressEvaluating]) {
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
                // there is also createBookmarks2(), which avoids actually inserting anything. That has the
                // advantage that changing a character in the middle of a word will allow the entire word to
                // be evaluated by the markup routine. However, testing shows that the cursor then doesn't
                // actually go back to where it was: it gets shifted to the right.
                let bookmarks = ckeditorSelection.createBookmarks(true);

                // For some reason, we have cases, mostly (always?) on paste, where
                // ckeditor is inserting tons of comments which are messing with our parsing
                // See http://issues.bloomlibrary.org/youtrack/issue/BL-4775
                removeCommentsFromEditableHtml(editableDiv);

                // If there's no tool active, we don't need to update the markup.
                const activeTool =
                    currentTool && toolbox.toolboxIsShowing()
                        ? currentTool
                        : undefined;
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
                // Not covered by a unit test: the pieces are (cleanUpNbsps in toolboxSpec.ts,
                // which now checks that it leaves the text nodes alone when it has nothing to
                // convert; the highlight primitives in textHighlightManagerSpec.ts), but the
                // *ordering* between them is only exercised by running handlePageEditing, and
                // that needs a live ckeditor instance on the editable div plus the parent
                // window's "page" iframe and a real Selection - none of which we can stand up in
                // jsdom. So if you reorder anything in here, test it by typing in a Leveled
                // Reader book and watching the over-long sentences stay highlighted.
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
                ckeditorOfThisBox.getSelection().selectBookmarks(bookmarks);
            }
        }
        // clear this value to prevent unnecessary calls to clearTimeout() for timeouts that have already expired.
        keypressTimer = null;
    };
    keypressTimer = setTimeout(
        () => mainTask(remainingRetriesForInvalidSelectionState),
        500,
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

function showToolboxChanged(wasShowing: boolean): void {
    postString(
        "editView/saveToolboxSetting",
        "visibility\t" + (wasShowing ? "" : "visible"),
    );
    if (currentTool) {
        if (wasShowing) {
            detachCurrentTool();
            currentTool.hideTool();
            postString(
                "logger/writeEvent",
                `Toolbox deactivating: ${currentTool.id()}`,
            );
        } else {
            activateTool(currentTool);
        }
    } else {
        // starting up for the very first time in this book...no tool is current,
        // so select and properly initialize the first one. If the toolbox somehow has
        // no tool sections at all, fall back to the talking book tool, which is always
        // enabled. (This should never happen; we're just being defensive.)
        const adapter = getToolboxReactAdapter();
        adapter?.setActiveToolByToolId(
            adapter.getFirstToolId() ?? kTalkingBookToolId,
        );
    }
}
