import theOneLocalizationManager from '../lib/localizationManager/localizationManager';



export function SayHello(){alert ('sayHello');}
import {getToolboxFrameMethods} from './js/BloomFrames';
export {getToolboxFrameMethods};

// "region" Add Page dialog
function CreateAddPageDiv(templatesJSON) {

    var dialogContents = $('<div id="addPageConfig"/>').appendTo($('body'));

    // For some reason when the height is 100% we get an unwanted scroll bar on the far right.
    var html = "<iframe id=\"addPage_frame\" src=\"/bloom/pageChooser/page-chooser-main.htm\" scrolling=\"no\" style=\"width: 100%; height: 99%; border: none; margin: 0\"></iframe>";

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
      var lm = theOneLocalizationManager;
      
    // don't show if a dialog already exists
    if ($(document).find(".ui-dialog").length) {
        return;
    }
        lm.loadStrings(getAddPageDialogLocalizedStrings(), null, function() {

        var forChooseLayout = templatesJSON.chooseLayout;
        if (forChooseLayout) {
            var title = lm.getText('EditTab.AddPageDialog.ChooseLayoutTitle', 'Choose Different Layout...');

        } else {
            var title = lm.getText('EditTab.AddPageDialog.Title', 'Add Page...');
        }
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

function getAddPageDialogLocalizedStrings() {
    // Without preloading these, they are not available when the dialog is created
    var pairs = {};
    pairs['EditTab.AddPageDialog.Title'] = 'Add Page...';
    return pairs;
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

