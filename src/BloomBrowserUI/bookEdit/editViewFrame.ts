import {
    IConfirmDialogProps,
    showConfirmDialogFromOutsideReact
} from "../react_components/confirmDialog";
import {
    IColorPickerDialogProps,
    showColorPickerDialog as doShowColorPickerDialog
} from "../react_components/color-picking/colorPickerDialog";
import "../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js"; //for dialog()

export interface IEditViewFrameExports {
    showDialog(dialogContents: string | JQuery, options: any): JQuery;
    closeDialog(id: string): void;
    toolboxIsShowing(): boolean;
    doWhenToolboxLoaded(
        task: (toolboxFrameExports: IToolboxFrameExports) => any
    );
    getModalDialogContainer(): HTMLElement | null;
    showConfirmDialog(props: IConfirmDialogProps): void;
    showColorPickerDialog(props: IColorPickerDialogProps): void;
    showCopyrightAndLicenseDialog(imageUrl?: string): void;
    showEditViewTopicChooserDialog(): void;
}

export function SayHello() {
    alert("Hello!! from editViewFrame");
}

// These functions should be available for calling by non-module code (such as C# directly)
// using the editTabBundle object (see more details in bloomFrames.ts)
import { getToolboxBundleExports } from "./js/bloomFrames";
export { getToolboxBundleExports };
import { getEditablePageBundleExports } from "./js/bloomFrames";
export { getEditablePageBundleExports };
import { showPageChooserDialog } from "../pageChooser/PageChooserDialog";
export { showPageChooserDialog };

import "../lib/errorHandler";
import { showBookSettingsDialog } from "./bookSettings/BookSettingsDialog";
export { showBookSettingsDialog };
import { reportError } from "../lib/errorHandler";
import { IToolboxFrameExports } from "./toolbox/toolboxBootstrap";
import { showCopyrightAndLicenseInfoOrDialog } from "./copyrightAndLicense/CopyrightAndLicenseDialog";
import { showTopicChooserDialog } from "./TopicChooser/TopicChooserDialog";
import ReactDOM = require("react-dom");
import { FunctionComponentElement } from "react";
export { getImageUrlFromImageButton } from "./js/bloomImages";

//Called by c# using editTabBundle.handleUndo()
export function handleUndo(): void {
    // First see if origami is active and knows about something we can undo.
    const contentWindow = getEditablePageBundleExports();
    if (contentWindow && contentWindow.origamiCanUndo()) {
        contentWindow.origamiUndo();
    }
    // Undoing changes made by commands and dialogs in the toolbox can't be undone using
    // ckeditor, and has its own mechanism. Look next to see whether we know about any Undos there.
    const toolboxWindow = getToolboxBundleExports();
    if (toolboxWindow && toolboxWindow.canUndo()) {
        toolboxWindow.undo();
    } else if (contentWindow && contentWindow.ckeditorCanUndo()) {
        contentWindow.ckeditorUndo();
    }
    // See also Browser.Undo; if all else fails we ask the C# browser object to Undo.
}

export function switchContentPage(newSource: string) {
    try {
        if (
            this.getEditablePageBundleExports &&
            this.getEditablePageBundleExports()?.pageUnloading
        ) {
            this.getEditablePageBundleExports().pageUnloading();
        }
    } catch (e) {
        // It's not too unlikely something goes wrong while trying to unload the previous
        // page. If the user is clicking fast, it may not have had time to finish loading.
        // We try to guard against this with all the checks above and some in pageUnloading,
        // but it's disastrous if we fail to load the new page because of some problem
        // with the old one. (See BL-6296, BL-2634.) So do our best to report any remaning
        // problems, but don't let them stop us loading the new page.
        try {
            reportError(e.message, e.stack);
        } catch (e2) {
            // swallow
        }
    }
    const iframe = <HTMLIFrameElement>document.getElementById("page");
    // We want to call getToolboxBundleExports().applyToolboxStateToPage() to allow
    // any tool that is active to update its state to match the new page content.
    // This gets a bit complicated because we want the tool to actually see the new
    // content in the DOM, and it won't be present right when we set the src attribute.
    // The normal way to deal with this is to wait and call the function after the new
    // page is loaded. But sometimes (see comment below) the load event doesn't get
    // raised, so we have a fall-back.
    // Enhance: Should this use DOMContentLoaded instead of load?
    let handlerCalled = false;
    const handler = () => {
        handlerCalled = true;
        iframe.removeEventListener("load", handler);
        getToolboxBundleExports()!.applyToolboxStateToPage();
    };
    iframe.removeEventListener("load", handler);
    iframe.addEventListener("load", handler);
    iframe.src = newSource;
    // When we don't already have a video (either a new page, or it has been deleted),
    // and record a new one, we switchContentPage to make the new video show up.
    // And for no known reason, the load event never fires. There may possibly be
    // other cases since I have no explanation. Rather than leaving control state
    // permanently wrong, if the event is delayed much longer than expected we just
    // call the handler.
    window.setTimeout(() => {
        if (!handlerCalled) {
            handler();
        }
    }, 1500);
}

// This function allows code in the toolbox (or other) frame to create a dialog with dynamic content in the root frame
// (so that it can be dragged anywhere in the gecko window). The dialog() function behaves strangely (e.g., draggable doesn't work)
// if the jquery wrapper for the element is created in a different frame than the parent of the dialog element.
export function showDialog(
    dialogContents: string | JQuery,
    options: any
): JQuery {
    const dialogElement = $(dialogContents).appendTo($("body"));
    dialogElement.dialog(options);
    return dialogElement;
}

// This allows closing a dialog opened in the outer frame window. Apparently a dialog must be closed by
// code in the window that opened it.
export function closeDialog(id: string) {
    $("#" + id).dialog("close");
}

export function toolboxIsShowing() {
    return (<HTMLInputElement>$(document)
        .find("#pure-toggle-right")
        .get(0)).checked;
}

// Do this task when the toolbox is loaded. If it isn't already, we set a timeout and do it when we can.
// (The value passed to the task function will be the value from getToolboxBundleExports(). Unfortunately we
// haven't yet managed to declare a type for that, so I can't easily specify it here.)
export function doWhenToolboxLoaded(
    task: (toolboxFrameExports: IToolboxFrameExports) => any
) {
    const toolboxWindow = getToolboxBundleExports();
    if (toolboxWindow) {
        task(toolboxWindow);
    } else {
        setTimeout(() => {
            doWhenToolboxLoaded(task);
        }, 10);
    }
}

//Called by c# using editTabBundle.canUndo()
export function canUndo(): string {
    // See comments on handleUndo()
    const contentWindow = getEditablePageBundleExports();
    if (contentWindow && (<any>contentWindow).origamiCanUndo()) {
        return "yes";
    }
    const toolboxWindow = getToolboxBundleExports();
    if (toolboxWindow && toolboxWindow.canUndo && toolboxWindow.canUndo()) {
        return "yes";
    }
    if (contentWindow && contentWindow.ckeditorCanUndo()) {
        return "yes";
    }
    return "fail"; //can't undo in Javascript, possibly something in C# can?
}

//noinspection JSUnusedGlobalSymbols
// method called from EditingModel.cs
// for "templatesJSON", see property EditingModel.GetJsonTemplatePageObject

// Only one modal dialog can be open at a time, so we'll just make space for one.
export function getModalDialogContainer(): HTMLElement | null {
    return document.getElementById("modal-dialog-container");
}

export function ShowEditViewDialog(dialog: FunctionComponentElement<any>) {
    let root = document.getElementById("modal-dialog-react-root");
    // remove any left over dialog stuff
    if (root) {
        document.body.removeChild(root);
    }
    // add a place to root the React system.
    root = document.createElement("div");
    root.id = "modal-dialog-react-root";
    document.body.appendChild(root);
    // Note that modal dialogs actually create a sibling to this, they don't actually end up being children in the DOM.
    // Also note that if we call this twice, everything is fine: MUI doesn't seem to actually care if we remove the
    // root we called render on; it has already made a child of Body that it is using for the root of its dialog.
    ReactDOM.render(dialog, root);
}

export function showConfirmDialog(props: IConfirmDialogProps): void {
    showConfirmDialogFromOutsideReact(props);
}

export function showColorPickerDialog(props: IColorPickerDialogProps): void {
    doShowColorPickerDialog(props);
}

export function showCopyrightAndLicenseDialog(imageUrl?: string) {
    showCopyrightAndLicenseInfoOrDialog(imageUrl);
}

export function showEditViewTopicChooserDialog() {
    showTopicChooserDialog();
}
export function showEditViewBookSettingsDialog(
    initiallySelectedGroupIndex?: number
) {
    showBookSettingsDialog(initiallySelectedGroupIndex);
}
