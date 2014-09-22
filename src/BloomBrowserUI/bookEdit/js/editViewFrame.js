function FindOrCreateConfigDiv(title) {

    var dialogContents = $('<div id="synphonyConfig" title="' + title + '"/>').appendTo($("body"));

    var html = '<iframe id="settings_frame" src="/bloom/bookEdit/readerSetup/ReaderSetup.htm" scrolling="no" ' +
        'style="width: 100%; height: 100%; border-width: 0; margin: 0" ' +
        'onload="initializeReaderSetupDialog();"></iframe>';

    dialogContents.append(html);

    return dialogContents;
}

function showSetupDialog(showWhat) {

    var accordion = document.getElementById('accordion').contentWindow;
    accordion.localizationManager.loadStrings(getSettingsDialogLocalizedStrings(), null, function() {

    var title;
    if (showWhat == 'stages')
        title = accordion.localizationManager.getText('ReaderSetup.SetUpDecodableReaderTool', 'Set up Decodable Reader Tool');
    else
        title = accordion.localizationManager.getText('ReaderSetup.SetUpLeveledReaderTool', 'Set up Leveled Reader Tool');
    var dialogContents = FindOrCreateConfigDiv(title);

    var h = 580;
    var w = 720;

    // This height and width will fit inside the "800 x 600" settings
    var sw = document.body.scrollWidth;
    if (sw < 583) {
        h = 460;
        w = 390;
    }

    // This height and width will fit inside the "1024 x 586 Low-end netbook with windows Task bar" settings
    else if ((sw < 723) || (window.innerHeight < 583)) {
        h = 460;
        w = 580;
    }

    accordion.model.setupType = showWhat;

    $(dialogContents).dialog({
        autoOpen: "true",
        modal: "true",
        buttons: {
            Help: {
                // For consistency, I would have made this 'Common.Help', but we already had 'HelpMenu.Help Menu' translated
                text: accordion.localizationManager.getText('HelpMenu.Help Menu', 'Help'),
                class: 'left-button',
                click: function() {
                    document.getElementById('settings_frame').contentWindow.postMessage('Help', '*');
                }
            },
            OK: {
                text: accordion.localizationManager.getText('Common.OK', 'OK'),
                click: function () {
                    document.getElementById('settings_frame').contentWindow.postMessage('OK', '*');
                }
            },

            Cancel: {
                text: accordion.localizationManager.getText('Common.Cancel', 'Cancel'),
                click: function () {
                    $(this).dialog("close");
                }
            }
        },
        close: function() {
            $(this).remove();
            document.getElementById('accordion').contentWindow.SynphonyApi.fireCSharpEvent('setModalStateEvent', 'false');
        },
        open: function () {
            $('#synphonyConfig').css('overflow', 'hidden');
            $('button span:contains("Help")').prepend('<i class="fa fa-question-circle"></i> ');
        },
        height: h,
        width: w
    });

    accordion.SynphonyApi.fireCSharpEvent('setModalStateEvent', 'true');
    });

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

    var model = document.getElementById('accordion').contentWindow.model;

    var sourceMsg = 'Data\n' +  JSON.stringify(model.getSynphony().source);
    var fontMsg = 'Font\n' +  model.fontName;
    document.getElementById('settings_frame').contentWindow.postMessage(sourceMsg, '*');
    document.getElementById('settings_frame').contentWindow.postMessage(fontMsg, '*');
}


/**
 * Called by C# after the setup data has been saved, following Save click.
 */
function closeSetupDialog() {
    $('#synphonyConfig').dialog("close");
}