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

    // Renders this tool's panel. ToolboxRoot renders it inside the tool's accordion
    // section, in the toolbox's single React tree, so context (e.g. the MUI theme)
    // reaches it normally.
    // It should return the main content of the tool, which must be a single element
    // (ToolboxRoot sizes that element to fill the section).
    // ToolboxRoot renders the section header (label, icon, subscription badge) around it;
    // this method is however responsible to localize the content of the panel.
    renderPanel(): React.ReactNode;
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
        configurePageEditingHandlers(container, this);
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
 * requestedToolId is a canonical tool id (the toolbox UI only ever reports tools it is
 * offering, and it was told about them by their canonical ids).
 * Note: do not name this parameter newToolId; that is the module-level variable this
 * function clears at the end, and shadowing it silently breaks getActiveToolId().
 */
function switchTool(requestedToolId: string): void {
    // Have Bloom remember which tool is active. (Might be none.) The book's meta.json
    // has always stored this with the historical "Tool" suffix.
    postString(
        "editView/saveToolboxSetting",
        "current\t" + toPersistedToolName(requestedToolId),
    );
    let newTool: ITool | null = null;
    if (requestedToolId) {
        newTool =
            masterToolList.find((tool) => tool.id() === requestedToolId) ??
            null;
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
