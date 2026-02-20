/// <reference path="../../typings/jquery/jquery.d.ts" />
import $ from "jquery";
import {
    getTheOneToolbox,
    applyToolboxStateToUpdatedPage,
    removeToolboxMarkup,
    showOrHideTool_click,
    handleClickOutsideToolbox,
    scheduleMarkupUpdateAfterPaste,
} from "./toolbox";
import { getTheOneReaderToolsModel } from "./readers/readerToolsModel";
import { ToolBox } from "./toolbox";
import { DecodableReaderToolboxTool } from "./readers/decodableReader/decodableReaderToolboxTool";
import { LeveledReaderToolboxTool } from "./readers/leveledReader/leveledReaderToolboxTool";
import { MusicToolAdaptor } from "./music/musicToolControls";
import { ImpairmentVisualizerAdaptor } from "./impairmentVisualizer/impairmentVisualizer";
import { MotionTool } from "./motion/motionTool";
import TalkingBookTool from "./talkingBook/talkingBook";
import { SignLanguageTool } from "./signLanguage/signLanguageTool";
import { ImageDescriptionAdapter } from "./imageDescription/imageDescription";
import "errorHandler";
import { CanvasTool } from "./canvas/canvasTool";
import { GameTool, setActiveDragActivityTab } from "./games/GameTool";
// Explicit imports needed so that these symbols are in local scope for the window.toolboxBundle object
import {
    addWordListChangedListener,
    beginSaveChangedSettings,
    makeLetterWordList,
} from "./readers/readerTools";
import { activateLongPressFor } from "../js/bloomEditing";
import { IAudioRecorder } from "./talkingBook/IAudioRecorder";
import { theOneAudioRecorder } from "./talkingBook/audioRecording";
import { renderToolboxRoot } from "./ToolboxRoot";

export interface IToolboxFrameExports {
    addWordListChangedListener(
        listenerNameAndContext: string,
        callback: () => void,
    ): void;

    activateLongPressFor(jQuerySetOfMatchedElements): void;

    getTheOneToolbox(): ToolBox;

    scheduleMarkupUpdateAfterPaste(): void;

    canUndo(): boolean;
    undo(): void;

    applyToolboxStateToPage(): void;

    removeToolboxMarkup(): void;
    setActiveDragActivityTab(tab: number): void;
    getTheOneAudioRecorderForExportOnly(): IAudioRecorder;
    handleClickOutsideToolbox(): void;
}

// each of these exports shows up under this window's toolboxBundle object (see bloomFrames.ts)
export { removeToolboxMarkup, showOrHideTool_click, setActiveDragActivityTab };
export {
    showSetupDialog,
    initializeReaderSetupDialog,
    closeSetupDialog,
} from "./readers/readerSetup/readerSetupDialog";
export {
    addWordListChangedListener,
    beginSaveChangedSettings,
    makeLetterWordList,
} from "./readers/readerTools";
export { activateLongPressFor } from "../js/bloomEditing";
export { TalkingBookTool }; // one function is called by CSharp.

export { getTheOneToolbox };
export { scheduleMarkupUpdateAfterPaste };

// Import the functions we're re-exporting so we can use them in the bundle
import {
    showSetupDialog,
    initializeReaderSetupDialog,
    closeSetupDialog,
} from "./readers/readerSetup/readerSetupDialog";

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

// Don't use this directly, use getAudioRecorder() in audioRecording.ts instead.
export function getTheOneAudioRecorderForExportOnly(): IAudioRecorder {
    return theOneAudioRecorder;
}

export function copyLeveledReaderStatsToClipboard() {
    const readerToolsModel = getTheOneReaderToolsModel();
    if (readerToolsModel) {
        readerToolsModel.copyLeveledReaderStatsToClipboard();
    }
}

$(document).ready(() => {
    renderToolboxRoot();
    getTheOneToolbox().initialize();
});

// Make the one instance of each Toolbox class and register it with the master toolbox.
// The imports we need to make these calls possible also serve to ensure that each
// toolbox's code is made part of the bundle.
ToolBox.registerTool(new DecodableReaderToolboxTool());
ToolBox.registerTool(new LeveledReaderToolboxTool());
ToolBox.registerTool(new MusicToolAdaptor());
ToolBox.registerTool(new ImpairmentVisualizerAdaptor());
ToolBox.registerTool(new MotionTool());
ToolBox.registerTool(new TalkingBookTool());
ToolBox.registerTool(new SignLanguageTool());
ToolBox.registerTool(new ImageDescriptionAdapter());
ToolBox.registerTool(new CanvasTool());
ToolBox.registerTool(new GameTool());

// Legacy global exposure: mimic old webpack window["toolboxBundle"] contract
interface ToolboxBundleApi {
    getTheOneToolbox: typeof getTheOneToolbox;
    scheduleMarkupUpdateAfterPaste: typeof scheduleMarkupUpdateAfterPaste;
    applyToolboxStateToPage: typeof applyToolboxStateToPage;
    removeToolboxMarkup: typeof removeToolboxMarkup;
    showOrHideTool_click: typeof showOrHideTool_click;
    showSetupDialog: typeof import("./readers/readerSetup/readerSetupDialog").showSetupDialog;
    initializeReaderSetupDialog: typeof import("./readers/readerSetup/readerSetupDialog").initializeReaderSetupDialog;
    closeSetupDialog: typeof import("./readers/readerSetup/readerSetupDialog").closeSetupDialog;
    addWordListChangedListener: typeof addWordListChangedListener;
    beginSaveChangedSettings: typeof beginSaveChangedSettings;
    makeLetterWordList: typeof makeLetterWordList;
    activateLongPressFor: typeof activateLongPressFor;
    TalkingBookTool: typeof TalkingBookTool;
    canUndo: typeof canUndo;
    undo: typeof undo;
    applyToolboxStateToPageLegacy: typeof applyToolboxStateToPage; // alias if older code referenced different name
    setActiveDragActivityTab: typeof setActiveDragActivityTab;
    getTheOneAudioRecorderForExportOnly: typeof getTheOneAudioRecorderForExportOnly;
    copyLeveledReaderStatsToClipboard: typeof copyLeveledReaderStatsToClipboard;
    handleClickOutsideToolbox: typeof import("./toolbox").handleClickOutsideToolbox;
}

declare global {
    interface Window {
        toolboxBundle: ToolboxBundleApi;
    }
}

window.toolboxBundle = {
    getTheOneToolbox,
    scheduleMarkupUpdateAfterPaste,
    applyToolboxStateToPage,
    removeToolboxMarkup,
    showOrHideTool_click,
    showSetupDialog,
    initializeReaderSetupDialog,
    closeSetupDialog,
    addWordListChangedListener,
    beginSaveChangedSettings,
    makeLetterWordList,
    activateLongPressFor,
    TalkingBookTool,
    canUndo,
    undo,
    applyToolboxStateToPageLegacy: applyToolboxStateToPage,
    setActiveDragActivityTab,
    getTheOneAudioRecorderForExportOnly,
    copyLeveledReaderStatsToClipboard,
    handleClickOutsideToolbox,
};
