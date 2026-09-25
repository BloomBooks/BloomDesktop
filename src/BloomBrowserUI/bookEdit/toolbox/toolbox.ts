/// <reference path="../../typings/jqueryui/jqueryui.d.ts" />

import $ from "jquery";
import "../../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js";
import "../../lib/jquery.i18n.custom";
import "../../lib/jquery.onSafe";
import axios from "axios";
import { get, postString, wrapAxios } from "../../utils/bloomApi";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { hookupLinkHandler } from "../../utils/linkHandler";
import {
    ckeditableSelector,
    getPageIFrame,
    getPageIframeBody,
} from "../../utils/shared";
import { GameTool } from "./games/GameTool";
import { isLongPressEvaluating } from "../longPressShared";
import { configurePageEditingHandlers } from "./pageEditingMarkup";
import { getFeatureStatusAsync } from "../../react_components/featureStatus";
import { showRequiresSubscriptionDialogInAnyView } from "../../react_components/requiresSubscription";
import {
    callOnBlur,
    setExtraFunctionToHandleBlurTasks,
} from "../../utils/menuCloseOnBlur";
export { isLongPressEvaluating };
export { callOnBlur as registerMenuCloseOnBlur };

const checkLeaveOffTool: string = "Visualizer";

type ToolboxSettings = Record<string, string> & {
    current?: string;
    visibility?: string;
};

let savedSettings: ToolboxSettings = {};

// This variable stores all the ids of the enabled tools, so
// that the React toolbox settings can initially check the
// checkboxes that correspond to the enabled tools
let enabledToolIds = new Set<string>();

// checks if the tool is currently enabled by using its
// name and the enabledToolIds set
export function isToolEnabledInToolbox(toolName: string): boolean {
    return enabledToolIds.has(toolName);
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
// can be asked to do by the rest of the system.
// See ToolboxView.cs class comment for a summary of how to add a new tool.
export interface ITool {
    beginRestoreSettings(settings: string): JQueryPromise<void>;
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
    id(): string; // without trailing "Tool"!
    isAlwaysEnabled(): boolean;
    // If this is true, the tool may only be selected on pages that have data-tool-id matching this tool's id.
    requiresToolId(): boolean;

    // Implement this if the tool uses React.
    // It should return the main content of the tool, which must be a single div.
    // (toolbox will construct the h3 element which goes along with it in the accordion
    // and set its data-toolId attr; this method is however responsible to
    // localize the content of the div.)
    // It may be unimplemented for older tools where beginAddTool() already knows
    // where to find an HTML file for the tool content.
    makeRootElement(): HTMLDivElement;
    // notifies the tool that an image has been changed on the page.
    // If the change only affects one image, it may be passed; otherwise, all should be fixed.
    imageUpdated(img: HTMLImageElement | undefined): void;
}

export interface IReactTool {
    // For tools that require a subscription. This will trigger an indicator communicating that this
    // featureName requires a subscription.
    featureName?: string;
}

// The toolbox is progressively migrating to React. Recently, in toolboxRoot.tsx, we made
// the root of the whole toolbox a React component. The code here has not been fully
// integrated into the new approach, along with several tools that are not yet React.
// This interface, which is exported by the React component, allows the legacy code
// to interact with the React component, e.g., to set the active tool,
// or to be notified when the active tool changes.
interface IToolboxReactAdapter {
    isEnabled(): boolean;
    setActiveToolByToolId(toolId: string): void;
    getActiveToolId(): string | undefined;
    onActiveToolChanged(callback: (toolId: string) => void): void;
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
        // until we've finished otherwise setting up the toolbox; in particular, we can't refresh
        // the accordion before we first set it up.
        // It's possible there will be a tiny bit of flicker if the book opens on a page that
        // has a required tool as we first initialize the toolbox without that tool and then
        // add it. But this is fairly rare and I have not found it noticeable.
        const doAdjustment = () => {
            if (!this.builtToolbox) {
                setTimeout(doAdjustment, 100);
                return;
            }
            const toolbox = document.getElementById("toolbox") as HTMLElement;
            let toolsAdjusted = false;
            for (let i = 0; i < masterToolList.length; i++) {
                if (masterToolList[i].requiresToolId()) {
                    // We may need to add or remove the specified tool

                    // Adapt the tool object id to the value used as the ID of the element
                    // for that tool in the toolbox.
                    const toolId = ToolBox.addToolToString(
                        masterToolList[i].id(),
                    );
                    // Get the header element that represents the tool in the DOM.
                    const toolHeader = toolbox.querySelector(
                        "[data-toolid='" +
                            ToolBox.addToolToString(toolId) +
                            "']",
                    ) as HTMLElement;
                    const haveTool = !!toolHeader;
                    const wantTool = requiredToolId === masterToolList[i].id();
                    if (haveTool !== wantTool) {
                        // add or remove as needed.
                        showOrHideTool(
                            ToolBox.addToolToString(masterToolList[i].id()),
                            wantTool,
                        ); // required tools don't have check boxes.
                        toolsAdjusted = wantTool;
                    }
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

    // Append "Tool" to the tool name if it's not already there.
    // Put a space between the name and "Tool" if addSpace is true.
    public static addToolToString(
        toolName: string,
        addSpace: boolean = false,
    ): string {
        if (toolName) {
            if (
                toolName.indexOf(checkLeaveOffTool) === -1 &&
                toolName.indexOf("Tool") === -1
            ) {
                if (addSpace) {
                    return toolName + " Tool";
                } else {
                    return toolName + "Tool";
                }
            }
        }
        return toolName;
    }

    // In the process of moving this to shared.ts, but a lot of
    // code still expects to find it here.
    public static getPageFrame(): HTMLIFrameElement {
        return getPageIFrame();
    }

    // In the process of moving this to shared.ts as getPageIframeBody, but a lot of
    // code still expects to find it here.
    // The body of the editable page, a root for searching for document content.
    public static getPage(): HTMLElement | null {
        return getPageIframeBody();
    }

    public static isXmatterPage(): boolean {
        const page = ToolBox.getPage();
        if (!page) return false;
        const bloomPage = page.querySelector(".bloom-page");
        if (!bloomPage) return false;
        const classes = bloomPage.getAttribute("class");
        if (!classes) return false;
        return (
            // Enhance: when our typescript "groks" string.include(), it would simplify things.
            classes.indexOf("bloom-frontMatter") > -1 ||
            classes.indexOf("bloom-backMatter") > -1
        );
    }

    public static registerTool(tool: ITool) {
        masterToolList.push(tool);
    }

    private getEnabledTools() {
        // Using axios directly because api calls for returning the promise.
        return axios.get("/bloom/api/toolbox/enabledTools");
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

        // Using axios directly because bloomApi doesn't support merging promises with .all
        wrapAxios(
            axios.all([this.getEnabledTools()]).then(
                axios.spread((enabledTools) => {
                    // remove any experimental tools the user doesn't want
                    // TODO: give each experimental tool it's own setting once we have any experimental tools again.
                    // Presumably use the tool id as the keyword in the list of experimental features.
                    const toolsToLoad = enabledTools.data
                        .split(",")
                        .map((toolId: string) => toolId.trim())
                        .filter((toolId: string) => toolId.length > 0)
                        .map((toolId: string) =>
                            toolId.endsWith("Tool")
                                ? toolId.substring(0, toolId.length - 4)
                                : toolId,
                        );
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

                    toolsToLoad.push("settings");
                    $("#toolbox").hide();
                    const loadNextTool = () => {
                        if (toolsToLoad.length === 0) {
                            $("#toolbox").accordion({
                                heightStyle: "fill",
                            });
                            $("body").find("*[data-i18n]").localize(); // run localization

                            // Now bind the window's resize function to the toolbox resizer
                            $(window).bind("resize", () => {
                                clearTimeout(resizeTimer); // resizeTimer variable is defined outside of ready function
                                resizeTimer = setTimeout(resizeToolbox, 100);
                            });
                            this.builtToolbox = true;
                            // loaded them all, now we can deal with settings.
                            restoreToolboxSettings();
                            $("#toolbox").show();
                            // I don't know why, but the accordion refresh inside resizeToolbox is needed
                            // to (at least) make the accordion icons appear, and it has to happen on a later cycle.
                            setTimeout(resizeToolbox, 0);
                        } else {
                            // optimize: maybe we can overlap these?
                            const nextToolId = toolsToLoad.pop();
                            const toolId = ToolBox.addToolToString(nextToolId);
                            beginAddTool(toolId, false, () => loadNextTool());
                        }
                    };
                    loadNextTool();
                }),
            ),
        );
    }

    public isToolActive(toolId: string): boolean {
        const tools = $("*[data-toolId]");
        const filteredTools = tools.filter(function () {
            return $(this).attr("data-toolId") === toolId;
        });
        return filteredTools.length > 0;
    }

    // Enables a tool from an in-page action, ensuring the toolbox is visible.
    public enableToolFromPage(toolId: string): void {
        if (!this.toolboxIsShowing()) {
            this.toggleToolbox();
        }
        setToolEnabledFromSettings(toolId, true);
    }

    public activateToolFromId(toolId: string) {
        if (!getITool(toolId)) {
            // Normally we won't even give a way to see this tool if it's
            // not available for experimental reasons, but sometimes (e.g.
            // clicking on a video placeholder, it will help the user to
            // say why nothing is happening.
            const msg =
                "This tool requires that you enable Settings : Advanced Program Settings : Show Experimental Features";
            alert(msg);
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

        if (isToolEnabledInToolbox(toolId)) {
            // Already enabled; just make it the active tool.
            setCurrentTool(toolId);
        } else {
            // Not a required-for-this-page tool that's already present, and not yet enabled.
            const toolbox = document.getElementById("toolbox") as HTMLElement;
            const toolHeader = toolbox.querySelector(
                "[data-toolid='" + ToolBox.addToolToString(toolId) + "']",
            ) as HTMLElement;
            if (toolHeader) {
                // Present in the accordion (e.g. a required tool) but not in enabledToolIds.
                setCurrentTool(toolId);
            } else {
                // Genuinely disabled: enable it, which persists the state and updates
                // enabledToolIds, then activates it (showOrHideTool opens it by default).
                setToolEnabledFromSettings(toolId, true);
            }
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

// The AI decided to create this react adapter object and save in in a window variable.
// It gets set in a useEffect in the React component that is the root of the toolbox.
// This function retrieves it. Once the toolbox has started up, it should always
// successfully return a valid adapter object. AI has built fallback code that tries to
// do various things in other ways when it is not available. Most of that fallback code
// is probably already redundant, but it's hard to be sure which. I'm inclined to leave
// it until we get all the tools migrated to React; then we can do a lot of simplification
// and probably get rid the adapter and fallbacks entirely; instead, each component
// will belong to its own accordion section and will be able to manage its own state
// and lifecycle.
function getToolboxReactAdapter(): IToolboxReactAdapter | undefined {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const adapter = (window as any).toolboxReactAdapter as
        | IToolboxReactAdapter
        | undefined;
    if (!adapter) {
        return undefined;
    }
    if (!adapter.isEnabled()) {
        return undefined;
    }
    return adapter;
}

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

// Pending deferred "open this tool" timers, keyed by tool name, so a later toggle
// of the same tool can cancel an open that hasn't fired yet.
// We deliberately don't clear this map on toolbox teardown/navigation: each timer
// is ~300ms and removes its own entry when it fires, so at most a couple of very
// short-lived entries ever exist and nothing can accumulate. (BL-16501)
const pendingShowToolTimeouts = new Map<
    string,
    ReturnType<typeof setTimeout>
>();

// modifies the enabledToolIds set, the saved active
// state of the tool in question, and the presence of
// the tool in the toolbox, whenever the tool is checked
// or unchecked in the toolbox settings.
// deferShowToRevealCheckbox is set only by the "More..." settings checkboxes:
// when turning a tool on from there, opening it collapses the settings section,
// so we briefly delay the open (see below) to let the user see the checkbox they
// ticked. Other callers (e.g. activating a tool from an in-page action) leave it
// false so the tool opens immediately. (BL-16501)
export function setToolEnabledFromSettings(
    toolName: string,
    turnOn: boolean,
    deferShowToRevealCheckbox: boolean = false,
): void {
    if (turnOn) {
        enabledToolIds.add(toolName);
    } else {
        enabledToolIds.delete(toolName);
    }

    const toolId =
        toolName.indexOf(checkLeaveOffTool) === -1
            ? toolName + "Tool"
            : toolName;

    postString(
        "editView/saveToolboxSetting",
        "active\t" + toolName + "Check\t" + (turnOn ? "1" : "0"),
    );

    if (changeToolboxSettingsState !== undefined) {
        changeToolboxSettingsState(toolName, turnOn);
    }

    // A pending deferred open (below) reflects an earlier state; this call
    // supersedes it, so cancel it. Without this, ticking a tool on and then off
    // again within the delay would let the stale timer re-add the disabled tool
    // (the disable runs synchronously and would otherwise be overtaken).
    const pendingTimeout = pendingShowToolTimeouts.get(toolName);
    if (pendingTimeout !== undefined) {
        clearTimeout(pendingTimeout);
        pendingShowToolTimeouts.delete(toolName);
    }

    if (turnOn && deferShowToRevealCheckbox) {
        // Turning a tool on adds it to the accordion and makes it the active
        // section, which collapses the "More..." settings section. If we do that
        // immediately, the "More..." section closes before the user perceives the
        // checkbox they just ticked. Briefly delay so the checkmark is visible
        // before the section collapses to reveal the newly-enabled tool. (BL-16501)
        const timeout = setTimeout(() => {
            pendingShowToolTimeouts.delete(toolName);
            // Guard against the tool having been turned off again during the delay.
            if (enabledToolIds.has(toolName)) {
                showOrHideTool(toolId, true);
            }
        }, kShowToolAfterEnableDelayMs);
        pendingShowToolTimeouts.set(toolName, timeout);
    } else {
        showOrHideTool(toolId, turnOn);
    }
}

function showOrHideTool(
    tool: string,
    turnOn: boolean,
    openTool: boolean = true,
) {
    if (turnOn) {
        beginAddTool(tool, openTool);
    } else {
        $("*[data-toolId]")
            .filter(function () {
                return $(this).attr("data-toolId") === tool;
            })
            .remove();
        window.dispatchEvent(
            new CustomEvent("toolbox-tool-removed", {
                detail: { toolId: tool },
            }),
        );
    }
    resizeToolbox();
}

export function restoreToolboxSettings() {
    get("toolbox/settings", (result) => {
        savedSettings = result.data;
        const pageFrame = ToolBox.getPageFrame();
        const contentWin = pageFrame.contentWindow;
        if (contentWin && contentWin.document.readyState === "loading") {
            // We can't finish restoring settings until the main document is loaded, so arrange to call the next stage when it is.
            $(contentWin.document).ready((_e) =>
                restoreToolboxSettingsWhenPageReady(),
            );
            return;
        }
        restoreToolboxSettingsWhenPageReady(); // not loading, we can proceed immediately.
    });
}

export function applyToolboxStateToUpdatedPage() {
    get("toolbox/settings", (result) => {
        savedSettings = result.data;
        // savedSettings["current"] is always set to the last active tool for the book,
        // except for new books where it is null. In that case, the default value
        // should be talkingBookTool.  (BL-16026)
        const currentFromBook = ToolBox.addToolToString(
            (savedSettings && savedSettings["current"]) || "talkingBookTool",
        );
        const currentInToolbox = currentTool
            ? ToolBox.addToolToString(currentTool.id())
            : "";
        const shouldBeVisible = !!(
            savedSettings && savedSettings["visibility"]
        );
        const isVisible = toolbox.toolboxIsShowing();

        // When switching books, sync visibility/current tool first.
        if (
            currentFromBook !== currentInToolbox ||
            shouldBeVisible !== isVisible
        ) {
            restoreToolboxSettingsWhenPageReady();
            return;
        }

        if (currentTool && toolbox.toolboxIsShowing()) {
            doWhenPageReady(() => {
                const activeTool = currentTool;
                if (activeTool && isToolInitialized(activeTool)) {
                    activeTool
                        .beginRestoreSettings(
                            savedSettings as unknown as string,
                        )
                        .then(() => {
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
    const page = ToolBox.getPage();
    if (!page || !ToolBox.getPageFrame()) {
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
    const contentWindow = ToolBox.getPageFrame().contentWindow as
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

// Once the page is ready, makes the toolbox's visibility and current tool match the book's saved
// settings. The settings are read only then, not before waiting: while the page loads, the user
// (or the page itself, e.g. one that requires a tool) may already have opened or closed the
// toolbox or chosen a tool, and each of those is saved as it happens. Reading the settings after
// the wait keeps those changes; applying a copy fetched earlier would undo them.
function restoreToolboxSettingsWhenPageReady() {
    doWhenPageReady(() => {
        // OK, CKEditor is done (or page doesn't use it), we can finally do the real initialization.
        get("toolbox/settings", (result) => {
            savedSettings = result.data;
            const opts = savedSettings;
            // currentTool is always set except for new books. For new books, it is undefined and we want
            // to treat that the same as if it were set to "talkingBookTool" so that the tool will display
            // the first time the user opens the toolbox. (BL-16026)
            const currentTool = opts["current"] || "talkingBookTool";
            const shouldBeVisible = !!opts["visibility"];

            if (toolbox.toolboxIsShowing() !== shouldBeVisible) {
                toolbox.toggleToolbox();
            }

            // Before we set stage/level, as it initializes them to 1.
            setCurrentTool(currentTool);

            // Note: the bulk of restoring the settings (everything but which if any tool is active)
            // is done when a tool becomes current.
        });
    });
}

// Remove any markup the toolbox is inserting. Called by a RunJavaScript() in EditingView
// before saving the page.
export function removeToolboxMarkup() {
    detachCurrentTool();
}

function switchTool(newToolName: string): void {
    // Have Bloom remember which tool is active. (Might be none)
    postString("editView/saveToolboxSetting", "current\t" + newToolName);
    let newTool: ITool | null = null;
    if (newToolName) {
        for (let i = 0; i < masterToolList.length; i++) {
            // the newToolName comes from meta.json and we've changed our minds a few times about
            // whether it should end in "Tool" so what's in the meta.json might have it or not.
            // For robustness we will recognize any tool name that starts with the (no -Tool)
            // name we're looking for.
            if (newToolName.startsWith(masterToolList[i].id())) {
                newTool = masterToolList[i];
            }
        }
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
        const toolElt = getToolElement(newTool);
        if (!toolElt) {
            return;
        }
        // Always re-restore settings so tool state tracks the current book.
        newTool
            .beginRestoreSettings(savedSettings as unknown as string)
            .then(() => {
                activateToolInternalAsync(newTool, toolElt);
            });
    }
}

function getToolElement(tool: ITool): HTMLElement | null {
    let toolElement: HTMLElement | null = null;
    if (tool) {
        const toolName = ToolBox.addToolToString(tool.id());
        $("#toolbox")
            .find("> h3")
            .each(function () {
                if ($(this).attr("data-toolId") === toolName) {
                    // REVIEW: this may in fact be unneeded but I'm just trying to get eslint set up and conceivably it is intentional
                    // eslint-disable-next-line @typescript-eslint/no-this-alias
                    toolElement = this;
                    return false; // break from the each() loop
                }
                return true; // continue the each() loop
            });
    }
    return toolElement;
}

function isToolInitialized(tool: ITool): boolean {
    return !!getToolElement(tool);
}

async function activateToolInternalAsync(
    newTool: ITool,
    toolElt: HTMLElement | null,
): Promise<void> {
    if (!toolElt) {
        throw new Error(
            `activateToolInternalAsync called for uninitialized tool: ${newTool.id()}`,
        );
    }
    // Await it so that we can guarantee that newPageReady() happens after showTool.
    await newTool.showTool();

    postString("logger/writeEvent", `Toolbox activated: ${newTool.id()}`);

    // Note: Allowed to begin some async work too, and we will await its result.
    // (This apparently solves the single flash mentioned in BL-10471.)
    await newTool.newPageReady();
    scheduleDelayedNewPageReady(newTool);
}

/**
 * This function attempts to activate the tool whose "data-toolId" attribute is equal to the value
 * of "currentTool" (the last tool displayed).
 */
function setCurrentTool(toolID: string) {
    // I'm downright grumpy about how this code sometimes uses names with "Tool" appended, sometimes doesn't.
    // For now I'm just making functions work with either form.
    toolID = ToolBox.addToolToString(toolID);

    const adapter = getToolboxReactAdapter();
    if (adapter) {
        if (!toolboxReactActivationHooked) {
            adapter.onActiveToolChanged((newToolName: string) => {
                switchTool(newToolName);
            });
            toolboxReactActivationHooked = true;
        }

        if (!toolID) {
            toolID =
                ($("#toolbox").find("> h3").first().attr("data-toolId") as
                    | string
                    | undefined) ?? "";
        }

        if (toolID) {
            const tool = masterToolList.find(
                (possibleTool) =>
                    ToolBox.addToolToString(possibleTool.id()) === toolID,
            );
            if (tool && !isToolInitialized(tool)) {
                toolID =
                    ($("#toolbox").find("> h3").first().attr("data-toolId") as
                        | string
                        | undefined) ?? "";
            }
        }

        if (toolID) {
            adapter.setActiveToolByToolId(toolID);
        }
        return;
    }

    // NOTE: tools without a "data-toolId" attribute (such as the More tool) cannot be the "currentTool."
    let idx = 0;
    const toolbox = $("#toolbox");

    const accordionHeaders = toolbox.find("> h3");
    if (toolID) {
        let foundTool = false;
        // find the index of the tool whose "data-toolId" attribute equals the value of "currentTool"
        accordionHeaders.each(function () {
            if ($(this).attr("data-toolId") === toolID) {
                foundTool = true;
                // break from the each() loop
                return false;
            }
            idx++;
            return true; // continue the each() loop
        });
        if (!foundTool) {
            idx = 0;
            toolID = "";
        }
    }
    if (!toolID) {
        // Leave idx at 0, and update currentTool to the corresponding ID.
        toolID = toolbox.find("> h3").first().attr("data-toolId");
    }
    if (idx >= accordionHeaders.length - 1) {
        // don't pick the More... tool, pick whatever happens to be first.
        idx = 0;
    }

    // turn off animation
    const ani = toolbox.accordion("option", "animate");
    toolbox.accordion("option", "animate", false);

    // the index must be passed as an int, a string will not work.
    toolbox.accordion("option", "active", idx);

    // turn animation back on
    toolbox.accordion("option", "animate", ani);

    // when a tool is activated, save its data-toolId so state can be restored when Bloom is restarted.
    // We do this after we actually set the initial tool, because setting the intial tool may not CHANGE
    // the active tool (if it's already the one we want, typically the first), so we can't rely on
    // the activate event happening in the initial call. Instead, we make SURE to call it for the
    // tool we are making active.
    toolbox.onSafe("accordionactivate.toolbox", (event, ui) => {
        let newToolName = "";
        if (ui.newHeader.attr("data-toolId")) {
            newToolName = ui.newHeader.attr("data-toolId").toString();
        }
        switchTool(newToolName);
    });
    //alert("switching to " + currentTool + " which has index " + toolIndex);
    //setTimeout(e => switchTool(currentTool), 700);
    switchTool(toolID);
}

// Parameter 'toolId' is the complete tool id with the 'Tool' suffix
// Can return undefined in the case of an experimental tool with
// Advanced Program Settings: Show Experimental Features unchecked.
function getITool(toolId: string): ITool {
    // I'm downright grumpy about how this code sometimes uses names with "Tool" appended, sometimes doesn't.
    // For now I'm just making functions work with either form.
    const reactToolId =
        toolId.indexOf("Tool") > -1
            ? toolId.substring(0, toolId.length - 4)
            : toolId; // strip off "Tool"
    return masterToolList.find((tool) => tool.id() === reactToolId)!;
}

/**
 * Requests a tool from localhost and loads it into the toolbox.
 * These tools are the tools enabled by the user, tools that are
 * always enabled (like the talking book tool), and the settings
 * "tool".
 */
// these last three parameters were never used: function requestTool(checkBoxId, toolId, loadNextCallback, tools, currentTool) {
function beginAddTool(
    toolId: string,
    openTool: boolean,
    whenLoaded?: () => void,
): void {
    // new-style tool implemented in React
    const tool = getITool(toolId);
    if (!tool) {
        console.error(
            `Tool ${toolId} not found, assuming that was from a different version of Bloom.`,
        );
        return;
    }

    if (isToolInitialized(tool)) {
        if (openTool && toolbox.toolboxIsShowing()) {
            const toolName = ToolBox.addToolToString(tool.id());
            const adapter = getToolboxReactAdapter();
            if (adapter) {
                adapter.setActiveToolByToolId(toolName);
            }
        }

        if (whenLoaded) {
            whenLoaded();
        }
        return;
    }

    const content = $(tool.makeRootElement());

    // the settings for the toolbox is React, but
    // its localization works a little differently
    // than the other toolbox tools. So, special-case
    // handling is needed for the settings
    const isSettingsTool = tool.id() === "settings";

    const toolName = ToolBox.addToolToString(tool.id());
    // const parts = $("<h3 data-toolId='musicTool' data-i18n='EditTab.Toolbox.MusicTool'>"
    //     + "Music Tool</h3><div data-toolId='musicTool' class='musicBody'/>");

    const toolIdUpper =
        tool.id()[0].toUpperCase() + tool.id().substring(1, tool.id().length);
    const i18Id = isSettingsTool
        ? "EditTab.Toolbox.More"
        : "EditTab.Toolbox." +
          toolIdUpper +
          (toolName.indexOf(checkLeaveOffTool) === -1 ? "Tool" : "");
    // Not sure this will always work, but we can do something more complicated...maybe a new method
    // on ITool...if we need it. Note that this is just a way to come up with the English,
    // we don't do it to localizations. But in English, the code value beats the xlf one.
    const toolLabel = isSettingsTool
        ? "More..."
        : ToolBox.addToolToString(
              toolIdUpper.replace(/([A-Z])/g, " $1").trim(),
              true,
          );

    const reactTool = tool as unknown as IReactTool;

    // Currently, all subscription tools are React, so we haven't implemented a way to add the subscription badge to old-style tools
    const possibleSubscriptionBadge = reactTool.featureName
        ? `<span class="subscription-badge"></span>`
        : "";
    const header = $(
        `<h3><div class="toolbox-accordion-header-text" data-i18n=${i18Id}>${toolLabel}</div>${possibleSubscriptionBadge}</span></h3>`,
    );
    header.attr("data-toolId", toolName);
    content.attr("data-toolId", toolName);

    // Check feature status asynchronously and apply subscription requirements if needed
    if (reactTool.featureName) {
        header.attr("data-feature", reactTool.featureName);
        addFeatureStatusMessageTitlesToSubscriptionBadges(header);

        getFeatureStatusAsync(reactTool.featureName).then((featureStatus) => {
            if (featureStatus && featureStatus.subscriptionTier !== "Basic") {
                header.addClass("requiresSubscription");
            }
        });
    }

    loadToolboxTool(header, content, toolId, openTool);
    if (whenLoaded) {
        whenLoaded();
    }
    //}
}

let resizeTimer;
function resizeToolbox() {
    const windowHeight = $(window).height();
    const root = $(".toolboxRoot");
    // Set toolbox container height to fit in new window size
    // Then toolbox Resize() will adjust it to fit the container
    root.height(windowHeight - 25); // 25 is the top: value set for div.toolboxRoot in toolbox.less
    if (!getToolboxReactAdapter()) {
        $("#toolbox").accordion("refresh");
    }
}

/**
 * Gets the localized title text for a feature based on its status
 * @param featureName The name of the feature to get status for
 * @returns A Promise that resolves to the localized title text
 */
async function getFeatureEnabledAndMessage(
    featureName: string,
): Promise<{ enabled: boolean; message: string }> {
    return new Promise<{ enabled: boolean; message: string }>((resolve) => {
        get(`features/status?featureName=${featureName}`, (c) => {
            const featureStatus = c.data;
            const localizedTier = featureStatus?.localizedTier;

            let titleText: string;
            if (featureStatus.enabled) {
                titleText = theOneLocalizationManager.getText(
                    "Subscription.FeatureIsIncludedSentence",
                    "This feature is included in your {0} subscription.",
                    localizedTier,
                );
            } else {
                titleText = theOneLocalizationManager.getText(
                    "Subscription.RequiredTierForFeatureSentence",
                    'This feature requires a Bloom subscription tier of at least "{0}".',
                    localizedTier,
                );
            }
            resolve({ enabled: featureStatus.enabled, message: titleText });
        });
    });
}

function showSubscriptionDialog(featureName: string): void {
    showRequiresSubscriptionDialogInAnyView(featureName);
}

/**
 * Adds feature status message titles to subscription badges found in the specified jQuery element
 * @param element The jQuery element containing subscription badges
 */
async function addFeatureStatusMessageTitlesToSubscriptionBadges(
    element: JQuery,
): Promise<void> {
    const subscriptionBadges = element.find(".subscription-badge");
    const promises: Promise<void>[] = [];
    subscriptionBadges.each(function (_i, subscriptionBadge: HTMLElement) {
        if (subscriptionBadge.hasAttribute("title")) return;
        const featureName =
            subscriptionBadge.parentElement?.getAttribute("data-feature");
        if (!featureName) return;

        const promise = (async () => {
            const { enabled, message } =
                await getFeatureEnabledAndMessage(featureName);
            subscriptionBadge.setAttribute("title", message);
            if (!enabled) {
                subscriptionBadge.addEventListener("click", () =>
                    showSubscriptionDialog(featureName),
                );
                subscriptionBadge.style.cursor = "pointer";
            }
        })();

        promises.push(promise);
    });

    // Wait for all the promises to complete
    await Promise.all(promises);
}

function loadToolboxTool(
    header: JQuery,
    content: JQuery,
    toolId,
    openTool: boolean,
) {
    const toolboxElt = $("#toolbox");
    const label = header.text();

    // Where to insert the new tool? We want to keep them alphabetical except for More...which is always last,
    // so insert before the first one with text alphabetically greater than this (if any).
    if (toolboxElt.children().length === 0) {
        // none yet...this will be the "more" tool which we insert first.
        toolboxElt.append(header);
        toolboxElt.append(content);
    } else {
        let insertBefore = toolboxElt
            .children() // children() includes both the headers and the contents of the tools
            .filter(".ui-accordion-header") // we only want to sort this into the headers...
            .filter(function () {
                // Note that we aren't (as of 4.4) setting the "locale" of the browser to match the
                // UI language. In my tests, it's stuck at "en-US" (navigator.language). But if we ever do
                // set this, then this will do a better job of ordering. Meanwhile, no worse.
                return label.localeCompare($(this).text()) < 0;
            })
            .first();
        if (insertBefore.length === 0) {
            // Nothing is greater, but still insert before "More". Two children represent "More", so before the second last.
            insertBefore = $(
                toolboxElt.children()[toolboxElt.children.length - 2],
            );
        }
        header.insertBefore(insertBefore);
        content.insertBefore(insertBefore);
    }

    // if requested, open the tool that was just inserted
    if (openTool && toolbox.toolboxIsShowing()) {
        const adapter = getToolboxReactAdapter();
        if (adapter) {
            const toolId = header.attr("data-toolId");
            if (toolId) {
                adapter.setActiveToolByToolId(toolId);
            }
        } else {
            toolboxElt.accordion("refresh");
            const id = header.attr("id");
            const toolNumber = parseInt(
                id.substring(id.lastIndexOf("-") + 1),
                10,
            );
            toolboxElt.accordion("option", "active", toolNumber); // must pass as integer
        }
    }

    window.dispatchEvent(
        new CustomEvent("toolbox-tool-added", {
            detail: { toolId: toolId },
        }),
    );
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
        // so select and properly initialize the first one.
        let newToolName = $("#toolbox")
            .find("> h3")
            .first()
            .attr("data-toolId");
        if (!newToolName) {
            // This should never happen; we're just being defensive.
            // At one point (BL-5330) this code could run against the document in the wrong iframe
            // and fail to find the #toolbox div; then we get a null and end up saving
            // current tool as "undefined" with various bad results. Just in case it happens again
            // somehow, we hard code that in this situation we default to
            // the talking book tool.
            newToolName = "talkingBookTool";
        }
        const adapter = getToolboxReactAdapter();
        if (adapter) {
            adapter.setActiveToolByToolId(newToolName);
            return;
        }
        switchTool(newToolName);
    }
}
