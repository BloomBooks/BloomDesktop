/// <reference path="../../typings/jquery/jquery.d.ts" />
import * as $ from "jquery";
import {
    getTheOneToolbox,
    applyToolboxStateToUpdatedPage,
    removeToolboxMarkup,
    showOrHideTool_click
} from "./toolbox";
import { getTheOneReaderToolsModel } from "./readers/readerToolsModel";
import { ToolBox } from "./toolbox";
import { BookSettings } from "./bookSettings/bookSettings";
import { DecodableReaderToolboxTool } from "./readers/decodableReader/decodableReaderToolboxTool";
import { LeveledReaderToolboxTool } from "./readers/leveledReader/leveledReaderToolboxTool";
import { MusicToolAdaptor } from "./music/musicToolControls";
import { ImpairmentVisualizerAdaptor } from "./impairmentVisualizer/impairmentVisualizer";
import { MotionTool } from "./motion/motionTool";
import TalkingBookTool from "./talkingBook/talkingBook";
import { handleBookSettingCheckboxClick } from "./bookSettings/bookSettings";
import { SignLanguageTool } from "./signLanguage/signLanguageTool";
import { ImageDescriptionAdapter } from "./imageDescription/imageDescription";
import "errorHandler";
import { ComicTool } from "./comic/comicTool";

// each of these exports shows up under this window's FrameExports object (see bloomFrames.ts)
export { removeToolboxMarkup, showOrHideTool_click };
export {
    showSetupDialog,
    initializeReaderSetupDialog,
    closeSetupDialog
} from "./readers/readerSetup/readerSetupDialog";
export {
    addWordListChangedListener,
    beginSaveChangedSettings,
    makeLetterWordList
} from "./readers/readerTools";
export { loadLongpressInstructions } from "../js/bloomEditing";
export { TalkingBookTool }; // one function is called by CSharp.

// called by click handler in jade; also, exporting something from it gets it included in the bundle.
export { handleBookSettingCheckboxClick };
export { getTheOneToolbox };

export function canUndo(): boolean {
    const readerToolsModel = getTheOneReaderToolsModel();

    return (
        readerToolsModel &&
        readerToolsModel.shouldHandleUndo() &&
        readerToolsModel.canUndo()
    );
}

export function undo() {
    const readerToolsModel = getTheOneReaderToolsModel();
    if (readerToolsModel) {
        readerToolsModel.undo();
    }
}

export function applyToolboxStateToPage() {
    applyToolboxStateToUpdatedPage();
}

$(document).ready(() => {
    getTheOneToolbox().initialize();
});

// Make the one instance of each Toolbox class and register it with the master toolbox.
// The imports we need to make these calls possible also serve to ensure that each
// toolbox's code is made part of the bundle.
ToolBox.registerTool(new BookSettings());
ToolBox.registerTool(new DecodableReaderToolboxTool());
ToolBox.registerTool(new LeveledReaderToolboxTool());
ToolBox.registerTool(new MusicToolAdaptor());
ToolBox.registerTool(new ImpairmentVisualizerAdaptor());
ToolBox.registerTool(new MotionTool());
ToolBox.registerTool(new TalkingBookTool());
ToolBox.registerTool(new SignLanguageTool());
ToolBox.registerTool(new ImageDescriptionAdapter());
ToolBox.registerTool(new ComicTool());
