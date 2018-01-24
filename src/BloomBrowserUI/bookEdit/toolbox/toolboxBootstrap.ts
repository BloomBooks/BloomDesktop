/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from "jquery";
import { getTheOneToolbox, applyToolboxStateToUpdatedPage, showOrHidePanel_click, removeToolboxMarkup } from "./toolbox";
import { getTheOneReaderToolsModel } from "./readers/readerToolsModel";
import TalkingBookModel from "./talkingBook/talkingBook";
import DecodableReaderToolboxPanel from "./readers/decodableReader/decodableReaderToolboxPanel";
import LeveledReaderToolboxPanel from "./readers/leveledReader/leveledReaderToolboxPanel";
import { handleBookSettingCheckboxClick } from "./bookSettings/bookSettings";
import PanAndZoom from "./panAndZoom/panAndZoom";

// each of these exports shows up under this window's FrameExports object (see bloomFrames.ts)
// reviewslog: is this actually needed? Could these be be directly imported where they are used?
export { showOrHidePanel_click };
export { removeToolboxMarkup };
export { showSetupDialog, initializeReaderSetupDialog, closeSetupDialog } from "./readers/readerSetup/readerSetupDialog";
export { addWordListChangedListener, beginSaveChangedSettings, makeLetterWordList } from "./readers/readerTools";
export { loadLongpressInstructions } from "../js/bloomEditing";
export { TalkingBookModel }; // one function is called by CSharp; also, exporting something from it gets it included in the bundle.

// these are exported just to make sure they gets included in the bundle
// (and each adds an instance of itself to the collection in toolbox.ts)
export { LeveledReaderToolboxPanel };
export { DecodableReaderToolboxPanel };
export { PanAndZoom };

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
