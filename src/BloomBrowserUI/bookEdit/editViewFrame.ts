import theOneLocalizationManager from '../lib/localizationManager/localizationManager';
import 'jquery-ui/jquery-ui-1.10.3.custom.min.js'; //for dialog()

export function SayHello() { alert('Hello from editViewFrame'); }

// These functions should be available for calling by non-module code (such as C# directly)
// using the FrameExports object (see more details in bloomFrames.ts)
import { getToolboxFrameExports } from './js/bloomFrames';
export { getToolboxFrameExports };
import { getPageFrameExports } from './js/bloomFrames';
export { getPageFrameExports };
import { showAddPageDialog } from '../pageChooser/launch-page-chooser';
export { showAddPageDialog };

//Called by c# using FrameExports.handleUndo()
export function handleUndo(): void {
        // First see if origami is active and knows about something we can undo.
        var contentWindow = getPageFrameExports();
        if (contentWindow && (<any>contentWindow).origamiCanUndo()) {
                (<any>contentWindow).origamiUndo();
        }
        // Undoing changes made by commands and dialogs in the toolbox can't be undone using
        // ckeditor, and has its own mechanism. Look next to see whether we know about any Undos there.
        var toolboxWindow = getToolboxFrameExports();
        if (toolboxWindow && (<any>toolboxWindow).canUndo()) {
                (<any>toolboxWindow).undo();
        }
        else if (contentWindow && contentWindow.ckeditorCanUndo()) {
                contentWindow.ckeditorUndo();
        }
        // See also Browser.Undo; if all else fails we ask the C# browser object to Undo.
}

export function switchContentPage(newSource: string) {
        let iframe = (<HTMLIFrameElement>document.getElementById('page'));
        iframe.src = newSource;
        // I don't fully understand why the load is necessary; it seems that without it
        // the old page content is still around and applyToolboxStateToPage() works on that
        // instead of the new page.
        $(iframe).load(() =>
                getToolboxFrameExports().applyToolboxStateToPage());
}

// This function allows code in the toolbox (or other) frame to create a dialog with dynamic content in the root frame
// (so that it can be dragged anywhere in the gecko window). The dialog() function behaves strangely (e.g., draggable doesn't work)
// if the jquery wrapper for the element is created in a different frame than the parent of the dialog element.
export function showDialog(dialogContents: string, options: any): JQuery {
        var dialogElement = $(dialogContents).appendTo($('body'));
        dialogElement.dialog(options);
        return dialogElement;
}

// This allows closing a dialog opened in the outer frame window. Apparently a dialog must be closed by
// code in the window that opened it.
export function closeDialog(id: string) {
        $('#' + id).dialog('close');
}

export function toolboxIsShowing() { return (<HTMLInputElement>$(document).find('#pure-toggle-right').get(0)).checked; }

// Do this task when the toolbox is loaded. If it isn't already, we set a timeout and do it when we can.
// (The value passed to the task function will be the value from getToolboxFrameExports(). Unfortunately we
// haven't yet managed to declare a type for that, so I can't easily specify it here.)
export function doWhenToolboxLoaded(task: (toolboxFrameExports: any) => any) {
        let toolboxWindow = getToolboxFrameExports();
        if (toolboxWindow) {
                task(toolboxWindow);
        }
        else {
                setTimeout(() => {
                        doWhenToolboxLoaded(task);
                }, 10);
        }
}

//Called by c# using FrameExports.canUndo()
export function canUndo(): string {
        // See comments on handleUndo()
        var contentWindow = getPageFrameExports();
        if (contentWindow && (<any>contentWindow).origamiCanUndo()) { return 'yes'; }
        var toolboxWindow = getToolboxFrameExports();
        if (toolboxWindow && (<any>toolboxWindow).canUndo()) {
                return 'yes';
        }
        if (contentWindow && contentWindow.ckeditorCanUndo()) {
                return 'yes';
        }
        return "fail"; //go ask the browser
}

//noinspection JSUnusedGlobalSymbols
// method called from EditingModel.cs
// for 'templatesJSON', see property EditingModel.GetJsonTemplatePageObject
