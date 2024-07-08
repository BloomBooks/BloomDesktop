/// <reference path="../../typings/jqueryui/jqueryui.d.ts" />

import "../../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js";
import "../../lib/jquery.i18n.custom";
import "../../lib/jquery.onSafe";
import axios from "axios";
import { get, postString, wrapAxios } from "../../utils/bloomApi";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { hookupLinkHandler } from "../../utils/linkHandler";
import { ckeditableSelector } from "../../utils/shared";
import { EditableDivUtils } from "../js/editableDivUtils";
import { DragActivityTool } from "./dragActivity/dragActivityTool";

export const isLongPressEvaluating: string = "isLongPressEvaluating";

/**
 * The html code for a check mark character
 * @type String
 */
const checkMarkString: string = "&#10004;";
const checkLeaveOffTool: string = "Visualizer";

let savedSettings: string;

let keypressTimer: any = null;

let showExperimentalTools: boolean; // set by Toolbox.initialize()

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
    newPageReady(); // called when a new page is displayed or tool is activated (called after showTool completes)
    detachFromPage(); // called when a page is going away AND before hideTool
    id(): string; // without trailing "Tool"!
    hasRestoredSettings: boolean;
    isAlwaysEnabled(): boolean;
    isExperimental(): boolean;
    // If this is true, the tool may only be selected on pages that have data-tool-id matching this tool's id.
    requiresToolId(): boolean;

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    finishToolLocalization(pane: HTMLElement);

    // Implement this if the tool uses React.
    // It should return the main content of the tool, which must be a single div.
    // (toolbox will construct the h3 element which goes along with it in the accordion
    // and set its data-toolId attr; this method is however responsible to
    // localize the content of the div.)
    // It may be unimplemented for older tools where beginAddTool() already knows
    // where to find an HTML file for the tool content.
    makeRootElement(): HTMLDivElement;
}

// The newer React tools also implement this interface
export interface IReactTool {
    // toolbox beginAddTool() calls this to determine if this is a Bloom Enterprise only tool
    toolRequiresEnterprise(): boolean;
}

// Class that represents the whole toolbox. Gradually we will move more functionality in here.
export class ToolBox {
    public toolboxIsShowing() {
        return (<HTMLInputElement>$(parent.window.document)
            .find("#pure-toggle-right")
            .get(0)).checked;
    }
    public toggleToolbox() {
        (<HTMLInputElement>$(parent.window.document)
            .find("#pure-toggle-right")
            .get(0)).click();
    }
    private builtToolbox: boolean = false;
    public adjustToolListForPage(page: HTMLElement) {
        const requiredToolId = page.getAttribute("data-tool-id");

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

            for (let i = 0; i < masterToolList.length; i++) {
                if (masterToolList[i].requiresToolId()) {
                    // We may need to add or remove the specified tool

                    // Adapt the tool object id to the value used as the ID of the element
                    // for that tool in the toolbox.
                    const toolId = ToolBox.addToolToString(
                        masterToolList[i].id()
                    );
                    // Get the header element that represents the tool in the DOM.
                    const toolHeader = toolbox.querySelector(
                        "[data-toolid='" +
                            ToolBox.addToolToString(toolId) +
                            "']"
                    ) as HTMLElement;
                    const haveTool = !!toolHeader;
                    const wantTool = requiredToolId === masterToolList[i].id();
                    if (haveTool !== wantTool) {
                        // add or remove as needed.
                        showOrHideTool(
                            "dummy", // required tools don't have check boxes.
                            ToolBox.addToolToString(masterToolList[i].id()),
                            wantTool
                        );
                    }
                }
            }
        };
        doAdjustment();
    }
    public configureElementsForTools(container: HTMLElement) {
        for (let i = 0; i < masterToolList.length; i++) {
            masterToolList[i].configureElements(container);
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
            */

            $(container)
                .find(".bloom-editable")
                .keyup(event => {
                    //don't do markup on cursor keys
                    if (event.keyCode >= 37 && event.keyCode <= 40) {
                        // this is check is another workaround for one scenario of BL-3490, but one that, as far as I can tell makes sense.
                        // if all they did was move the cursor, we don't need to look at markup.
                        //console.log("skipping markup on arrow key");
                        return;
                    }
                    handleKeyboardInput();
                })
                .on("compositionend", argument => {
                    // Keyman (and other IME's?) don't send keydown events, but do send compositionend events
                    // See https://silbloom.myjetbrains.com/youtrack/issue/BL-5440.
                    handleKeyboardInput();
                });
        }
    }

    public getTheOneDragActivityTool(): DragActivityTool | undefined {
        return DragActivityTool.theOneDragActivityTool;
    }

    // Append "Tool" to the tool name if it's not already there.
    // Put a space between the name and "Tool" if addSpace is true.
    public static addToolToString(
        toolName: string,
        addSpace: boolean = false
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

    public static getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById(
            "page"
        ) as HTMLIFrameElement;
    }

    // The body of the editable page, a root for searching for document content.
    public static getPage(): HTMLElement | null {
        const page = ToolBox.getPageFrame();
        if (!page || !page.contentWindow) return null;
        return page.contentWindow.document.body;
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

    private getEnabledExperimentalFeatures() {
        // Using axios directly because api calls for returning the promise.
        return axios.get("/bloom/api/app/enabledExperimentalFeatures");
    }
    private getEnabledTools() {
        // Using axios directly because api calls for returning the promise.
        return axios.get("/bloom/api/toolbox/enabledTools");
    }

    public static getShowExperimentalTools(): boolean {
        return showExperimentalTools;
    }

    // Called from document.ready, initializes the whole toolbox.
    public initialize(): void {
        // It seems (see BL-5330) that the toolbox code is loaded into the edit document as well as the
        // toolbox one. Nothing outside toolbox imports it directly, so it must be some indirect link.
        // It's important that this function is only hooked up to the real toolbox instance.
        $(parent.window.document).ready(() => {
            $(parent.window.document)
                .find("#pure-toggle-right")
                .change(function() {
                    showToolboxChanged(!this.checked);
                });
        });
        hookupLinkHandler();

        // Using axios directly because bloomApi doesn't support merging promises with .all
        wrapAxios(
            axios
                .all([
                    this.getEnabledExperimentalFeatures(),
                    this.getEnabledTools()
                ])
                .then(
                    axios.spread((experimentalFeatures, enabledTools) => {
                        // remove any experimental tools the user doesn't want
                        // TODO: give each experimental tool it's own setting once we have any experimental tools again.
                        // Presumably use the tool id as the keyword in the list of experimental features.
                        const toolsToLoad = enabledTools.data.split(",");
                        // remove any tools we don't know about. This might happen where settings were saved in a later version of Bloom.
                        for (let i = toolsToLoad.length - 1; i >= 0; i--) {
                            if (
                                !masterToolList.some(
                                    mod => mod.id() === toolsToLoad[i]
                                )
                            ) {
                                toolsToLoad.splice(i, 1);
                            }
                        }

                        for (let j = 0; j < masterToolList.length; j++) {
                            // add any tools we always show
                            if (
                                masterToolList[j].isAlwaysEnabled() &&
                                !toolsToLoad.includes(masterToolList[j].id())
                            ) {
                                toolsToLoad.push(masterToolList[j].id());
                            }
                        }

                        // for correct positioning and so we can find check boxes when adding others must load this one first,
                        // which means putting it last in the array.
                        toolsToLoad.push("settings");
                        $("#toolbox").hide();
                        const loadNextTool = () => {
                            if (toolsToLoad.length === 0) {
                                $("#toolbox").accordion({
                                    heightStyle: "fill"
                                });
                                $("body")
                                    .find("*[data-i18n]")
                                    .localize(); // run localization

                                get("currentUiLanguage", result => {
                                    const langName = result.data;

                                    const nodeList = document.querySelectorAll(
                                        ':not([data-i18n=""])'
                                    );
                                    for (let i = 0; i < nodeList.length; ++i) {
                                        const node = nodeList.item(i);

                                        if (!node.hasAttribute("data-i18n")) {
                                            // Nodes which don't have data-18n will match the selector that it's not equal to "",
                                            // but we definitely don't want to apply language text-specific markup to those non-leaf nodes.
                                            continue;
                                        }

                                        // TODO: This only works when the tool is loaded up for the first time.
                                        // It doesn't work if you open a new tool after the talking book tool is initialized for the first time.
                                        // TODO: How to re-translate when UI lang changed.
                                        const i18nId = node.getAttribute(
                                            "data-i18n"
                                        );
                                        if (!i18nId) {
                                            node.setAttribute("lang", langName);
                                        } else {
                                            // Double-check that it's actually in this language and not just using an English fallback
                                            theOneLocalizationManager
                                                .asyncGetTextInLang(
                                                    i18nId,
                                                    "",
                                                    langName,
                                                    ""
                                                )
                                                .done(result => {
                                                    if (result) {
                                                        node.setAttribute(
                                                            "lang",
                                                            langName
                                                        );
                                                    } else {
                                                        node.removeAttribute(
                                                            "lang"
                                                        ); // Or maybe set to "en" instead?
                                                    }
                                                });
                                        }
                                    }
                                });

                                // Now bind the window's resize function to the toolbox resizer
                                $(window).bind("resize", () => {
                                    clearTimeout(resizeTimer); // resizeTimer variable is defined outside of ready function
                                    resizeTimer = setTimeout(
                                        resizeToolbox,
                                        100
                                    );
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
                                const checkBoxId = nextToolId + "Check";
                                const toolId = ToolBox.addToolToString(
                                    nextToolId
                                );
                                beginAddTool(checkBoxId, toolId, false, () =>
                                    loadNextTool()
                                );
                            }
                        };
                        loadNextTool();
                    })
                )
        );
    }

    // Adds "lang" attributes into the DOM for toolbox elements which have internationalization. (AKA, have data-i18n)
    // TODO: This only works with non-React toolbox components. For now, we only need it for talking book tool though.
    public static insertLangAttributesIntoToolboxElements() {
        get("currentUiLanguage", result => {
            const langName = result.data;

            const nodeList = document.querySelectorAll(':not([data-i18n=""])');
            for (let i = 0; i < nodeList.length; ++i) {
                const node = nodeList.item(i);

                if (!node.hasAttribute("data-i18n")) {
                    // Nodes which don't have data-18n will match the selector that it's not equal to "",
                    // but we definitely don't want to apply language text-specific markup to those non-leaf nodes.
                    continue;
                }

                const i18nId = node.getAttribute("data-i18n");
                if (!i18nId) {
                    node.setAttribute("lang", langName);
                } else {
                    // Double-check that it's actually in this language and not just using an English fallback
                    theOneLocalizationManager
                        .asyncGetTextInLang(i18nId, "", langName, "")
                        .done(result => {
                            if (result) {
                                node.setAttribute("lang", langName);
                            } else {
                                node.removeAttribute("lang"); // Or maybe set to "en" instead?
                            }
                        });
                }
            }
        });
    }

    //currently just a wrapper around the global, to be enhanced someday when we get rid of all the globals
    public getToolIfOffered(toolId: string): ITool {
        return getITool(toolId);
    }

    // Returns 'true' if the checkbox in the More... tab for the requested tool (w/"Tool" suffix!) is checked.
    public isToolActive(toolId: string): boolean {
        const tools = $("*[data-toolId]");
        const filteredTools = tools.filter(function() {
            return $(this).attr("data-toolId") === toolId;
        });
        return filteredTools.length > 0;
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

        if (!this.toolboxIsShowing()) {
            this.toggleToolbox();
        }
        const checkBox = $("#" + toolId + "Check").get(0) as HTMLDivElement;
        if (checkBox) {
            // if it was an actual "input" element, we would just check for "checked",
            // but it's actually a div with possibly a checkmark character inside,
            // so just check string length.
            if (checkBox.innerText.length === 0) {
                checkBox.click(); // will also activate
            } else {
                setCurrentTool(toolId);
            }
        } else {
            // no corresponding checkbox, probably a required tool for this page type
            const toolbox = document.getElementById("toolbox") as HTMLElement;
            const toolHeader = toolbox.querySelector(
                "[data-toolid='" + ToolBox.addToolToString(toolId) + "']"
            ) as HTMLElement;
            if (toolHeader) {
                // Review: do we want to force the tool to be current?
                //setCurrentTool(toolId);
            } else {
                showOrHideTool("dummy", ToolBox.addToolToString(toolId), true);
            }
        }
    }

    public getCurrentTool() {
        return currentTool;
    }
}

const toolbox = new ToolBox();

export function getTheOneToolbox() {
    return toolbox;
}

// Array of ITool objects, typically one for each tool. The code for each tool inserts an appropriate ITool
// into this array in order to interact with the overall toolbox code.
const masterToolList: ITool[] = [];
let currentTool: ITool | undefined = undefined;

/**
 * Handles the click event of the divs in Settings.htm that are styled to be check boxes.
 * @param chkbox
 */
export function showOrHideTool_click(chkbox) {
    const tool = $(chkbox).data("tool");
    const turnOn = chkbox.innerHTML === "";
    if (turnOn) {
        chkbox.innerHTML = checkMarkString;
        postString(
            "editView/saveToolboxSetting",
            "active\t" + chkbox.id + "\t1"
        );
    } else {
        chkbox.innerHTML = "";
        postString(
            "editView/saveToolboxSetting",
            "active\t" + chkbox.id + "\t0"
        );
    }
    showOrHideTool(chkbox.id, tool, turnOn);
}

function showOrHideTool(chkboxId: string, tool: string, turnOn: boolean) {
    if (turnOn) {
        beginAddTool(chkboxId, tool, true);
    } else {
        $("*[data-toolId]")
            .filter(function() {
                return $(this).attr("data-toolId") === tool;
            })
            .remove();
    }
    resizeToolbox();
}

export function restoreToolboxSettings() {
    get("toolbox/settings", result => {
        savedSettings = result.data;
        const pageFrame = ToolBox.getPageFrame();
        const contentWin = pageFrame.contentWindow;
        if (contentWin && contentWin.document.readyState === "loading") {
            // We can't finish restoring settings until the main document is loaded, so arrange to call the next stage when it is.
            $(contentWin.document).ready(e =>
                restoreToolboxSettingsWhenPageReady(result.data)
            );
            return;
        }
        restoreToolboxSettingsWhenPageReady(result.data); // not loading, we can proceed immediately.
    });
}

export function applyToolboxStateToUpdatedPage() {
    if (currentTool && toolbox.toolboxIsShowing()) {
        doWhenPageReady(() => {
            if (currentTool) {
                currentTool.newPageReady();
                // We used to call updateMarkup() here
                // Now we don't because it would mess up the Talking Book Tool
                // if you really need it, add call to updateMarkup to currentTool's implementation of newPageReady.
            }
        });
    }
}

function doWhenPageReady(action: () => void) {
    const page = ToolBox.getPage();
    if (!page || !ToolBox.getPageFrame()) {
        // Somehow, despite firing this function when the document is supposedly ready,
        // it may not really be ready when this is first called. If it doesn't even have a body yet,
        // we need to try again later.
        setTimeout(e => doWhenPageReady(action), 100);
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
            action: action
        },
        page
    );
}

function doWhenCkEditorReadyCore(
    arg: {
        removers: Array<any>;
        done: boolean;
        action: () => void;
    },
    page: HTMLElement
): void {
    if ((<any>ToolBox.getPageFrame().contentWindow).CKEDITOR) {
        const editorInstances = (<any>ToolBox.getPageFrame().contentWindow)
            .CKEDITOR.instances;
        // Somewhere in the process of initializing ckeditor, it resets content to what it was initially.
        // This wipes out (at least) our page initialization.
        // To prevent this we hold our initialization until CKEditor has done initializing.
        // If any instance on the page (e.g., one per div) is not ready, wait until all are.
        // Enhance: this logic is roughly duplicated in StyleEditor.ts function AttachToBox.
        // There may be some way to refactor it into a common place, but I don't know where.
        // (The instances property leads to an object in which each property is an instance of CkEditor)
        let gotOne = false;
        for (const property in editorInstances) {
            const instance = editorInstances[property];
            gotOne = true;
            if (!instance.instanceReady) {
                arg.removers.push(
                    instance.on("instanceReady", e => {
                        doWhenCkEditorReadyCore(arg, page);
                    })
                );
                return;
            }
        }
        if (!gotOne) {
            if (page.querySelector(ckeditableSelector)) {
                // If any editable divs exist, call us again once the page gets set up with ckeditor.
                // See BL-12381.
                arg.removers.push(
                    (<any>ToolBox.getPageFrame().contentWindow).CKEDITOR.on(
                        "instanceReady",
                        e => {
                            doWhenCkEditorReadyCore(arg, page);
                        }
                    )
                );
                return;
            }
        }
    }
    // OK, all CKEditors are ready (or page doesn't use it), we can finally do the action.
    if (!arg.done) {
        // We are the first call-back to find all ready! Any other editors invoking this should be ignored.
        arg.done = true; // ensures action only done once
        arg.removers.map(r => r.removeListener()); // try to prevent future callbacks for this action
        arg.action();
    }
}

function restoreToolboxSettingsWhenPageReady(settings: string) {
    doWhenPageReady(() => {
        // OK, CKEditor is done (or page doesn't use it), we can finally do the real initialization.
        const opts = settings;
        const currentTool = opts["current"] || "";

        // Before we set stage/level, as it initializes them to 1.
        setCurrentTool(currentTool);

        // Note: the bulk of restoring the settings (everything but which if any tool is active)
        // is done when a tool becomes current.
    });
}

// Remove any markup the toolbox is inserting. Called by a RunJavaScript() in EditingView
// before saving the page.
export function removeToolboxMarkup() {
    if (currentTool != null) {
        currentTool.detachFromPage();
    }
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
    if (currentTool !== newTool) {
        if (currentTool) {
            currentTool.detachFromPage();
            currentTool.hideTool();
        }
        if (newTool) {
            activateTool(newTool);
        }
        // Without recording that currentTool isn't defined, then returning from
        // More... to the same tool doesn't activate that tool.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-6720.
        currentTool = newTool ? newTool : undefined;
    }
}

function activateTool(newTool: ITool) {
    if (newTool && toolbox.toolboxIsShowing()) {
        const toolElt = getToolElement(newTool);
        // If we're activating this tool for the first time, restore its settings.
        if (!newTool.hasRestoredSettings) {
            newTool.hasRestoredSettings = true;
            newTool.beginRestoreSettings(savedSettings).then(() => {
                activateToolInternalAsync(newTool, toolElt);
            });
        } else {
            activateToolInternalAsync(newTool, toolElt);
        }
    }
}

function getToolElement(tool: ITool): HTMLElement | null {
    let toolElement: HTMLElement | null = null;
    if (tool) {
        const toolName = ToolBox.addToolToString(tool.id());
        $("#toolbox")
            .find("> h3")
            .each(function() {
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

async function activateToolInternalAsync(
    newTool: ITool,
    toolElt: HTMLElement | null
): Promise<void> {
    if (toolElt) {
        newTool.finishToolLocalization(toolElt);
    }

    // Await it so that we can guarantee that newPageReady() and insertLangAttributesIntoToolboxElements()
    // happen after showTool.
    await newTool.showTool();

    // Note: Allowed to begin some async work too, and we will await its result.
    // (This apparently solves the single flash mentioned in BL-10471.)
    await newTool.newPageReady();

    // Note: Begins some async work too, but currently no need to await its result.
    ToolBox.insertLangAttributesIntoToolboxElements();
}

/**
 * This function attempts to activate the tool whose "data-toolId" attribute is equal to the value
 * of "currentTool" (the last tool displayed).
 */
function setCurrentTool(toolID: string) {
    // NOTE: tools without a "data-toolId" attribute (such as the More tool) cannot be the "currentTool."
    let idx = 0;
    const toolbox = $("#toolbox");

    // I'm downright grumpy about how this code sometimes uses names with "Tool" appended, sometimes doesn't.
    // For now I'm just making functions work with either form.

    toolID = ToolBox.addToolToString(toolID);
    const accordionHeaders = toolbox.find("> h3");
    if (toolID) {
        let foundTool = false;
        // find the index of the tool whose "data-toolId" attribute equals the value of "currentTool"
        accordionHeaders.each(function() {
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
        toolID = toolbox
            .find("> h3")
            .first()
            .attr("data-toolId");
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
    return (<any>masterToolList).find(tool => tool.id() === reactToolId);
}

/**
 * Requests a tool from localhost and loads it into the toolbox.
 * This is used when the user ticks a previously unticked checkbox of a tool, or as part of
 * initializing the toolbox for those that are already checked.
 */
// these last three parameters were never used: function requestTool(checkBoxId, toolId, loadNextCallback, tools, currentTool) {
function beginAddTool(
    checkBoxId: string,
    toolId: string,
    openTool: boolean,
    whenLoaded?: () => void
): void {
    const chkBox = document.getElementById(checkBoxId);
    if (chkBox) {
        // always-enabled tools don't have checkboxes.
        chkBox.innerHTML = checkMarkString;
    }

    const subpath = {
        talkingBookTool: "talkingBook/talkingBookToolboxTool.html",
        decodableReaderTool:
            "readers/decodableReader/decodableReaderToolboxTool.html",
        leveledReaderTool:
            "readers/leveledReader/leveledReaderToolboxTool.html",
        toolboxSettingsTool:
            "toolboxSettingsTool/toolboxSettingsToolboxTool.html",
        settingsTool: "settings/Settings.html"
        // none for music: done in React
    };
    const subPathToPremadeHtml = subpath[toolId];
    if (subPathToPremadeHtml) {
        // old-style tool implemented in pug and typescript
        // Using axios because this is retrieving a file, not invoking an api,
        // so the required path does not start with /bloom/api/
        wrapAxios(
            axios
                .get("/bloom/bookEdit/toolbox/" + subPathToPremadeHtml)
                .then(result => {
                    loadToolboxToolText(result.data, toolId, openTool);
                    if (whenLoaded) {
                        whenLoaded();
                    }
                })
        );
    } else {
        // new-style tool implemented in React
        const tool = getITool(toolId);
        if (!tool) {
            console.error(
                `Tool ${toolId} not found, assuming that was from a different version of Bloom.`
            );
            return;
        }
        const content = $(tool.makeRootElement());
        const toolName = ToolBox.addToolToString(tool.id());
        // const parts = $("<h3 data-toolId='musicTool' data-i18n='EditTab.Toolbox.MusicTool'>"
        //     + "Music Tool</h3><div data-toolId='musicTool' class='musicBody'/>");

        const toolIdUpper =
            tool.id()[0].toUpperCase() +
            tool.id().substring(1, tool.id().length);
        let i18Id = "EditTab.Toolbox." + toolIdUpper;
        if (toolName.indexOf(checkLeaveOffTool) === -1) {
            i18Id += "Tool";
        }
        // Not sure this will always work, but we can do something more complicated...maybe a new method
        // on ITool...if we need it. Note that this is just a way to come up with the English,
        // we don't do it to localizations. But in English, the code value beats the xlf one.
        let toolLabel = toolIdUpper.replace(/([A-Z])/g, " $1").trim();
        toolLabel = ToolBox.addToolToString(toolLabel, true);
        const header = $(
            "<h3 data-i18n='" + i18Id + "'>" + toolLabel + "</h3>"
        );
        const reactTool = (tool as unknown) as IReactTool;
        const requiresEnterprise = reactTool
            ? reactTool.toolRequiresEnterprise()
            : false;
        // must both have this attr and value for removing if disabled.
        header.attr("data-toolId", toolName);
        content.attr("data-toolId", toolName);
        if (requiresEnterprise) {
            header.addClass("requiresEnterprise");
        }
        loadToolboxTool(header, content, toolId, openTool);
        if (whenLoaded) {
            whenLoaded();
        }
    }
}

let keydownEventCounter = 0;

function handleKeyboardInput(): void {
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
    keypressTimer = setTimeout(async () => {
        // This happens 500ms after the user stops typing.
        const page: HTMLIFrameElement = <HTMLIFrameElement>(
            parent.window.document.getElementById("page")
        );
        if (!page || !page.contentWindow) return; // unit testing?

        const selection: Selection | null = page.contentWindow.getSelection();
        const anchor: Node | null = selection ? selection.anchorNode : null;
        const active = anchor ? <HTMLDivElement>$(anchor)
                  .closest("div")
                  .get(0) : null;
        if (
            !active ||
            (selection &&
                (selection.rangeCount > 1 ||
                    (selection.rangeCount === 1 &&
                        !selection.getRangeAt(0).collapsed)))
        ) {
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
            const ckeditorOfThisBox = (<any>editableDiv).bloomCkEditor;
            // Normally every editable box has a ckeditor attached. But some arithmetic template boxes are
            // intended to contain numbers not needing translation and don't get one...because the logic
            // that invokes WireToCKEditor is looking for classes like bloom-content1 that are not present
            // in ArithmeticTemplate. Here we're presumng that if a block didn't get one attached,
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
                if (currentTool && toolbox.toolboxIsShowing()) {
                    if (currentTool.isUpdateMarkupAsync()) {
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

                        const actualUpdateFunc = await currentTool.updateMarkupAsync();
                        if (
                            keydownEventCounter ==
                            counterValueThatIdentifiesThisKeyDown
                        ) {
                            // go ahead and make the change. (If the counts are different,
                            // we got another keystroke, and initiated a new updatemarkup,
                            // while processing this one. We don't want to save the results
                            // of updating for the earlier keystroke.)
                            actualUpdateFunc();
                        }
                    } else {
                        // Note, the updateMarkup routine must be sure to use the result of
                        // ckEditor's getData() method, not the raw HTML of the editableDivs.
                        // See EditableDivUtils.doCkEditorCleanup() and .restoreSelectionFromCkEditorBookmarks().
                        // Unfortunately, we can't easily do that in a top-level (general for all tools) way because of
                        // our current architecture. Namely, the reader tools have a lower-level
                        // doMarkup() which gets called more than just from here.
                        currentTool.updateMarkup();
                    }
                }

                cleanUpNbsps(editableDiv);

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
    }, 500);
}

function RemoveNonPTags(editableDivHtml: string): string {
    return editableDivHtml
        .replace(/<[^p\/].*?>/g, "")
        .replace(/<\/[^p].*?>/g, "");
}

// Check if the &nbsp; is at the start or end of a paragraph, regardless of any other tags in between (e.g. the empty talking book spans)
function NbspIsOnEdgeOfParagraph(
    editableDivHtml: string,
    nbspIndex: number
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

    let i = -1;
    let j = -1;
    // Simultaneously loop through the text and the html, finding each corresponding nbsp.
    // The text shows us what the adjacent characters are so we can make a replacement decision, but
    // the actual replacement is done in the html so as to keep the markup.
    // We also make the replacements in the text as we go so that, for example,
    // if we change one nbsp to a regular space, that space prevents converting an adjacent nbsp.
    // eslint-disable-next-line no-constant-condition
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
                "Unexpected situation discovered in cleanUpNbsps. Html has nbsp but text doesn't."
            );
            console.error("editableDivHtml: " + editableDivHtml);
            console.error("editableDivText: " + editableDivText);

            // Restore the bookmarks. See comment above.
            if (originalBookMarkContent)
                setCkeditorBookmarkContent(
                    editableDiv,
                    originalBookMarkContent
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
        }
    }
    editableDiv.innerHTML = editableDivHtml;

    // Restore the bookmarks. See comment above.
    if (originalBookMarkContent)
        setCkeditorBookmarkContent(editableDiv, originalBookMarkContent);
}

// For the given div, replace the content of any ckeditor bookmarks with the given content.
// We actually only expect one bookmark for our case, but we're being safe.
// Return the original content (of the last one... we have to pick one...) so we can restore it later.
function setCkeditorBookmarkContent(
    editableDiv: HTMLElement,
    content: string
): string | undefined {
    let existingContent: string | undefined = undefined;

    const ckeBookmarks = editableDiv.querySelectorAll("[id^='cke_bm_']");
    ckeBookmarks.forEach(bm => {
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
    if (fixedHtml != editable.innerHTML) {
        editable.innerHTML = fixedHtml;
    }
}

let resizeTimer;
function resizeToolbox() {
    const windowHeight = $(window).height();
    const root = $(".toolboxRoot");
    // Set toolbox container height to fit in new window size
    // Then toolbox Resize() will adjust it to fit the container
    root.height(windowHeight - 25); // 25 is the top: value set for div.toolboxRoot in toolbox.less
    $("#toolbox").accordion("refresh");
}

/**
 * Adds one tool to the toolbox
 * @param {String} newContent
 * @param {String} toolId
 * @param {Boolean} openTool
 */
function loadToolboxToolText(
    newContent: string,
    toolId: string,
    openTool: boolean
) {
    const parts = $($.parseHTML(newContent, document, true));

    parts.filter("*[data-i18n]").localize();
    parts.find("*[data-i18n]").localize();

    // expect parts to have 2 items, an h3 and a div
    if (parts.length < 2) return;

    // get the toolbox tool label
    const header = parts.filter("h3").first();
    if (header.length < 1) return; // we used to have a tool that was empty and didn't get added.

    // get the tool content div
    const content = parts.filter("div").first();

    loadToolboxTool(header, content, toolId, openTool);
}
function loadToolboxTool(
    header: JQuery,
    content: JQuery,
    toolId,
    openTool: boolean
) {
    const toolboxElt = $("#toolbox");
    const label = header.text();
    if (toolId === "settingsTool" && !showExperimentalTools) {
        content.addClass("hideExperimental");
    }

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
            .filter(function() {
                // Note that we aren't (as of 4.4) setting the "locale" of the browser to match the
                // UI language. In my tests, it's stuck at "en-US" (navigator.language). But if we ever do
                // set this, then this will do a better job of ordering. Meanwhile, no worse.
                return label.localeCompare($(this).text()) < 0;
            })
            .first();
        if (insertBefore.length === 0) {
            // Nothing is greater, but still insert before "More". Two children represent "More", so before the second last.
            insertBefore = $(
                toolboxElt.children()[toolboxElt.children.length - 2]
            );
        }
        header.insertBefore(insertBefore);
        content.insertBefore(insertBefore);
    }

    // if requested, open the tool that was just inserted
    if (openTool && toolbox.toolboxIsShowing()) {
        toolboxElt.accordion("refresh");
        const id = header.attr("id");
        const toolNumber = parseInt(id.substring(id.lastIndexOf("-") + 1), 10);
        toolboxElt.accordion("option", "active", toolNumber); // must pass as integer
    }
}

function showToolboxChanged(wasShowing: boolean): void {
    postString(
        "editView/saveToolboxSetting",
        "visibility\t" + (wasShowing ? "" : "visible")
    );
    if (currentTool) {
        if (wasShowing) {
            currentTool.detachFromPage();
            currentTool.hideTool();
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
        switchTool(newToolName);
    }
}
