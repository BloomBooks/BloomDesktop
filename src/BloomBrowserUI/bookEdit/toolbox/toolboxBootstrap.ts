/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from "jquery";
import { getTheOneToolbox, applyToolboxStateToUpdatedPage, showOrHideTool_click, removeToolboxMarkup } from "./toolbox";
import { getTheOneReaderToolsModel } from "./readers/readerToolsModel";
import TalkingBookModel from "./talkingBook/talkingBook";
import { handleBookSettingCheckboxClick } from "./bookSettings/bookSettings";

// each of these exports shows up under this window's FrameExports object (see bloomFrames.ts)
// reviewslog: is this actually needed? Could these be be directly imported where they are used?
export { showOrHideTool_click };
export { removeToolboxMarkup };
export { showSetupDialog, initializeReaderSetupDialog, closeSetupDialog } from "./readers/readerSetup/readerSetupDialog";
export { addWordListChangedListener, beginSaveChangedSettings, makeLetterWordList } from "./readers/readerTools";
export { loadLongpressInstructions } from "../js/bloomEditing";
export { TalkingBookModel }; // one function is called by CSharp.


// called by click handler in jade; also, exporting something from it gets it included in the bundle.
export { handleBookSettingCheckboxClick };
export { getTheOneToolbox };

export function canUndo(): boolean {
    return getTheOneReaderToolsModel().shouldHandleUndo() && getTheOneReaderToolsModel().canUndo();
}

export function undo() {
    getTheOneReaderToolsModel().undo();
}

export function applyToolboxStateToPage() {
    applyToolboxStateToUpdatedPage();
}


$(document).ready(function () {
    getTheOneToolbox().initialize();
});
