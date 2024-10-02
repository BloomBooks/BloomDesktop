/// <reference path="../typings/bundledFromTSC.d.ts" />
/// <reference path="../typings/jquery.i18n.custom.d.ts" />
/// <reference path="../lib/jquery.i18n.custom.ts" />

import * as $ from "jquery";
import { bootstrap } from "./js/bloomEditing";
import { EditableDivUtils } from "./js/editableDivUtils";
import "../lib/jquery.i18n.custom.ts"; //localize()
import "errorHandler";
import { theOneBubbleManager, BubbleManager } from "./js/bubbleManager";
import { renderDragActivityTabControl } from "./toolbox/dragActivity/DragActivityTabControl";

function getPageId(): string {
    const page = document.querySelector(".bloom-page");
    if (!page) {
        // alert is tempting here. But possibly the browser control has not yet been added
        // to its parent, so we won't see it, and the loading code will be frozen waiting for
        // a response to the alert. Hopefully the error will show up somewhere.
        throw new Error(
            "Could not find the div.bloom-page; this often means editablePage.ts is being compiled into a bundle where it does not belong"
        );
    }
    return page.getAttribute("id")!;
}
// This notification lets the C# know that this partciular page is ready to edit.
// It is important that this does not get pulled into any other compiled bundle,
// since it will generate errors when loaded into any page that does not have a .bloom-page.
document.addEventListener("DOMContentLoaded", () => {
    postString("editView/pageDomLoaded", getPageId());
});

// This allows strong typing to be done for exported functions.
// Note: although these function are exported, which I think is necessary to make this cross-iframe
// calling possible, they MUST not simply be imported. Doing so will pull this file into the importing
// bundle, which will cause errors when it is loaded into a page that does not have a .bloom-page
// and other things that are expected to be here. Rather, use code like this:
//      const exports = getEditablePageBundleExports();
//      return exports ? exports.getTheOneBubbleManager() : undefined;
// It is theoreically OK to import these functions to code that is only included in the page bundle,
// but I think it is unwise. It is so easy for an extra file to get imported into another bundle,
// and then it will bring this along, with disastrous results.
export interface IPageFrameExports {
    requestPageContent(): void;
    pageUnloading(): void;
    copySelection(): void;
    cutSelection(): void;
    pasteClipboard(): void;
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
    renderDragActivityTabControl(currentTab: number): void;
    showGamePromptDialog: (onlyIfEmpty: boolean) => void;
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
    pasteClipboard,
    makeElement,
    SetupElements,
    attachToCkEditor,
    removeImageId,
    changeImage
} from "./js/bloomEditing";
import { showGamePromptDialog } from "../bookEdit/toolbox/dragActivity/dragActivityTool";
export {
    getBodyContentForSavePage,
    requestPageContent,
    userStylesheetContent,
    pageUnloading,
    copySelection,
    cutSelection,
    pasteClipboard,
    makeElement,
    SetupElements,
    attachToCkEditor,
    removeImageId,
    changeImage,
    renderDragActivityTabControl,
    getTheOneBubbleManager,
    showGamePromptDialog
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

function getTheOneBubbleManager(): BubbleManager {
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
