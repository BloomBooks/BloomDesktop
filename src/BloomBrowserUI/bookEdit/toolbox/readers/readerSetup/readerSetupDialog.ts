/*
 * The methods here are refugees that had been encamped with DecodableReader
 * (which becaues DecodableReaderToolboxTool), but which are used just as much for the
 * leveled reader.
 * Note that that these methods just get the dialog created and in the right home and able
 * to respond to Help, OK, and Cancel, whereas the
 * ReaderSetupUI is concerned with the inner workings of the dialog.
 */

/// <reference path="../readerToolsModel.ts" />

import { getTheOneReaderToolsModel } from "../readerToolsModel";
import theOneLocalizationManager from "../../../../lib/localizationManager/localizationManager";
import { getEditTabBundleExports } from "../../../js/bloomFrames";
import { get, postBoolean } from "../../../../utils/bloomApi";
import $ from "jquery";

function getDialogHtml(title) {
    const dialogContents = $(
        '<div id="synphonyConfig" title="' + title + '"/>',
    ).appendTo($(parentDocument()).find("body"));

    const html =
        '<iframe id="settings_frame" src="/bloom/bookEdit/toolbox/readers/readerSetup/ReaderSetup.html" scrolling="no" ' +
        'style="width: 100%; height: 100%; border-width: 0; margin: 0; position: absolute" ' +
        "onload=\"document.getElementById('toolbox').contentWindow.toolboxBundle.initializeReaderSetupDialog()\"></iframe>";

    dialogContents.append(html);

    return dialogContents;
}

function parentDocument() {
    return window.parent.document;
}

function settingsFrameWindow() {
    return (<HTMLIFrameElement>(
        parentDocument().getElementById("settings_frame")
    )).contentWindow;
}

let setupDialogElement: JQuery;

export function showSetupDialog(showWhat) {
    //var toolbox = window;
    theOneLocalizationManager.loadStrings(
        getSettingsDialogLocalizedStrings(),
        null,
        () => {
            let title;
            if (showWhat === "stages")
                title = theOneLocalizationManager.getText(
                    "ReaderSetup.SetUpDecodableReaderTool",
                    "Set up Decodable Reader Tool",
                );
            else
                title = theOneLocalizationManager.getText(
                    "ReaderSetup.SetUpLeveledReaderTool",
                    "Set up Leveled Reader Tool",
                );

            let h = 600;
            let w = 720;
            const size = getAppropriateDialogSize(h, w);
            h = size[0];
            w = size[1];

            getTheOneReaderToolsModel().setupType = showWhat;

            get("readers/io/readerSettingsEditForbidden", (result) => {
                const buttons: any = {
                    Cancel: {
                        text: theOneLocalizationManager.getText(
                            "Common.Cancel",
                            "Cancel",
                        ),
                        click: () => {
                            //nb: the element pointed to here by setupDialogElement is the same as "this"
                            //however, the jquery that you'd get by saying $(this) is *not* the same one as
                            //that stored in setupDialogElement. Ref BL-3331.
                            setupDialogElement.dialog("close");
                        },
                    },
                };
                if (!result.data) {
                    buttons.Help = {
                        text: theOneLocalizationManager.getText(
                            "Common.Help",
                            "Help",
                        ),
                        class: "left-button",
                        click: () => {
                            const window = settingsFrameWindow();
                            if (window) window.postMessage("Help", "*");
                        },
                    };
                    buttons.OK = {
                        text: theOneLocalizationManager.getText(
                            "Common.OK",
                            "OK",
                        ),
                        click: () => {
                            const window = settingsFrameWindow();
                            if (window) window.postMessage("OK", "*");
                        },
                    };
                }
                // The showDialog function is a device to get the dialog element and its JQuery wrapper created in the frame
                // where it is displayed. The main dialog() function doesn't work quite right (can't drag or resize it), and other functions
                // like dialog("close") don't do anything, if the wrapper is created in the toolbox frame.
                setupDialogElement = getEditTabBundleExports().showDialog(
                    result.data
                        ? getSettingsForbidden(title, result.data)
                        : getDialogHtml(title),
                    {
                        autoOpen: true,
                        modal: true,
                        buttons,
                        close: () => {
                            // $(this).remove(); uses the wrong document (see https://silbloom.myjetbrains.com/youtrack/issue/BL-3962)
                            // the following derives from http://stackoverflow.com/questions/2864740/jquery-how-to-completely-remove-a-dialog-on-close
                            setupDialogElement.dialog("destroy").remove();
                            postBoolean("editView/setModalState", false);
                        },
                        open: () => {
                            $("#synphonyConfig").css("overflow", "hidden");
                            $('button span:contains("Help")').prepend(
                                '<i class="fa fa-question-circle"></i> ',
                            );
                        },
                        height: h,
                        width: w,
                    },
                );
            });
            postBoolean("editView/setModalState", true);
        },
    );
}

function getAppropriateDialogSize(preferredHeight, preferredWidth) {
    let h = preferredHeight;
    let w = preferredWidth;

    // This height and width will fit inside the "800 x 600" settings
    const sw = parentDocument().body.scrollWidth;
    if (sw < 583) {
        h = 460;
        w = 390;
    }

    // This height and width will fit inside the "1024 x 586 Low-end netbook with windows Task bar" settings
    else if (sw < 723 || window.parent.innerHeight < 583) {
        h = 460;
        w = 580;
    }

    return [h, w];
}

function getSettingsDialogLocalizedStrings() {
    // Without preloading these, they are not available when the dialog is created
    const pairs = {};
    pairs["ReaderSetup.SetUpDecodableReaderTool"] =
        "Set up Decodable Reader Tool";
    pairs["ReaderSetup.SetUpLeveledReaderTool"] = "Set up Leveled Reader Tool";
    pairs["Common.Help"] = "Help";
    pairs["Common.OK"] = "OK";
    pairs["Common.Cancel"] = "Cancel";
    return pairs;
}

//noinspection JSUnusedGlobalSymbols
/**
 * Used by the settings_frame to initialize the setup dialog
 */
export function initializeReaderSetupDialog() {
    if (
        typeof getTheOneReaderToolsModel().synphony.source === "undefined" ||
        getTheOneReaderToolsModel().synphony.source === null
    ) {
        throw new Error("ReaderToolsModel was not loaded with settings");
    }
    const sourceMsg =
        "Data\n" + JSON.stringify(getTheOneReaderToolsModel().synphony.source);
    const fontMsg = "Font\n" + getTheOneReaderToolsModel().fontName;
    const window = settingsFrameWindow();
    if (window) {
        window.postMessage(sourceMsg, "*");
        window.postMessage(fontMsg, "*");
    }
}

export function closeSetupDialog() {
    setupDialogElement.dialog("close");
}

// Get replacement settings dialog content when editing settings is forbidden.
// message is the value we got from the API which should explain why we can't edit them.
function getSettingsForbidden(title: string, message: any): string | JQuery {
    const dialogContents = $(
        '<div id="synphonyConfig" title="' + title + '"/>',
    ).appendTo($(parentDocument()).find("body"));

    // overide some styles from Dialog to make it compatible with the main title.
    const html = `<div style="padding: 10px; color: black;font-size:10pt;">${message}</div>`;

    dialogContents.append(html);

    return dialogContents;
}
