/// <reference path="../../typings/jquery/jquery.d.ts" />
import $ from "jquery";
import {
    getTheOneToolbox,
    applyToolboxStateToUpdatedPage,
    removeToolboxMarkup,
    removeToolMarkupFromPageClone,
    scheduleMarkupUpdateAfterPaste,
    updateMarkupAfterUndoOrRedo,
} from "./toolbox";
import { simulateBlurOnPageFrameMouseDown } from "../../utils/menuCloseOnBlur";
import { getTheOneReaderToolsModel } from "./readers/readerToolsModel";
import { ToolBox } from "./toolbox";
import TalkingBookTool from "./talkingBook/talkingBookTool";
import "errorHandler";
import { setActiveDragActivityTab } from "./games/GameTool";
import { registerAllToolboxTools } from "./registerAllToolboxTools";
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
    updateMarkupAfterUndoOrRedo(): void;

    canUndo(): boolean;
    undo(): void;

    applyToolboxStateToPage(): void;

    removeToolboxMarkup(): void;
    removeToolMarkupFromPageClone(pageClone: HTMLElement): void;
    setActiveDragActivityTab(tab: number): void;
    getTheOneAudioRecorderForExportOnly(): IAudioRecorder;
    simulateBlurOnPageFrameMouseDown(): void;
}

// each of these exports shows up under this window's toolboxBundle object (see workspaceFrames.ts)
export {
    removeToolboxMarkup,
    removeToolMarkupFromPageClone,
    setActiveDragActivityTab,
};
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
export { scheduleMarkupUpdateAfterPaste, updateMarkupAfterUndoOrRedo };

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

// Make the one instance of each Toolbox class and register it with the master toolbox. The list
// lives in registerAllToolboxTools.ts, which the test harness imports as well, so there is only
// one list to keep right.
registerAllToolboxTools();

const toolboxBundle: ToolboxBundleApi = {
    getTheOneToolbox,
    scheduleMarkupUpdateAfterPaste,
    updateMarkupAfterUndoOrRedo,
    applyToolboxStateToPage,
    removeToolboxMarkup,
    removeToolMarkupFromPageClone,
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
    simulateBlurOnPageFrameMouseDown,
};

window.toolboxBundle = toolboxBundle;
