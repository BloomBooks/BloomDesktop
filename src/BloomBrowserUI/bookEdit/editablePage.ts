/// <reference path="../typings/bundledFromTSC.d.ts" />
/// <reference path="../typings/jquery.i18n.custom.d.ts" />
// This is the root script for the editable page iframe, which gets transpiled into editablePageBundle.js.

// Note that code in this bundle depends on ckeditor.js also being loaded.
// Currently that is done using a regular script tag in the HTML, not via an import here.

import $ from "jquery";
import { bootstrap } from "./js/bloomEditing";
import { EditableDivUtils } from "./js/editableDivUtils";
import "../lib/long-press/jquery.longpress.js";
import "../lib/jquery.i18n.custom"; // side-effect: adds .localize() to $.fn (kept via sideEffects allow-list)
import "errorHandler";
import {
    theOneCanvasElementManager,
    CanvasElementManager,
} from "./js/canvasElementManager/CanvasElementManager";
import { kCanvasElementSelector } from "./toolbox/canvas/canvasElementConstants";
import { renderDragActivityTabControl } from "./js/AbovePageControls";

function getPageId(): string {
    const page = document.querySelector(".bloom-page");
    if (!page) {
        // alert is tempting here. But possibly the browser control has not yet been added
        // to its parent, so we won't see it, and the loading code will be frozen waiting for
        // a response to the alert. Hopefully the error will show up somewhere.
        throw new Error(
            "Could not find the div.bloom-page; this often means editablePage.ts is being compiled into a bundle where it does not belong",
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
//      return exports ? exports.getTheOneCanvasElementManager() : undefined;
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
        draggableArgs?,
    ): JQuery;
    SetupElements(container: HTMLElement): void;
    attachToCkEditor(element: any): void;

    origamiCanUndo(): boolean;
    origamiUndo(): void;

    getTheOneCanvasElementManager(): CanvasElementManager;

    ckeditorCanUndo(): boolean;
    ckeditorUndo(): void;
    imageOperationCanUndo(): boolean;
    imageOperationUndo(): boolean;

    addRequestPageContentDelay(id: string): void;
    removeRequestPageContentDelay(id: string): void;

    e2eSetActiveCanvasElementByIndex(index: number): boolean;
    e2eSetActivePatriarchBubbleOrFirstCanvasElement(): boolean;
    e2eDeleteLastCanvasElement(): void;
    e2eDuplicateActiveCanvasElement(): void;
    e2eDeleteActiveCanvasElement(): void;
    e2eClearActiveCanvasElement(): void;
    e2eSetActiveCanvasElementBackgroundColor(
        color: string,
        opacity: number,
    ): void;
    e2eGetActiveCanvasElementStyleSummary(): {
        textColor: string;
        outerBorderColor: string;
        backgroundColors: string[];
    };
    e2eResetActiveCanvasElementCropping(): void;
    e2eCanExpandActiveCanvasElementToFillSpace(): boolean;
    e2eOverrideCanExpandToFillSpace(value: boolean): boolean;
    e2eClearCanExpandToFillSpaceOverride(): void;

    SayHello(): void;
    renderDragActivityTabControl(currentTab: number): void;
    showGamePromptDialog: (onlyIfEmpty: boolean) => void;
}

// This exports the functions that should be accessible from other IFrames or from C#.
// For example, workspaceBundle.getEditablePageBundleExports().requestPageContent() can be called.
import {
    getBodyContentForSavePage,
    requestPageContent,
    captureContentForExternalProcessing,
    userStylesheetContent,
    pageUnloading,
    topBarButtonClick,
    copySelection,
    cutSelection,
    pasteClipboard,
    makeElement,
    SetupElements,
    attachToCkEditor,
    removeImageId,
    changeImage,
    imageOperationCanUndo,
    imageOperationUndo,
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
} from "./js/bloomEditing";
import { showGamePromptDialog } from "./toolbox/games/GameTool";
export {
    getBodyContentForSavePage,
    requestPageContent,
    captureContentForExternalProcessing,
    userStylesheetContent,
    pageUnloading,
    topBarButtonClick,
    copySelection,
    cutSelection,
    pasteClipboard,
    makeElement,
    SetupElements,
    attachToCkEditor,
    removeImageId,
    changeImage,
    imageOperationCanUndo,
    imageOperationUndo,
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
    renderDragActivityTabControl,
    getTheOneCanvasElementManager,
    showGamePromptDialog,
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
    "bookEdit/css/legacyQuizEditing.css",
];

function getTheOneCanvasElementManager(): CanvasElementManager {
    return theOneCanvasElementManager;
}

function getCanvasElementManagerForE2e(): CanvasElementManager {
    if (!theOneCanvasElementManager) {
        throw new Error("CanvasElementManager is not available.");
    }

    return theOneCanvasElementManager;
}

function getCanvasElementsForE2e(): HTMLElement[] {
    return Array.from(
        document.querySelectorAll(kCanvasElementSelector),
    ) as HTMLElement[];
}

let originalCanExpandToFillSpaceForE2e: (() => boolean) | undefined;

function e2eSetActiveCanvasElementByIndex(index: number): boolean {
    const element = getCanvasElementsForE2e()[index];
    if (!element) {
        return false;
    }

    getCanvasElementManagerForE2e().setActiveElement(element);
    return true;
}

function e2eSetActivePatriarchBubbleOrFirstCanvasElement(): boolean {
    const manager = getCanvasElementManagerForE2e();
    const patriarchBubble = manager.getPatriarchBubbleOfActiveElement?.();
    const patriarchContent = patriarchBubble?.content as
        | HTMLElement
        | undefined;
    if (patriarchContent) {
        manager.setActiveElement(patriarchContent);
        return true;
    }

    const firstCanvasElement = getCanvasElementsForE2e()[0];
    if (!firstCanvasElement) {
        return false;
    }

    manager.setActiveElement(firstCanvasElement);
    return true;
}

function e2eDeleteLastCanvasElement(): void {
    const elements = getCanvasElementsForE2e();
    const lastElement = elements[elements.length - 1];
    if (!lastElement) {
        return;
    }

    const manager = getCanvasElementManagerForE2e();
    manager.setActiveElement(lastElement);
    manager.deleteCurrentCanvasElement();
}

function e2eDuplicateActiveCanvasElement(): void {
    getCanvasElementManagerForE2e().duplicateCanvasElement();
}

function e2eDeleteActiveCanvasElement(): void {
    getCanvasElementManagerForE2e().deleteCurrentCanvasElement();
}

function e2eClearActiveCanvasElement(): void {
    getCanvasElementManagerForE2e().setActiveElement(undefined);
}

function e2eSetActiveCanvasElementBackgroundColor(
    color: string,
    opacity: number,
): void {
    getCanvasElementManagerForE2e().setBackgroundColor([color], opacity);
}

function e2eGetActiveCanvasElementStyleSummary(): {
    textColor: string;
    outerBorderColor: string;
    backgroundColors: string[];
} {
    const manager = getCanvasElementManagerForE2e();
    const textColorInfo = manager.getTextColorInformation?.();
    const bubbleSpec = manager.getSelectedItemBubbleSpec?.();

    return {
        textColor: textColorInfo?.color ?? "",
        outerBorderColor: bubbleSpec?.outerBorderColor ?? "",
        backgroundColors: bubbleSpec?.backgroundColors ?? [],
    };
}

function e2eResetActiveCanvasElementCropping(): void {
    getCanvasElementManagerForE2e().resetCropping?.();
}

function e2eCanExpandActiveCanvasElementToFillSpace(): boolean {
    return getCanvasElementManagerForE2e().canExpandToFillSpace();
}

function e2eOverrideCanExpandToFillSpace(value: boolean): boolean {
    const manager = getCanvasElementManagerForE2e();
    if (!originalCanExpandToFillSpaceForE2e) {
        originalCanExpandToFillSpaceForE2e =
            manager.canExpandToFillSpace.bind(manager);
    }

    manager.canExpandToFillSpace = () => value;
    return true;
}

function e2eClearCanExpandToFillSpaceOverride(): void {
    if (!originalCanExpandToFillSpaceForE2e) {
        return;
    }

    const manager = getCanvasElementManagerForE2e();
    manager.canExpandToFillSpace = originalCanExpandToFillSpaceForE2e;
    originalCanExpandToFillSpaceForE2e = undefined;
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
    // This doesn't work any more because we are now loading this code as a module,
    // which means it is loaded after the document is parsed.
    // document.write(
    //     '<link rel="stylesheet" type="text/css" href="/bloom/' +
    //         styleSheets[j] +
    //         '">',
    // );
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.type = "text/css";
    link.href = "/bloom/" + styleSheets[j];
    document.head.appendChild(link);
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
    $("body").find("*[data-i18n]").localize();
    bootstrap();
    // Step 1 of the off-screen page-capture handshake (see __bloomEditablePageReady in the
    // `declare global` block below): bootstrap()/SetupElements() has now run, so the synchronous
    // load-time DOM fix-ups (image sizing, canvas-element layout, etc.) are in place and it is safe
    // for the off-screen book processor to proceed to capture. Harmless no-op in the live editor,
    // which never reads this flag.
    window.__bloomEditablePageReady = true;

    // If the user clicks outside of the page thumbnail context menu, we want to close it.
    // Since it is currently a winforms menu, we do that by sending a message
    // back to c#-land. We have a similar listener in the pageThumbnailList itself.
    // Note that to receive this, the c# code must be listening on this iframe.
    // We can remove this in 5.6, or whenever we replace the winforms context menu with a react menu.
    $(window).click(() => {
        (window as any).chrome?.webview?.postMessage("browser-clicked");
    });
});

export function SayHello() {
    alert("hello from editable page frame.");
}

// Legacy global exposure: mimic old webpack window["editablePageBundle"] contract used by other iframes / C#
// NOTE: Keep this as a minimal curated surface: only expose functions intentionally callable cross-frame.
interface EditablePageBundleApi {
    requestPageContent: typeof requestPageContent;
    captureContentForExternalProcessing: typeof captureContentForExternalProcessing;
    getBodyContentForSavePage: typeof getBodyContentForSavePage;
    userStylesheetContent: typeof userStylesheetContent;
    pageUnloading: typeof pageUnloading;
    copySelection: typeof copySelection;
    cutSelection: typeof cutSelection;
    pasteClipboard: typeof pasteClipboard;
    topBarButtonClick: typeof topBarButtonClick;
    makeElement: typeof makeElement;
    SetupElements: typeof SetupElements;
    attachToCkEditor: typeof attachToCkEditor;
    removeImageId: typeof removeImageId;
    changeImage: typeof changeImage;
    imageOperationCanUndo: typeof imageOperationCanUndo;
    imageOperationUndo: typeof imageOperationUndo;
    origamiCanUndo: typeof origamiCanUndo;
    origamiUndo: typeof origamiUndo;
    getTheOneCanvasElementManager: typeof getTheOneCanvasElementManager;
    ckeditorCanUndo: typeof ckeditorCanUndo;
    ckeditorUndo: typeof ckeditorUndo;
    addRequestPageContentDelay: typeof addRequestPageContentDelay;
    removeRequestPageContentDelay: typeof removeRequestPageContentDelay;
    e2eSetActiveCanvasElementByIndex: typeof e2eSetActiveCanvasElementByIndex;
    e2eSetActivePatriarchBubbleOrFirstCanvasElement: typeof e2eSetActivePatriarchBubbleOrFirstCanvasElement;
    e2eDeleteLastCanvasElement: typeof e2eDeleteLastCanvasElement;
    e2eDuplicateActiveCanvasElement: typeof e2eDuplicateActiveCanvasElement;
    e2eDeleteActiveCanvasElement: typeof e2eDeleteActiveCanvasElement;
    e2eClearActiveCanvasElement: typeof e2eClearActiveCanvasElement;
    e2eSetActiveCanvasElementBackgroundColor: typeof e2eSetActiveCanvasElementBackgroundColor;
    e2eGetActiveCanvasElementStyleSummary: typeof e2eGetActiveCanvasElementStyleSummary;
    e2eResetActiveCanvasElementCropping: typeof e2eResetActiveCanvasElementCropping;
    e2eCanExpandActiveCanvasElementToFillSpace: typeof e2eCanExpandActiveCanvasElementToFillSpace;
    e2eOverrideCanExpandToFillSpace: typeof e2eOverrideCanExpandToFillSpace;
    e2eClearCanExpandToFillSpaceOverride: typeof e2eClearCanExpandToFillSpaceOverride;
    SayHello: typeof SayHello;
    renderDragActivityTabControl: typeof renderDragActivityTabControl;
    showGamePromptDialog: typeof showGamePromptDialog;
}

declare global {
    interface Window {
        editablePageBundle: EditablePageBundleApi;
        // ── Off-screen page-capture handshake (C# BookProcessor ⇆ this bundle) ──────────────────
        // The "process-book" feature (external/process-book API, used by BloomBridge to run
        // finished books through Bloom's browser-only page fix-ups) re-saves every page of a book
        // WITHOUT opening the live editor. For each page, C# loads it into a throwaway, off-screen
        // WebView2 and runs this three-step handshake against the two globals below:
        //
        //   1. C# polls window.__bloomEditablePageReady until it is true. We set it (once, in
        //      $(document).ready below) the moment bootstrap()/SetupElements() has applied the
        //      synchronous load-time DOM fix-ups (image sizing, canvas-element layout, ...).
        //   2. C# calls editablePageBundle.captureContentForExternalProcessing() (defined in
        //      bloomEditing.ts). That waits for any in-flight async DOM work to settle, then stashes
        //      the finished page onto window.__bloomExternalPageContent.
        //   3. C# polls window.__bloomExternalPageContent until it is non-empty and reads it back.
        //
        // Why globals + polling, rather than posting to the editView/pageContent API the way the live
        // editor's requestPageContent() does:
        //   - That API feeds the live EditingModel; reusing it off-screen would corrupt the real
        //     editor's state. We want the same page-cleanup output, delivered out-of-band.
        //   - C#'s JS runner on this path (RunJavascriptWithStringResult_Sync_Dangerous) is
        //     synchronous, so it can't directly await the capture function's internal async settle.
        //     A plain window field it can poll is the simplest bridge.
        // This looks fragile (two magic globals) but is well-contained: exactly one writer (the
        // capture fn) and one reader (BookProcessor), and every page gets its own fresh disposable
        // browser, so there is no stale-value or cross-page-bleed risk.
        //
        // Step 1's flag: set in $(document).ready below; read in BookProcessor.ProcessPage.
        __bloomEditablePageReady?: boolean;
        // Step 2/3's mailbox: the combined "body<SPLIT-DATA>userCss" string, or "ERROR: <message>".
        __bloomExternalPageContent?: string;
    }
}

window.editablePageBundle = {
    requestPageContent,
    captureContentForExternalProcessing,
    getBodyContentForSavePage,
    userStylesheetContent,
    pageUnloading,
    copySelection,
    cutSelection,
    pasteClipboard,
    topBarButtonClick,
    makeElement,
    SetupElements,
    attachToCkEditor,
    removeImageId,
    changeImage,
    imageOperationCanUndo: imageOperationCanUndo,
    imageOperationUndo: imageOperationUndo,
    origamiCanUndo,
    origamiUndo,
    getTheOneCanvasElementManager,
    ckeditorCanUndo,
    ckeditorUndo,
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
    e2eSetActiveCanvasElementByIndex,
    e2eSetActivePatriarchBubbleOrFirstCanvasElement,
    e2eDeleteLastCanvasElement,
    e2eDuplicateActiveCanvasElement,
    e2eDeleteActiveCanvasElement,
    e2eClearActiveCanvasElement,
    e2eSetActiveCanvasElementBackgroundColor,
    e2eGetActiveCanvasElementStyleSummary,
    e2eResetActiveCanvasElementCropping,
    e2eCanExpandActiveCanvasElementToFillSpace,
    e2eOverrideCanExpandToFillSpace,
    e2eClearCanExpandToFillSpaceOverride,
    SayHello,
    renderDragActivityTabControl,
    showGamePromptDialog,
};
