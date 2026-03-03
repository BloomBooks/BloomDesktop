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
        applyToolboxStateToPage: unknown;
        removeToolboxMarkup: unknown;
        showOrHideTool_click: unknown;
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
        applyToolboxStateToPageLegacy: unknown;
        setActiveDragActivityTab: unknown;
        getTheOneAudioRecorderForExportOnly: unknown;
        copyLeveledReaderStatsToClipboard: unknown;
        handleClickOutsideToolbox: unknown;
    }

    interface Window {
        toolboxReactAdapter?: ToolboxReactAdapterApi;
        toolboxBundle?: ToolboxBundleApi;
    }
}
