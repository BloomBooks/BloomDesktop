/// <reference path="../typings/bundledFromTSC.d.ts" />
/// <reference path="../typings/jquery.i18n.custom.d.ts" />
/// <reference path="../lib/jquery.i18n.custom.ts" />

import * as $ from "jquery";
import { bootstrap } from "./js/bloomEditing";
import { EditableDivUtils } from "./js/editableDivUtils";
import "../lib/jquery.i18n.custom.ts"; //localize()
import "errorHandler";
import { theOneBubbleManager, BubbleManager } from "./js/bubbleManager";

function getPageId(): string {
    const page = document.querySelector(".bloom-page");
    if (!page) throw new Error("Could not find the div.bloom-page");
    return page.getAttribute("id")!;
}
// This notification lets the C# know that this partciular page is ready to edit.
// It is important that this does not get pulled into any other compiled bundle,
// since it will generate errors when loaded into any page that does not have a .bloom-page.
document.addEventListener("DOMContentLoaded", () => {
    postString("editView/pageDomLoaded", getPageId());
});

// This allows strong typing to be done for exported functions
export interface IPageFrameExports {
    requestPageContent(): void;
    pageUnloading(): void;
    copySelection(): void;
    cutSelection(): void;
    pasteClipboardText(): void;
    makeElement(
        html: string,
        parent?: JQuery,
        resizableArgs?,
        draggableArgs?
    ): JQuery;
    SetupElements(container: HTMLElement): void;
    attachToCkEditor(element: any): void;

    origamiCanUndo(): boolean;
    origamiUndo(): void;

    getTheOneBubbleManager(): BubbleManager;

    ckeditorCanUndo(): boolean;
    ckeditorUndo(): void;

    SayHello(): void;
}

// This exports the functions that should be accessible from other IFrames or from C#.
// For example, editTabBundle.getEditablePageBundleExports().requestPageContent() can be called.
import {
    getBodyContentForSavePage,
    requestPageContent,
    userStylesheetContent,
    pageUnloading,
    copySelection,
    cutSelection,
    pasteClipboardText,
    makeElement,
    SetupElements,
    attachToCkEditor,
    changeImage
} from "./js/bloomEditing";
export {
    getBodyContentForSavePage,
    requestPageContent,
    userStylesheetContent,
    pageUnloading,
    copySelection,
    cutSelection,
    pasteClipboardText,
    makeElement,
    SetupElements,
    attachToCkEditor,
    changeImage
};
import { origamiCanUndo, origamiUndo } from "./js/origami";
import { postString } from "../utils/bloomApi";
export { origamiCanUndo, origamiUndo };

const styleSheets = [
    "themes/bloom-jqueryui-theme/jquery-ui-1.8.16.custom.css",
    "themes/bloom-jqueryui-theme/jquery-ui-dialog.custom.css",
    "lib/jquery.qtip.css",
    "bookEdit/css/qtipOverrides.css",
    "js/toolbar/jquery.toolbars.css",
    "bookEdit/css/origami.css",
    "bookEdit/css/origamiEditing.css",
    "bookEdit/css/tab.winclassic.css",
    "StyleEditor/StyleEditor.css",
    "bookEdit/TextBoxProperties/TextBoxProperties.css",
    "bookEdit/css/bloomDialog.css",
    "lib/long-press/longpress.css",
    "bookEdit/toolbox/talkingBook/audioRecording.css",
    "react_components/playbackOrderControls.css",
    "bookEdit/css/legacyQuizEditing.css"
];

export function getTheOneBubbleManager(): BubbleManager {
    return theOneBubbleManager;
}

// This is using an implementation secret of a particular version of ckeditor; but it seems to
// be the only way to get at whether ckeditor thinks there is something it can undo.
// And we really NEED to get at the ckeditor undo mechanism, since ckeditor intercepts paste
// in such a way that after a paste the C# browser object answers false to CanUndo.
export function ckeditorCanUndo(): boolean {
    // review: do we need to examine all instances?
    // C# may apparently call this before the module that defines the variable CKEDITOR
    // is even loaded. To avoid an error, we have to check both that it is defined AND
    // that it has a value.
    if (
        typeof CKEDITOR !== "undefined" &&
        CKEDITOR &&
        CKEDITOR.currentInstance &&
        (<any>CKEDITOR.currentInstance).undoManager &&
        (<any>CKEDITOR.currentInstance).undoManager.undoable()
    ) {
        return true;
    }
    return false;
}

export function ckeditorUndo() {
    // review: do we need to examine all instances?
    (<any>CKEDITOR.currentInstance).undoManager.undo();
}

for (let j = 0; j < styleSheets.length; j++) {
    document.write(
        '<link rel="stylesheet" type="text/css" href="/bloom/' +
            styleSheets[j] +
            '">'
    );
}

// TODO: move script stuff out of book.AddJavaScriptForEditing() and into here:
//var scripts = [
//     'lib/localizationManager/localizationManager.js',
//     'lib/jquery.i18n.custom.js',

// ];

// for (var i = 0; i < scripts.length; i++) {
//     document.write('<script type="text/javascript" src="/bloom/' + scripts[i] + '"></script>');
// }

//PasteImageCredits() is called by a script tag on a <a> element in a tooltip
window["PasteImageCredits"] = () => {
    EditableDivUtils.pasteImageCredits();
};

$(document).ready(() => {
    $("body")
        .find("*[data-i18n]")
        .localize();
    bootstrap();

    // If the user clicks outside of the page thumbnail context menu, we want to close it.
    // Since it is currently a winforms menu, we do that by sending a message
    // back to c#-land. We have a similar listener in the pageThumbnailList itself.
    // Note that to receive this, the c# code must be listening on this iframe.
    // We can remove this in 5.6, or whenever we replace the winforms context menu with a react menu.
    $(window).click(() => {
        (window as any).chrome.webview.postMessage("browser-clicked");
    });
});

export function SayHello() {
    alert("hello from editable page frame.");
}
