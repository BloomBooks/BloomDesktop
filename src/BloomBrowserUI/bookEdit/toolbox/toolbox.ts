import $ from "jquery";
import axios from "axios";
// Type-only: ITool.renderPanel() returns a React node, but nothing in this module
// actually uses React at runtime.
import type * as React from "react";
import { get, postString, wrapAxios } from "../../utils/bloomApi";
import { hookupLinkHandler } from "../../utils/linkHandler";
import {
    ckeditableSelector,
    getPageIFrame,
    getPageIframeBody,
} from "../../utils/shared";
import { GameTool } from "./games/GameTool";
import { isLongPressEvaluating } from "../longPressShared";
import { configurePageEditingHandlers } from "./pageEditingMarkup";
import {
    callOnBlur,
    setExtraFunctionToHandleBlurTasks,
} from "../../utils/menuCloseOnBlur";
import {
    getExpandedToolId,
    getFirstOfferedToolId,
    getToolboxUiState,
    isToolEnabled,
    isToolOffered,
    notePageReady,
    offerTool,
    setExpandedTool,
    setEnabledTools,
    setToolEnabled,
    setToolboxVisible,
    subscribeToToolboxUiState,
    withdrawTool,
} from "./toolboxState";
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

/**
 * The book's toolbox settings as last fetched from the server. A tool is handed these when
 * it starts running, so that it can restore whatever it saved in this book; see
 * ITool.beginRestoreSettings and useToolLifecycle.ts.
 */
export function getSavedToolboxSettings(): IToolboxSettings {
    return savedSettings;
}

// Each tool implements this interface and adds an instance of its implementation to the
// list maintained here. The methods support the different things individual tools
// can be asked to do by the rest of the system. Everything the toolbox needs to know
// about a tool, including the metadata it shows in the tool's header, comes from
// here (or is derived from id(); see toolIds.ts).
// See ToolboxView.cs class comment for a summary of how to add a new tool.
//
// The lifecycle methods (beginRestoreSettings, showTool, newPageReady, detachFromPage,
// hideTool) are not called from here. They follow from what the toolbox state store says:
// the accordion ToolboxRoot renders for a tool runs them from an effect keyed on whether
// that tool is running -- expanded, with the toolbox showing -- and on which page. See
// useToolLifecycle.ts. The rest are called directly, from the code that raises them:
// updateMarkup on keystrokes (pageEditingMarkup.ts), configureElements on page setup (for
// every registered tool, expanded or not), imageUpdated by the page frame.
export interface ITool {
    // For tools that require a subscription. This will trigger an indicator communicating that this
    // featureName requires a subscription.
    readonly featureName?: string;
    // Gives the tool a chance to restore whatever it saved in the book's toolbox settings
    // (its own "<toolId>State" property, if any) before it is shown. Called each time the
    // tool starts running, so it also serves to make the tool's state track the
    // current book. The returned promise must resolve when the tool is ready to be shown.
    beginRestoreSettings(settings: IToolboxSettings): Promise<void>;
    configureElements(container: HTMLElement);
    // Called when the tool starts running -- it has become the expanded tool of a showing
    // toolbox, or it was already expanded and a new page arrived. Expanding this tool's own
    // accordion section is not by itself what triggers it; starting to run is.
    showTool();
    // The exact inverse: called when the tool stops running, whether because another tool
    // took over, the toolbox was hidden, or it stopped being offered. One flag in
    // useToolLifecycle.ts drives both.
    hideTool();
    // Note, new implementations of updateMarkup should read the boxes rather than rewrite them, as readerToolsModel.doMarkup() does.
    // A rewrite replaces the box's nodes, which moves the caret and invalidates highlight Ranges over the text; if one is
    // unavoidable, see EditableDivUtils.doCkEditorCleanup() for how to keep CKEditor's filling character out of what it writes.
    // The markup pass (pageEditingMarkup.ts) records the caret only around an async tool's markup, so a tool whose markup
    // must rewrite a box implements updateMarkupAsync and returns true from isUpdateMarkupAsync.
    updateMarkup(); // called on most keypresses (but notably, not on arrow navigation, also not Ctrl+C). It is called on typing letters (obviously), Ctrl+X, Ctrl+V, Ctrl+Z, Ctrl+Y etc... or even just pressing and releasing Ctrl or Shift.
    // like updateMarkup, but expected to be async. Implement instead of updateMarkup if you need to use async functions.
    // Because it is async, it is not guaranteed that all the async processing will complete before another keystroke is received.
    // To guard against this, it should make no changes to the document; rather, it returns a function which will,
    // synchronously, make the changes. Toolbox will call this returned function iff no more keystrokes have been received.
    // Note, new implementations of updateMarkupAsync may need to implement something like cleanUpCkEditorHtml() in audioRecording.ts.
    updateMarkupAsync(): Promise<() => void>;
    isUpdateMarkupAsync(): boolean; // should return true if updateMarkupAsync should be called and awaited instead of updateMarkup.
    // called when a new page is displayed or the tool starts running (called after showTool completes).
    // To guard against certain race conditions, we currently call this again after 600ms. Tools should
    // allow for this possibility and not repeat any work that was already done.
    newPageReady();
    detachFromPage(); // called when a page is going away AND before hideTool
    id(): string; // the canonical id, without trailing "Tool"!
    isAlwaysEnabled(): boolean;
    // If this is true, the tool may only be selected on pages that have data-tool-id matching this tool's id.
    requiresToolId(): boolean;

    // Renders this tool's panel. ToolboxRoot renders it inside the tool's accordion, in
    // the toolbox's single React tree, so context (e.g. the MUI theme) reaches it normally.
    // It should return the main content of the tool, which must be a single React element
    // -- a React node describing the UI, never a DOM element; nothing here creates or hands
    // back real HTML. (ToolboxRoot sizes that element to fill the space below the header.)
    // ToolboxRoot renders the tool's header (label, icon, subscription badge) around it;
    // this method is however responsible to localize the content of the panel.
    renderPanel(): React.ReactNode;
    // notifies the tool that an image has been changed on the page.
    // If the change only affects one image, it may be passed; otherwise, all should be fixed.
    imageUpdated(img: HTMLImageElement | undefined): void;
    // The URL of the icon to show in this tool's header in the toolbox, e.g.
    // "/bloom/images/microphone-white.svg". Undefined for the few tools that don't
    // have an icon.
    iconPath(): string | undefined;
}

// The whole toolbox, as the rest of Bloom's frames see it. Functionality moves *out* of here
// into React (ToolboxRoot.tsx and the store); what stays is what has to run in this frame,
// because this is the cross-frame API the page and the shell call through window.toolboxBundle.
export class ToolBox {
    // The sidebar is shown and hidden by a checkbox in the workspace frame, not by us, so
    // this is the truth about whether the toolbox is showing. (It is mirrored into the
    // toolbox state store, which is how the tools' lifecycle follows it; see
    // syncToolboxVisibilityFromDom.) Returns false rather than throwing if there is no such
    // checkbox, which is the case in unit tests and other hosts of the toolbox UI.
    public toolboxIsShowing(): boolean {
        const toggle = $(parent.window.document)
            .find("#pure-toggle-right")
            .get(0) as HTMLInputElement | undefined;
        return !!toggle?.checked;
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
        // attribute here would force the reader tool expanded and keep the book "stuck" to its
        // original type, preventing the user from switching to (and staying on) another
        // tool. So we ignore those values and leave the last tool shown (stored in the
        // book's metadata) as the expanded tool. (BL-16615)
        if (
            requiredToolId === "leveledReader" ||
            requiredToolId === "decodableReader"
        ) {
            requiredToolId = null;
        }
        toolThePageRequires = requiredToolId || undefined;
        expandedToolWhenThePageAsked = getExpandedToolId();
        // This function is the main task of adjustToolListForPage. It may have to be postponed
        // until we've finished otherwise setting up the toolbox.
        // It's possible there will be a tiny bit of flicker if the book opens on a page that
        // has a required tool as we first initialize the toolbox without that tool and then
        // add it. But this is fairly rare and I have not found it noticeable.
        const doAdjustment = () => {
            if (!this.builtToolbox) {
                setTimeout(doAdjustment, 100);
                return;
            }
            let toolsAdjusted = false;
            for (const tool of masterToolList) {
                if (!tool.requiresToolId()) {
                    continue;
                }
                // We may need to add or remove this tool.
                const haveTool = isToolOffered(tool.id());
                const wantTool = requiredToolId === tool.id();
                if (haveTool !== wantTool) {
                    // add or remove as needed. (Required tools don't have check boxes.)
                    offerOrWithdrawTool(tool.id(), wantTool);
                    toolsAdjusted = toolsAdjusted || wantTool;
                }
            }
            // Offering the page's tool just now also expanded it (offerToolAndMaybeExpand).
            // Otherwise it still has to be expanded here: either it was already on offer,
            // or this pass only withdrew tools -- toolsAdjusted records offers, not
            // withdrawals, because withdrawing moves the expanded tool on by itself.
            // See the later comments on BL-14434 (after the first PR link).
            if (requiredToolId && !toolsAdjusted) {
                expandToolOrFirstOffered(requiredToolId);
            }
        };
        doAdjustment();
    }
    public configureElementsForTools(container: HTMLElement) {
        for (let i = 0; i < masterToolList.length; i++) {
            masterToolList[i].configureElements(container);
        }
        configurePageEditingHandlers(container, this);
    }

    public getTheOneGameTool(): GameTool | undefined {
        return GameTool.theOneDragActivityTool;
    }

    // Generally prefer to use the standalone function detachExpandedTool() instead of this method.
    // In some contexts where we want to detach, we may not be able to get the toolbox instance,
    // and that function has some fallback behavior in that case.
    public detachExpandedTool(): void {
        this.runTasksForClosingTool();
        findExpandedTool()?.detachFromPage();
    }
    // Runs (once) whatever was registered with addWhenClosingToolTask(). Called both by the
    // explicit detach above and by the tool's own lifecycle cleanup (useToolLifecycle.ts).
    public runTasksForClosingTool(): void {
        for (const task of this.doWhenClosingTool) {
            task();
        }
        this.doWhenClosingTool = [];
    }
    // A list of tasks to do when the running tool stops. This is currently used to
    // keep track of popups and dialogs that need to be closed when the tool goes away.
    // We could make each tool responsible for this in its own detachFromPage() method,
    // but I think it would make for some duplication, as well as some complexity for
    // components that are used by particular (or multiple) tools and would need to find
    // the right tool to notify. This gives us one place to track such cleanup tasks.
    // (Of course the popup may get closed before we move away from the page or tool, so
    // the task passed must be OK to call even after the popup is closed.)
    private doWhenClosingTool: (() => void)[] = [];
    public static addWhenClosingToolTask(task: () => void): void {
        // This is used to add a task that should be run when the running tool stops.
        // It is used by the Talking Book tool to clean up the CkEditor markup.
        getTheOneToolbox().doWhenClosingTool.push(task);
    }

    public static registerTool(tool: ITool) {
        masterToolList.push(tool);
    }

    /**
     * The tools the book has enabled, as a comma-separated list of tool names. This is the
     * one place we ask; the answer drives which tools the toolbox offers.
     */
    private getEnabledTools() {
        // Using axios directly because we want the promise.
        return axios.get<string>("/bloom/api/toolbox/enabledTools");
    }

    // Called from document.ready, initializes the whole toolbox.
    public initialize(): void {
        // From here on, whenever a different tool becomes the expanded one, have Bloom
        // remember it. That is all we do: which tool is *running* follows from the store
        // itself (see toolboxState.ts), and running it -- restoring its
        // settings, showing it, telling it the page is ready, detaching and hiding
        // whatever it replaced -- follows from that; see useToolLifecycle.ts.
        subscribeToToolboxUiState(() => {
            persistExpandedToolIfItChanged();
            forgetPageToolRequestIfTheToolboxHasMovedOn();
        });

        // It seems (see BL-5330) that the toolbox code is loaded into the edit document as well as the
        // toolbox one. Nothing outside toolbox imports it directly, so it must be some indirect link.
        // It's important that this function is only hooked up to the real toolbox instance.
        $(parent.window.document).ready(() => {
            $(parent.window.document)
                .find("#pure-toggle-right")
                .change(function () {
                    showToolboxChanged(this.checked);
                });
            // The handler above fires on every later toggle, but nothing announces the state
            // the checkbox is already in, so read it once now.
            syncToolboxVisibilityFromDom();
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

                setEnabledTools(toolsToLoad);

                for (let j = 0; j < masterToolList.length; j++) {
                    // add any tools we always show
                    if (
                        masterToolList[j].isAlwaysEnabled() &&
                        !toolsToLoad.includes(masterToolList[j].id())
                    ) {
                        toolsToLoad.push(masterToolList[j].id());
                    }
                }

                // The "More..." tool, which is how the user enables the other tools,
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
                        offerToolAndMaybeExpand(nextToolId, false, () =>
                            loadNextTool(),
                        );
                    }
                };
                loadNextTool();
            }),
        );
    }

    // Enables a tool (canonical id) from an in-page action, ensuring the toolbox is visible.
    public enableToolFromPage(toolId: string): void {
        if (!this.toolboxIsShowing()) {
            this.toggleToolbox();
        }
        applyToolEnabledChange(toolId, true);
    }

    /**
     * Expands the given tool (canonical id), enabling it first if
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
        // Show the toolbox first: offerToolAndMaybeExpand() only expands a tool while the
        // toolbox is showing, so showing it afterwards would leave the tool enabled and
        // offered but collapsed. We don't seem to get flicker seeing some other tool first.
        const toolboxWasShowing = this.toolboxIsShowing();
        if (!toolboxWasShowing) {
            this.toggleToolbox();
        }

        // The tool may be present without being enabled if it is a
        // required-for-this-page tool (see adjustToolListForPage).
        if (isToolEnabled(toolId) || isToolOffered(toolId)) {
            expandToolOrFirstOffered(toolId);
        } else {
            // Genuinely disabled: enable it, which persists the state, records it in the
            // store, and offers it -- and offering expands it, the toolbox now being shown.
            applyToolEnabledChange(toolId, true);
        }
    }

    /** The EXPANDED tool: the one the user has chosen. See toolboxState.ts. */
    public getExpandedTool(): ITool | undefined {
        return findExpandedTool();
    }

    /**
     * Which tool this page belongs to, as getEffectiveToolId() answers it -- the tool a
     * loading page requires if it has said so, otherwise the expanded tool.
     *
     * On the instance so that the e2e suite can ask: it decides whether Talking Book may put
     * its highlight and audio on a page that belongs to the Game tool (BL-14434), and the page
     * looks the same either way, so no test can read it from the DOM. Nothing in Bloom calls
     * this; the module function is what production code uses.
     */
    public getEffectiveToolIdForTests(): string | undefined {
        return getEffectiveToolId();
    }

    /**
     * The tool that is RUNNING: the expanded one, while the toolbox is showing. This is what
     * code outside the toolbox usually wants -- a tool left expanded behind a hidden toolbox has
     * been detached and hidden, so it should not be marked up, played, or told about images.
     * See toolboxState.ts for the vocabulary.
     */
    public getRunningTool(): ITool | undefined {
        return this.toolboxIsShowing() ? findExpandedTool() : undefined;
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

/**
 * The expanded tool, or undefined if none is. Note that it is not necessarily *running*:
 * hiding the toolbox stops a tool running without collapsing it.
 * toolboxState.ts defines both words.
 */
function findExpandedTool(): ITool | undefined {
    const expandedToolId = getExpandedToolId();
    if (!expandedToolId) {
        return undefined;
    }
    return masterToolList.find((tool) => tool.id() === expandedToolId);
}

/**
 * Runs whatever was registered with ToolBox.addWhenClosingToolTask(). Exported for the
 * running tool's lifecycle cleanup (useToolLifecycle.ts), which is where a tool that stops
 * running gets closed down. It goes through the toolbox instance so that the tasks run are
 * the ones registered with the toolbox in this iframe.
 */
export function runTasksForClosingTool(): void {
    getTheOneToolbox()?.runTasksForClosingTool();
}

// This primarily calls the detachFromPage method of the expanded tool, if any.
// It also tries to find the current toolbox instance (in the right iframe, wherever it is called),
// and runs any cleanup tasks that have been registered for when closing the tool.
// It's important to get the right toolbox (in the right iframe) because that's the one
// that has the valid list of tasks to run when closing the tool.
function detachExpandedTool() {
    const toolbox = getTheOneToolbox();
    if (toolbox) {
        toolbox.detachExpandedTool();
    } else if (findExpandedTool()) {
        // If the toolbox is not available, we still may be able to detach the expanded tool.
        // This is what we used to do before we had some extra behavior in the toolbox.
        findExpandedTool()?.detachFromPage();
    }
}

// Mirrors the sidebar's real showing/hidden state into the toolbox state store, which is
// what makes the expanded tool start or stop running. Everything that shows or hides the
// toolbox goes through the workspace's checkbox, so showToolboxChanged() normally keeps
// this up to date; the other callers are for the times we may never have been told (the
// state it started in) or may have missed it (a new page or book).
function syncToolboxVisibilityFromDom(): void {
    setToolboxVisible(toolbox.toolboxIsShowing());
}

// The tool a page that is still loading says it requires, until the toolbox has caught up
// and expanded it. Not a second opinion about what is running -- the store owns
// that -- but an answer to "which tool is this page for?", which can be known before the
// toolbox has been told to offer it.
//
// It is what keeps the Talking Book tool from laying its highlight and sound over a page
// that is arriving for some other tool: the page's own tool is announced here first, so
// getEffectiveToolId() stops naming the outgoing tool before the switch completes. Without it
// the asynchronous calls during page loading race and the wrong tool gets newPageReady.
// See BL-14434, and doesEffectiveToolPlayAudio() in audioRecording.ts.
let toolThePageRequires: string | undefined = undefined;

// Which tool was expanded at the moment the page asked. The request is only worth answering with
// until the toolbox has moved somewhere since: once it has, the store knows better.
let expandedToolWhenThePageAsked: string | undefined = undefined;

/**
 * Drops the page's tool request once the toolbox has expanded something since it was made --
 * the page's own tool, or another one the user picked instead. Called on every store change.
 *
 * Why not simply drop it as soon as the page has been dealt with: while the toolbox is
 * hidden, offerToolAndMaybeExpand() offers a page's tool without expanding it, so nothing in
 * the store yet knows this page belongs to the Game tool, and dropping the request there
 * would let Talking Book claim the page's audio and highlighting -- the very thing
 * BL-14434 added it for. And why not keep it until the next page: the user can expand
 * another tool meanwhile, and then the request would go on naming a tool that is not
 * running.
 */
function forgetPageToolRequestIfTheToolboxHasMovedOn(): void {
    if (!toolThePageRequires) {
        return;
    }
    if (getExpandedToolId() !== expandedToolWhenThePageAsked) {
        toolThePageRequires = undefined;
    }
}

/**
 * The tool whose markup and audio should apply to the page in front of us: the one a loading
 * page requires if it has said so, otherwise the expanded tool. This is deliberately not any
 * of the words toolboxState.ts defines -- during a page change it can name a tool that is
 * not yet the expanded one, which is the whole reason it exists.
 */
export function getEffectiveToolId(): string | undefined {
    return toolThePageRequires ?? findExpandedTool()?.id();
}

// How long, after a tool is turned on in the "More..." tool, we wait
// before adding/expanding it. Expanding it collapses the "More..." tool, so we delay
// it just long enough for the user to see the checkbox they ticked. (BL-16501)
const kShowToolAfterEnableDelayMs = 300;

// Pending deferred "expand this tool" timers, keyed by canonical tool id, so a later toggle
// of the same tool can cancel an expand that hasn't fired yet.
// We deliberately don't clear this map on toolbox teardown/navigation: each timer
// is ~300ms and removes its own entry when it fires, so at most a couple of very
// short-lived entries ever exist and nothing can accumulate. (BL-16501)
const pendingShowToolTimeouts = new Map<
    string,
    ReturnType<typeof setTimeout>
>();

// modifies the store's set of enabled tools, the saved enabled
// state of the tool in question (canonical id), and the presence of
// the tool in the toolbox, whenever the tool is checked
// or unchecked in the toolbox settings.
// deferShowToRevealCheckbox is set only by the "More..." settings checkboxes:
// when turning a tool on from there, expanding it collapses the settings tool,
// so we briefly delay the expand (see below) to let the user see the checkbox they
// ticked. Other callers (e.g. activating a tool from an in-page action) leave it
// false so the tool expands immediately. (BL-16501)
export function applyToolEnabledChange(
    toolId: string,
    turnOn: boolean,
    deferShowToRevealCheckbox: boolean = false,
): void {
    // The "More..." checkboxes render from the store, so this is also what re-ticks the
    // checkbox when a tool is enabled from somewhere else (e.g. an in-page action).
    setToolEnabled(toolId, turnOn);

    postString(
        "editView/saveToolboxSetting",
        "active\t" + toEnabledSettingName(toolId) + "\t" + (turnOn ? "1" : "0"),
    );

    // A pending deferred expand (below) reflects an earlier state; this call
    // supersedes it, so cancel it. Without this, ticking a tool on and then off
    // again within the delay would let the stale timer re-add the disabled tool
    // (the disable runs synchronously and would otherwise be overtaken).
    const pendingTimeout = pendingShowToolTimeouts.get(toolId);
    if (pendingTimeout !== undefined) {
        clearTimeout(pendingTimeout);
        pendingShowToolTimeouts.delete(toolId);
    }

    if (turnOn && deferShowToRevealCheckbox) {
        // Turning a tool on adds it to the toolbox and expands it, which collapses
        // the "More..." tool. If we do that immediately, "More..." closes before
        // the user perceives the checkbox they just ticked. Briefly delay, so the
        // checkmark is visible before "More..." closes to reveal the newly-enabled
        // tool. (BL-16501)
        const timeout = setTimeout(() => {
            pendingShowToolTimeouts.delete(toolId);
            // Guard against the tool having been turned off again during the delay.
            if (isToolEnabled(toolId)) {
                offerOrWithdrawTool(toolId, true);
            }
        }, kShowToolAfterEnableDelayMs);
        pendingShowToolTimeouts.set(toolId, timeout);
    } else {
        offerOrWithdrawTool(toolId, turnOn);
    }
}

function offerOrWithdrawTool(toolId: string, turnOn: boolean) {
    if (turnOn) {
        offerToolAndMaybeExpand(toolId, true);
    } else {
        withdrawTool(toolId);
    }
}

function restoreToolboxSettings() {
    // Snapshot BEFORE the request goes out, not in its callback: the user can open the
    // toolbox or pick a tool while it is in flight, and a snapshot taken on arrival would
    // already contain their choice and so look unchanged, letting the restore overwrite it.
    const stateWhenFetched = captureToolboxStateForRestore();
    get("toolbox/settings", (result) => {
        savedSettings = result.data;
        const pageFrame = getPageIFrame();
        const contentWin = pageFrame.contentWindow;
        if (contentWin && contentWin.document.readyState === "loading") {
            // We can't finish restoring settings until the main document is loaded, so arrange to call the next stage when it is.
            $(contentWin.document).ready((_e) =>
                restoreToolboxSettingsWhenPageReady(
                    result.data,
                    stateWhenFetched,
                ),
            );
            return;
        }
        // not loading, we can proceed immediately.
        restoreToolboxSettingsWhenPageReady(result.data, stateWhenFetched);
    });
}

export function applyToolboxStateToUpdatedPage() {
    // Snapshot before the request, for the reason given in restoreToolboxSettings().
    const stateWhenFetched = captureToolboxStateForRestore();
    get("toolbox/settings", (result) => {
        savedSettings = result.data;
        // savedSettings["current"] is always set to the last expanded tool for the book,
        // except for new books where it is null. In that case, the default value
        // should be the talking book tool.  (BL-16026)
        const currentFromBook = toCanonicalToolId(
            (savedSettings && savedSettings["current"]) || kTalkingBookToolId,
        );
        const currentInToolbox = findExpandedTool()?.id() ?? "";
        const shouldBeVisible = !!(
            savedSettings && savedSettings["visibility"]
        );
        const isVisible = toolbox.toolboxIsShowing();
        syncToolboxVisibilityFromDom();

        // When switching books, sync visibility and the expanded tool first.
        if (
            currentFromBook !== currentInToolbox ||
            shouldBeVisible !== isVisible
        ) {
            restoreToolboxSettingsWhenPageReady(
                savedSettings,
                stateWhenFetched,
            );
            return;
        }

        if (currentInToolbox && isVisible) {
            // Say that the page is ready, but not until it really is. That re-runs the
            // running tool's beginRestoreSettings/showTool/newPageReady for the new page:
            // some tools do their page-dependent setup in showTool(), and re-reading the
            // saved settings is how a tool's state follows a switch of book. See
            // useToolLifecycle.ts.
            // We used to call updateMarkup() here.
            // Now we don't because it would mess up the Talking Book Tool
            // if you really need it, add call to updateMarkup to the tool's implementation of newPageReady.
            doWhenPageReady(() => notePageReady());
        }
    });
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

// What the toolbox looked like when we fetched the settings we are about to apply. See
// restoreToolboxSettingsWhenPageReady().
interface IToolboxStateWhenFetched {
    expandedToolId: string | undefined;
    toolboxVisible: boolean;
}

function captureToolboxStateForRestore(): IToolboxStateWhenFetched {
    return {
        expandedToolId: getToolboxUiState().expandedToolId,
        toolboxVisible: toolbox.toolboxIsShowing(),
    };
}

/**
 * Once the page is ready, makes the toolbox's visibility and expanded tool match the book's
 * saved settings -- except where the user got there first.
 *
 * We have to wait for the page (CKEditor), and the settings we are applying were read
 * before that wait. Meanwhile the user can open the toolbox or pick a tool, and the page
 * itself can require one. Simply applying what we read would undo them: a toolbox just
 * expanded was shut again, and a tool just expanded collapsed back to the one saved in the book.
 * A person who clicks that fast simply clicks again, but it lost the e2e tests for Test
 * Case IDs 830, 441, 442 and 460 often enough that they had to be skipped.
 *
 * So each saved fact is imposed only if that fact has not changed since we read it.
 * Comparing against what we saw at fetch time, rather than re-reading the settings, is
 * deliberate: every change is saved with a fire-and-forget post, so a later read is not
 * guaranteed to see one that has just happened.
 */
function restoreToolboxSettingsWhenPageReady(
    settings: IToolboxSettings,
    stateWhenFetched: IToolboxStateWhenFetched,
) {
    doWhenPageReady(() => {
        // OK, CKEditor is done (or page doesn't use it), we can finally do the real initialization.
        const opts = settings;
        // The book's remembered tool is always set except for new books. For new books it is
        // undefined and we want
        // to treat that the same as if it were set to the talking book tool so that the tool will
        // display the first time the user opens the toolbox. (BL-16026)
        // Convert here, at the boundary: toolIds.ts owns the "Tool"-suffixed spelling and
        // everything inward of this point deals in canonical ids.
        const toolTheBookRemembers = toCanonicalToolId(
            opts["current"] || kTalkingBookToolId,
        );
        const shouldBeVisible = !!opts["visibility"];

        const isVisibleNow = toolbox.toolboxIsShowing();
        const somebodyChangedVisibility =
            isVisibleNow !== stateWhenFetched.toolboxVisible;
        if (!somebodyChangedVisibility && isVisibleNow !== shouldBeVisible) {
            toolbox.toggleToolbox();
        }
        syncToolboxVisibilityFromDom();

        // We only get here when a page has just become ready, so say so. Together with
        // expandToolOrFirstOffered() below this is one batch of state changes, hence one run of the
        // tool's lifecycle, however many of these facts actually changed.
        notePageReady();

        const somebodyChoseATool =
            getToolboxUiState().expandedToolId !==
            stateWhenFetched.expandedToolId;
        if (!somebodyChoseATool) {
            // Before we set stage/level, as it initializes them to 1.
            // Forget what we last persisted first, so that this book records its own expanded
            // tool even if the book before it was using the same one.
            lastPersistedToolId = undefined;
            expandToolOrFirstOffered(toolTheBookRemembers);
        }

        // Note: the bulk of restoring the settings (everything but which tool, if any, is
        // expanded) is done when a tool starts running.
    });
}

// Remove any markup the toolbox is inserting. Called by a RunJavaScript() in EditingView
// before saving the page.
export function removeToolboxMarkup() {
    detachExpandedTool();
}

// The tool we last told Bloom to remember. Only a cache, to keep us from re-posting the
// same setting on every state change; the store remains the one place that says which tool
// is expanded. Cleared when a page's saved settings are restored, so that each book writes its
// setting once even when two books in a row use the same tool.
let lastPersistedToolId: string | undefined = undefined;

/**
 * Has Bloom remember which tool is expanded, when that has changed. The book's meta.json has
 * always stored this with the historical "Tool" suffix.
 *
 * Nothing is persisted for "nothing expanded": the setting has no way to say that, and
 * expanding a tool later writes it then.
 */
function persistExpandedToolIfItChanged(): void {
    const expandedToolId = getToolboxUiState().expandedToolId;
    if (expandedToolId === lastPersistedToolId) {
        return;
    }
    lastPersistedToolId = expandedToolId;
    if (!expandedToolId) {
        return;
    }
    postString(
        "editView/saveToolboxSetting",
        "current\t" + toPersistedToolName(expandedToolId),
    );
}

/**
 * Attempts to expand the given tool (normally the tool the book was last using). If the
 * toolbox isn't offering that tool, falls back to the first tool it does
 * offer. Passing an empty id also means "whatever tool is first".
 * The id may arrive in either spelling, because one caller passes the book's saved
 * "current" tool name straight from meta.json.
 */
function expandToolOrFirstOffered(toolId: string) {
    toolId = toCanonicalToolId(toolId);

    // NOTE: getFirstOfferedToolId() never returns the More (settings) tool: it is never a
    // sensible *default*. (Expanding it by hand does still make it the expanded tool and gets
    // persisted -- meta.json calls that "current" -- as it always has.)
    if (!toolId) {
        toolId = getFirstOfferedToolId() ?? "";
    }

    if (toolId) {
        const tool = masterToolList.find(
            (possibleTool) => possibleTool.id() === toolId,
        );
        if (tool && !isToolOffered(tool.id())) {
            // The tool we were asked for isn't in the toolbox (e.g., it was disabled
            // since we saved the setting), so fall back to whatever is first.
            toolId = getFirstOfferedToolId() ?? "";
        }
    }

    if (toolId) {
        setExpandedTool(toolId);
    }
}

// Parameter 'toolId' may be spelled with or without the 'Tool' suffix, since it may have
// come from persisted data.
// Returns undefined if we know of no such tool, e.g. because the book's settings were
// saved by a version of Bloom that had a tool this one doesn't.
function getITool(toolId: string): ITool | undefined {
    const canonicalToolId = toCanonicalToolId(toolId);
    return masterToolList.find((tool) => tool.id() === canonicalToolId);
}

/**
 * Tells the toolbox to offer this tool, and -- if the toolbox is showing -- to expand it.
 * Asking to expand it while the toolbox is hidden offers it and nothing more, which is why
 * adjustToolListForPage() holds on to the page's request until the tool really is expanded.
 * These tools are the tools enabled by the user, tools that are always enabled
 * (like the talking book tool), and the settings ("More...") tool.
 */
function offerToolAndMaybeExpand(
    toolId: string,
    expandTool: boolean,
    whenLoaded?: () => void,
): void {
    const tool = getITool(toolId);
    if (!tool) {
        console.error(
            `Tool ${toolId} not found, assuming that was from a different version of Bloom.`,
        );
        return;
    }

    // No waiting for the toolbox UI here, unlike the adapter this replaced: a page can ask
    // for a tool while it is still loading (a custom layout page enables the Canvas tool as
    // it sets itself up, and finds us as soon as the toolbox frame publishes its exports),
    // which used to arrive before ToolboxRoot had mounted and registered itself. The store
    // is not the UI -- it exists as soon as the module loads, and ToolboxRoot shows whatever
    // it holds whenever it mounts -- so an early request simply lands.

    // Offering a tool that is already offered does nothing, so it is safe to do this
    // whether or not the toolbox is already offering it.
    offerTool(tool.id());

    if (expandTool && toolbox.toolboxIsShowing()) {
        setExpandedTool(tool.id());
    }

    if (whenLoaded) {
        whenLoaded();
    }
}

function showToolboxChanged(nowShowing: boolean): void {
    postString(
        "editView/saveToolboxSetting",
        "visibility\t" + (nowShowing ? "visible" : ""),
    );
    const expandedTool = findExpandedTool();
    if (expandedTool && !nowShowing) {
        postString(
            "logger/writeEvent",
            `Toolbox deactivating: ${expandedTool.id()}`,
        );
    }
    // Hiding the toolbox stops the expanded tool running, and showing it again starts it
    // again. Both follow from this; see useToolLifecycle.ts.
    setToolboxVisible(nowShowing);
    if (!expandedTool) {
        // Starting up for the very first time in this book: nothing is expanded, so expand
        // the first tool on offer. If the toolbox somehow has nothing to offer there is
        // nothing to expand -- which cannot happen, since Talking Book is always enabled.
        const firstOffered = getFirstOfferedToolId();
        if (firstOffered) {
            setExpandedTool(firstOffered);
        }
    }
}
