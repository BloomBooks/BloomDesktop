export {};

declare global {
    interface ToolboxReactAdapterApi {
        isEnabled: () => boolean;
        setActiveToolByToolId: (toolId: string) => void;
        getActiveToolId: () => string | undefined;
        onActiveToolChanged: (callback: (toolId: string) => void) => void;
    }

    interface ToolboxToolApi {
        makeRootElement?: () => HTMLDivElement;
    }

    interface CurrentToolApi {
        id: () => string;
    }

    interface ToolboxApi {
        getToolIfOffered?: (toolId: string) => ToolboxToolApi | undefined;
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
        getDecodableStageMatchingWords: unknown;
        getSynphonyAlwaysMatchSymbols: unknown;
        classifySampleTextFiles: unknown;
        addSampleTextFilesChangedListener: unknown;
        addWordListChangedListener: unknown;
        beginSaveChangedSettings: unknown;
        makeLetterWordList: unknown;
        removeSampleTextFilesChangedListener: unknown;
        removeWordListChangedListener: unknown;
        activateLongPressFor: unknown;
        TalkingBookTool: unknown;
        canUndo: unknown;
        undo: unknown;
        applyToolboxStateToPageLegacy: unknown;
        setActiveDragActivityTab: unknown;
        getTheOneAudioRecorderForExportOnly: unknown;
        copyLeveledReaderStatsToClipboard: unknown;
        simulateBlurOnPageFrameMouseDown: unknown;
    }

    interface Window {
        toolboxReactAdapter?: ToolboxReactAdapterApi;
        toolboxBundle?: ToolboxBundleApi;
    }
}
