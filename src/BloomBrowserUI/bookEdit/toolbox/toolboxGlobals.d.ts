export {};

declare global {
    // The set of functions the toolbox iframe publishes as window.toolboxBundle, for
    // other frames (and C#) to call. Consumers get the real types by casting to
    // IToolboxFrameExports (see workspaceFrames.ts), so these are just names.
    interface ToolboxBundleApi {
        getTheOneToolbox: unknown;
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
