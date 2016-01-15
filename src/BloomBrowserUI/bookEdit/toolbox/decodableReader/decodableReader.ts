/// <reference path="../toolbox.ts" />

class DecodableReaderModel implements ITabModel {
    restoreSettings(settings: string) {
        if (!model) model = new ReaderToolsModel();
        initializeDecodableReaderTool();
        if (settings['decodableReaderState']) {
            var state = libsynphony.dbGet('drt_state');
            if (!state) state = new DRTState();
            var decState = settings['decodableReaderState'];
            if (decState.startsWith("stage:")) {
                var parts = decState.split(";");
                state.stage = parseInt(parts[0].substring("stage:".length));
                var sort = parts[1].substring("sort:".length);
                model.setSort(sort);
            } else {
                // old state
                state.stage = parseInt(decState);
            }
            libsynphony.dbSet('drt_state', state);
        }
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // Enhance: at present, model is a global variable defined by readerToolsModel. Try to encapsulate it, or at least give a more specific name.
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            if (model) {
                model.doMarkup();
            }
        });

        $(container).find('.bloom-editable').focusin(function () {
            if (model) {
                model.noteFocus(this); // 'This' is the element that just got focus.
            }
        });

        $(container).find('.bloom-editable').keydown(function(e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (model.currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        model.redo();
                    } else {
                        model.undo();
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
        model.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!model.setMarkupType(1)) model.doMarkup();
    }

    hideTool() {
        model.setMarkupType(0);
    }

    updateMarkup() {
        model.doMarkup();
    }

    name() { return 'decodableReaderTool'; }
}

tabModels.push(new DecodableReaderModel());


// "region" ReaderSetup dialog
function CreateConfigDiv(title) {
    var dialogContents = $('<div id="synphonyConfig" title="' + title + '"/>').appendTo($(parentDocument()).find("body"));

    var html = '<iframe id="settings_frame" src="/bloom/bookEdit/toolbox/decodableReader/readerSetup/ReaderSetup.html" scrolling="no" ' +
        'style="width: 100%; height: 100%; border-width: 0; margin: 0" ' +
        'onload="document.getElementById(\'toolbox\').contentWindow.initializeReaderSetupDialog()"></iframe>';

    dialogContents.append(html);

    return dialogContents;
}

function parentDocument() {
    return window.parent.document;
}

function settingsFrameWindow() {
    return (<HTMLIFrameElement>parentDocument().getElementById('settings_frame')).contentWindow;
}

function showSetupDialog(showWhat) {

    var toolbox = window;
    var lm: LocalizationManager = (<any>toolbox).localizationManager;
    lm.loadStrings(getSettingsDialogLocalizedStrings(), null, function () {

        var title;
        if (showWhat == 'stages')
            title = lm.getText('ReaderSetup.SetUpDecodableReaderTool', 'Set up Decodable Reader Tool');
        else
            title = lm.getText('ReaderSetup.SetUpLeveledReaderTool', 'Set up Leveled Reader Tool');

        var dialogContents = CreateConfigDiv(title);

        var h = 580;
        var w = 720;
        var size = getAppropriateDialogSize(h, w);
        h = size[0];
        w = size[1];

        (<any>toolbox).model.setupType = showWhat;

        $(dialogContents).dialog({
            autoOpen: true,
            modal: true,
            buttons: (<any>{
                Help: {
                    // For consistency, I would have made this 'Common.Help', but we already had 'HelpMenu.Help Menu' translated
                    text: lm.getText('HelpMenu.Help Menu', 'Help'),
                    class: 'left-button',
                    click: function () {
                        settingsFrameWindow().postMessage('Help', '*');
                    }
                },
                OK: {
                    text: lm.getText('Common.OK', 'OK'),
                    click: function () {
                        settingsFrameWindow().postMessage('OK', '*');
                    }
                },

                Cancel: {
                    text: lm.getText('Common.Cancel', 'Cancel'),
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
function initializeReaderSetupDialog() {

    var model = (<any>window).model;

    var sourceMsg = 'Data\n' + JSON.stringify(model.getSynphony().source);
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