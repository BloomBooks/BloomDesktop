/// <reference path="../toolbox.ts" />
/// <reference path="./directoryWatcher.ts" />
/// <reference path="./readerToolsModel.ts" />

import {DirectoryWatcher} from "./directoryWatcher";
import {DRTState, ReaderToolsModel, MarkupType} from "./readerToolsModel";
import {beginInitializeDecodableReaderTool} from "./readerTools";
import {ITabModel} from "../toolbox";
import {ToolBox} from "../toolbox";
import theOneLocalizationManager from '../../../lib/localizationManager/localizationManager';
import {theOneLibSynphony}  from './libSynphony/synphony_lib';

class DecodableReaderModel implements ITabModel {
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        if (!ReaderToolsModel.model) ReaderToolsModel.model = new ReaderToolsModel();
        return beginInitializeDecodableReaderTool().then(() => {
            if (settings['decodableReaderState']) {
                var state = theOneLibSynphony.dbGet('drt_state');
                if (!state) state = new DRTState();
                var decState = settings['decodableReaderState'];
                if (decState.startsWith("stage:")) {
                    var parts = decState.split(";");
                    var stage = parseInt(parts[0].substring("stage:".length));
                    var sort = parts[1].substring("sort:".length);
                    ReaderToolsModel.model.setSort(sort);
                    ReaderToolsModel.model.setStageNumber(stage);
                } else {
                    // old state
                    ReaderToolsModel.model.setStageNumber(parseInt(decState));
                }
            }
        });
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            if (ReaderToolsModel.model) {
                ReaderToolsModel.model.doMarkup();
            }
        });

        $(container).find('.bloom-editable').focusin(function () {
            if (ReaderToolsModel.model) {
                ReaderToolsModel.model.noteFocus(this); // 'This' is the element that just got focus.
            }
        });

        $(container).find('.bloom-editable').keydown(function(e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (ReaderToolsModel.model.currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        ReaderToolsModel.model.redo();
                    } else {
                        ReaderToolsModel.model.undo();
                    }
                    return false;
                }
            }
        });
    }

    configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }

    showTool() {
        // change markup based on visible options
        ReaderToolsModel.model.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!ReaderToolsModel.model.setMarkupType(1)) ReaderToolsModel.model.doMarkup();
    }

    hideTool() {
        ReaderToolsModel.model.setMarkupType(0);
    }

    updateMarkup() {
        ReaderToolsModel.model.doMarkup();
    }

    name() { return 'decodableReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new DecodableReaderModel());


// "region" ReaderSetup dialog
function CreateConfigDiv(title) {
    var dialogContents = $('<div id="synphonyConfig" title="' + title + '"/>').appendTo($(parentDocument()).find("body"));

    var html = '<iframe id="settings_frame" src="/bloom/bookEdit/toolbox/decodableReader/readerSetup/ReaderSetup.html" scrolling="no" ' +
        'style="width: 100%; height: 100%; border-width: 0; margin: 0" ' +
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

export function showSetupDialog(showWhat) {

    //var toolbox = window;
    theOneLocalizationManager.loadStrings(getSettingsDialogLocalizedStrings(), null, function () {

        var title;
        if (showWhat == 'stages')
            title = theOneLocalizationManager.getText('ReaderSetup.SetUpDecodableReaderTool', 'Set up Decodable Reader Tool');
        else
            title = theOneLocalizationManager.getText('ReaderSetup.SetUpLeveledReaderTool', 'Set up Leveled Reader Tool');

        var dialogContents = CreateConfigDiv(title);

        var h = 580;
        var w = 720;
        var size = getAppropriateDialogSize(h, w);
        h = size[0];
        w = size[1];

        ReaderToolsModel.model.setupType = showWhat;

        $(dialogContents).dialog({
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
                        $(this).dialog("close");
                    }
                }
            }),
            close: function () {
                $(this).remove();
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

    var model = ReaderToolsModel.model;

    if(typeof model.synphony.source == 'undefined' || model.synphony.source === null){
        throw new Error("ReaderToolsModel.model was not loaded with settings");
    }
    var sourceMsg = 'Data\n' + JSON.stringify(model.synphony.source);
    var fontMsg = 'Font\n' + model.fontName;
    settingsFrameWindow().postMessage(sourceMsg, '*');
    settingsFrameWindow().postMessage(fontMsg, '*');
}

/**
 * Called by C# after the setup data has been saved, following Save click.
 */
function closeSetupDialog() {
    $(parentDocument()).find('#synphonyConfig').dialog("close");
}

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 */
// Enhance: JT notes that this method pops up from time to time; can we consolidate?
function fireCSharpEvent(eventName, eventData) {

    var event = new MessageEvent(eventName, {'bubbles': true, 'cancelable': true, 'data': eventData });
    document.dispatchEvent(event);
    // For when we someday change this file to TypeScript... since the above ctor is not declared anywhere.
    // Solution III (works)
    //var event = new (<any>MessageEvent)(eventName, { 'view': window, 'bubbles': true, 'cancelable': true, 'data': eventData });
}
// "endregion" ReaderSetup dialog