export {};

declare global {
    interface ToolboxToolApi {
        makeRootElement?: () => HTMLDivElement;
    }

    interface CurrentToolApi {
        id: () => string;
    }

    interface ToolboxApi {
        getCurrentTool?: () => CurrentToolApi | undefined;
    }

    interface ToolboxBundleApi {
        getTheOneToolbox: () => ToolboxApi | undefined;
        scheduleMarkupUpdateAfterPaste: unknown;
        updateMarkupAfterUndoOrRedo: unknown;
        applyToolboxStateToPage: unknown;
        removeToolboxMarkup: unknown;
        showSetupDialog: unknown;
        initializeReaderSetupDialog: unknown;
        closeSetupDialog: unknown;
        addWordListChangedListener: unknown;
        beginSaveChangedSettings: unknown;
        makeLetterWordList: unknown;
        activateLongPressFor: unknown;
        TalkingBookTool: unknown;
        canUndo: unknown;
        undo: unknown;
        setActiveDragActivityTab: unknown;
        getTheOneAudioRecorderForExportOnly: unknown;
        copyLeveledReaderStatsToClipboard: unknown;
        simulateBlurOnPageFrameMouseDown: unknown;
    }

    interface Window {
        toolboxBundle?: ToolboxBundleApi;
    }
}
