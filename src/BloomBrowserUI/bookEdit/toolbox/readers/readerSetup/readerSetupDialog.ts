/* 
 * The methods here are refugees that had been encamped with DecodableReader
 * (which becaues DecodableReaderToolboxPanel), but which are used just as much for the
 * leveled reader.
 * Note that that these methods just get the dialog created and in the right home and able
 * to respond to Help, OK, and Cancel, whereas the
 * ReaderSetupUI is concerned with the inner workings of the dialog.
 */


/// <reference path="../readerToolsModel.ts" />

import {getTheOneReaderToolsModel} from "../readerToolsModel";
import theOneLocalizationManager from '../../../../lib/localizationManager/localizationManager';
import {getEditViewFrameExports} from '../../../js/bloomFrames';


function getDialogHtml(title) {
    var dialogContents = $('<div id="synphonyConfig" title="' + title + '"/>').appendTo($(parentDocument()).find("body"));

    var html = '<iframe id="settings_frame" src="/bloom/bookEdit/toolbox/readers/readerSetup/ReaderSetup.html" scrolling="no" ' +
        'style="width: 100%; height: 100%; border-width: 0; margin: 0; position: absolute" ' +
        'onload="document.getElementById(\'toolbox\').contentWindow.FrameExports.initializeReaderSetupDialog()"></iframe>';

    dialogContents.append(html);

    return dialogContents;
}

function parentDocument() {
    return window.parent.document;
}

function settingsFrameWindow() {
    return (<HTMLIFrameElement>parentDocument().getElementById('settings_frame')).contentWindow;
}

var setupDialogElement: JQuery;

export function showSetupDialog(showWhat) {
    //var toolbox = window;
    theOneLocalizationManager.loadStrings(getSettingsDialogLocalizedStrings(), null, function () {

        var title;
        if (showWhat == 'stages')
            title = theOneLocalizationManager.getText('ReaderSetup.SetUpDecodableReaderTool', 'Set up Decodable Reader Tool');
        else
            title = theOneLocalizationManager.getText('ReaderSetup.SetUpLeveledReaderTool', 'Set up Leveled Reader Tool');

        var h = 580;
        var w = 720;
        var size = getAppropriateDialogSize(h, w);
        h = size[0];
        w = size[1];

        getTheOneReaderToolsModel().setupType = showWhat;

        // The showDialog function is a device to get the dialog element and its JQuery wrapper created in the frame
        // where it is displayed. The main dialog() function doesn't work quite right (can't drag or resize it), and other functions
        // like dialog("close") don't do anything, if the wrapper is created in the toolbox frame.
        setupDialogElement = getEditViewFrameExports().showDialog( getDialogHtml(title), {
            autoOpen: true,
            modal: true,
            buttons: (<any>{
                Help: {
                    // For consistency, I would have made this 'Common.Help', but we already had 'HelpMenu.Help Menu' translated
                    text: theOneLocalizationManager.getText('HelpMenu.Help Menu', 'Help'),
                    class: 'left-button',
                    click: function () {
                        settingsFrameWindow().postMessage('Help', '*');
                    }
                },
                OK: {
                    text: theOneLocalizationManager.getText('Common.OK', 'OK'),
                    click: function () {
                        settingsFrameWindow().postMessage('OK', '*');
                    }
                },

                Cancel: {
                    text: theOneLocalizationManager.getText('Common.Cancel', 'Cancel'),
                    click: function () {
                        //nb: the element pointed to here by setupDialogElement is the same as "this"
                        //however, the jquery that you'd get by saying $(this) is *not* the same one as
                        //that stored in setupDialogElement. Ref BL-3331.
                      setupDialogElement.dialog("close");
                    }
                }
            }),
            close: function () {
                // $(this).remove(); uses the wrong document (see https://silbloom.myjetbrains.com/youtrack/issue/BL-3962)
                // the following derives from http://stackoverflow.com/questions/2864740/jquery-how-to-completely-remove-a-dialog-on-close
                setupDialogElement.dialog('destroy').remove();
                fireCSharpEvent('setModalStateEvent', 'false');
            },
            open: function () {
                $('#synphonyConfig').css('overflow', 'hidden');
                $('button span:contains("Help")').prepend('<i class="fa fa-question-circle"></i> ');
            },
            height: h,
            width: w
        });

        fireCSharpEvent('setModalStateEvent', 'true');
    });
}

function getAppropriateDialogSize(preferredHeight, preferredWidth) {
    var h = preferredHeight;
    var w = preferredWidth;

    // This height and width will fit inside the "800 x 600" settings
    var sw = parentDocument().body.scrollWidth;
    if (sw < 583) {
        h = 460;
        w = 390;
    }

    // This height and width will fit inside the "1024 x 586 Low-end netbook with windows Task bar" settings
    else if ((sw < 723) || (window.parent.innerHeight < 583)) {
        h = 460;
        w = 580;
    }

    return [h, w];
}

function getSettingsDialogLocalizedStrings() {
    // Without preloading these, they are not available when the dialog is created
    var pairs = {};
    pairs['ReaderSetup.SetUpDecodableReaderTool'] = 'Set up Decodable Reader Tool';
    pairs['ReaderSetup.SetUpLeveledReaderTool'] = 'Set up Leveled Reader Tool';
    pairs['HelpMenu.Help Menu'] = 'Help';
    pairs['Common.OK'] = 'OK';
    pairs['Common.Cancel'] = 'Cancel';
    return pairs;
}

//noinspection JSUnusedGlobalSymbols
/**
 * Used by the settings_frame to initialize the setup dialog
 */
export function initializeReaderSetupDialog() {
  if (typeof getTheOneReaderToolsModel().synphony.source == 'undefined' || getTheOneReaderToolsModel().synphony.source === null) {
    throw new Error("ReaderToolsModel was not loaded with settings");
  }
  var sourceMsg = 'Data\n' + JSON.stringify(getTheOneReaderToolsModel().synphony.source);
  var fontMsg = 'Font\n' + getTheOneReaderToolsModel().fontName;
  settingsFrameWindow().postMessage(sourceMsg, '*');
  settingsFrameWindow().postMessage(fontMsg, '*');
}

export function closeSetupDialog() {
  setupDialogElement.dialog("close");
}

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 */
// Enhance: JT notes that this method pops up from time to time; can we consolidate?
function fireCSharpEvent(eventName, eventData) {

    var event = new MessageEvent(eventName, {'bubbles': true, 'cancelable': true, 'data': eventData });
    top.document.dispatchEvent(event);
    // For when we someday change this file to TypeScript... since the above ctor is not declared anywhere.
    // Solution III (works)
    //var event = new (<any>MessageEvent)(eventName, { 'view': window, 'bubbles': true, 'cancelable': true, 'data': eventData });
}
