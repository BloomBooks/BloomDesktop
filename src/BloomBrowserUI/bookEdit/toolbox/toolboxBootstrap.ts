/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from "jquery";
import { getTheOneToolbox, applyToolboxStateToUpdatedPage, showOrHideTool_click, removeToolboxMarkup } from "./toolbox";
import { getTheOneReaderToolsModel } from "./readers/readerToolsModel";
import { ToolBox } from "./toolbox";
import { BookSettings } from "./bookSettings/bookSettings";
import { DecodableReaderToolboxTool } from "./readers/decodableReader/decodableReaderToolboxTool";
import { LeveledReaderToolboxTool } from "./readers/leveledReader/leveledReaderToolboxTool";
import { MusicToolAdaptor } from "./music/musicToolControls";
import { PanAndZoomTool } from "./panAndZoom/panZoomToolControls";
import TalkingBookTool from "./talkingBook/talkingBook";
import { handleBookSettingCheckboxClick } from "./bookSettings/bookSettings";
import { VideoTool } from "./videoRecorder/videoTool";

// each of these exports shows up under this window's FrameExports object (see bloomFrames.ts)
// reviewslog: is this actually needed? Could these be be directly imported where they are used?
export { showOrHideTool_click };
export { removeToolboxMarkup };
export { showSetupDialog, initializeReaderSetupDialog, closeSetupDialog } from "./readers/readerSetup/readerSetupDialog";
export { addWordListChangedListener, beginSaveChangedSettings, makeLetterWordList } from "./readers/readerTools";
export { loadLongpressInstructions } from "../js/bloomEditing";
export { TalkingBookTool }; // one function is called by CSharp.


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

// Make the one instance of each Toolbox class and register it with the master toolbox.
// The imports we need to make these calls possible also serve to ensure that each
// toolbox's code is made part of the bundle.
ToolBox.registerTool(new BookSettings());
ToolBox.registerTool(new DecodableReaderToolboxTool());
ToolBox.registerTool(new LeveledReaderToolboxTool());
ToolBox.registerTool(new MusicToolAdaptor());
ToolBox.registerTool(new PanAndZoomTool());
ToolBox.registerTool(new TalkingBookTool());
ToolBox.registerTool(new VideoTool());
