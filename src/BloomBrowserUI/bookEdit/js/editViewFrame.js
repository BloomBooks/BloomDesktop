
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
    var title = 'Set up ' + (showWhat == 'stages' ? 'Decodable' : 'Leveled') + ' Reader Tool';
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
            "OK": function () {
                document.getElementById('settings_frame').contentWindow.postMessage('OK', '*');
            },
            "Cancel": function () {
                $(this).dialog("close");
            }
        },
        close: function() {
            $(this).remove();
            document.getElementById('accordion').contentWindow.SynphonyApi.fireCSharpEvent('setModalStateEvent', 'false');
        },
        open: function () { $('#synphonyConfig').css('overflow', 'hidden'); },
        height: h,
        width: w
    });

    accordion.SynphonyApi.fireCSharpEvent('setModalStateEvent', 'true');
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