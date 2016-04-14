import theOneLocalizationManager from '../lib/localizationManager/localizationManager';
import 'jquery-ui/jquery-ui-1.10.3.custom.min.js'; //for dialog()

export function SayHello() { alert('Hello from editViewFrame'); }

// These functions should be available for calling by non-module code (such as C# directly)
// using the FrameExports object (see more details in BloomFrames.ts)
import {getToolboxFrameExports} from './js/BloomFrames';
export {getToolboxFrameExports};
import {getPageFrameExports} from './js/BloomFrames';
export {getPageFrameExports};
export {default as BloomHelp} from '../BloomHelp';

//Called by c# using FrameExports.handleUndo()
export function handleUndo(): void {
    // First see if origami is active and knows about something we can undo.
    var contentWindow = getPageFrameExports();
    if(contentWindow && (<any>contentWindow).origamiCanUndo()) {
        (<any>contentWindow).origamiUndo();
    }
    // Undoing changes made by commands and dialogs in the toolbox can't be undone using
    // ckeditor, and has its own mechanism. Look next to see whether we know about any Undos there.
    var toolboxWindow = getToolboxFrameExports();
    if(toolboxWindow && (<any>toolboxWindow).canUndo()) {
        (<any>toolboxWindow).undo();
    } // elsewhere, we try to ask ckEditor to undo, else just the document
    else {
        // reviewslog: I don't think this will ever be executed given the current definition of canUndo.
        // I've tried to update it to the FrameExports world but am not confident it is right.
        var ckEditorUndo = this.ckEditorUndoCommand();
        if(ckEditorUndo === null || !ckEditorUndo.exec()) {
            //sometimes ckEditor isn't active, so it wasn't paying attention, so it can't do the undo. So ask the document to do an undo:
            (<any>contentWindow).document.execCommand('undo', false, null);
        }
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
    /* I couldn't find a way to ask ckeditor if it is ready to do an undo.
      The "canUndo()" is misleading; what it appears to mean is, can this command (undo) be undone?*/

    /*  var ckEditorUndo = this.ckEditorUndoCommand();
        if (ckEditorUndo === null) return 'fail';
        return ckEditorUndo.canUndo() ? 'yes' : 'no';
    */

    return "fail"; //go ask the browser
}

// "region" Add Page dialog
function CreateAddPageDiv(templatesJSON) {

    var dialogContents = $('<div id="addPageConfig"/>').appendTo($('body'));

    // For some reason when the height is 100% we get an unwanted scroll bar on the far right.
    var html = "<iframe id=\"addPage_frame\" src=\"/bloom/pageChooser/page-chooser-main.html\" scrolling=\"no\" style=\"width: 100%; height: 99%; border: none; margin: 0\"></iframe>";

    dialogContents.append(html);

    // When the page chooser loads, send it the templatesJSON
    $('#addPage_frame').load(function() {
        initializeAddPageDialog(templatesJSON);
    });

    return dialogContents;
}

//noinspection JSUnusedGlobalSymbols
// method called from EditingModel.cs
// for 'templatesJSON', see property EditingModel.GetJsonTemplatePageObject
export function showAddPageDialog(templatesJSON) {

    var theDialog;
    
    //reviewSlog. I don't see why the localiationManager should live on the page. Where stuff is equally relevant to all frames,
    //it should if anything belong to the root frmate (this one)
    //var parentElement = (<any>document.getElementById('page')).contentWindow;
    //var lm = parentElement.localizationManager;
       
    // don't show if a dialog already exists
    if ($(document).find(".ui-dialog").length) {
        return;
    }
    var forChooseLayout = templatesJSON.chooseLayout;
    var key = 'EditTab.AddPageDialog.Title';
    var english = 'Add Page...';

    if (forChooseLayout) {
        var title = theOneLocalizationManager.getText('EditTab.AddPageDialog.ChooseLayoutTitle', 'Choose Different Layout...');

    } else {
        key = 'EditTab.AddPageDialog.ChooseLayoutTitle';
        english = 'Choose Different Layout...';
    }
        
    theOneLocalizationManager.asyncGetText(key, english).done(title => {
        var dialogContents = CreateAddPageDiv(templatesJSON);

        theDialog = $(dialogContents).dialog({
            //reviewslog Typescript didn't like this class: "addPageDialog",
            autoOpen: false,
            resizable: false,
            modal: true,
            width: 795,
            height: 550,
            position: {
                my: "left bottom", at: "left bottom", of: window
            },
            title: title,
            close: function() {
                $(this).remove();
                fireCSharpEvent('setModalStateEvent', 'false');
            },
        });

        //TODO:  this doesn't work yet. We need to make it work, and then make it localizationManager.asyncGetText(...).done(translation => { do the insertion into the dialog });
        // theDialog.find('.ui-dialog-buttonpane').prepend("<div id='hint'>You can press ctrl+N to add the same page again, without opening this dialog.</div>");
    
        jQuery(document).on('click', 'body > .ui-widget-overlay', function () {
            $(".ui-dialog-titlebar-close").trigger('click');
            return false;
        });
        fireCSharpEvent('setModalStateEvent', 'true');
        theDialog.dialog('open');

        //parentElement.$.notify("testing notify",{});
    });
}

//noinspection JSUnusedGlobalSymbols
// Used by the addPage_frame to initialize the setup dialog with the available template pages
// 'templatesJSON' will be something like:
//([{ "templateBookFolderUrl": "/bloom/localhost//...(path to files).../factoryCollections/Templates/Basic Book/", 
//      "templateBookUrl": "/bloom/localhost/...(path to files).../factoryCollections/Templates/Basic Book/Basic Book.htm" }])
// See property EditingModel.GetJsonTemplatePageObject
function initializeAddPageDialog(templatesJSON) {
    var templateMsg = 'Data\n' + JSON.stringify(templatesJSON);
    (<any>document.getElementById('addPage_frame')).contentWindow.postMessage(templateMsg, '*');
}
// "endregion" Add Page dialog

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 */
// Enhance: JT notes that this method pops up from time to time; can we consolidate?
function fireCSharpEvent(eventName, eventData) {

    var event = new MessageEvent(eventName, {/*'view' : window,*/ 'bubbles' : true, 'cancelable' : true, 'data' : eventData});
    document.dispatchEvent(event);
    // For when we someday change this file to TypeScript... since the above ctor is not declared anywhere.
    // Solution III (works)
    //var event = new (<any>MessageEvent)(eventName, { 'view': window, 'bubbles': true, 'cancelable': true, 'data': eventData });
}

