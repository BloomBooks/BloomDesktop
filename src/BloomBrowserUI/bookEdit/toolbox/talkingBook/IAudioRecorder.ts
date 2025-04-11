// To minimize code shared between iframes, this defines an interface of the functions
// that theOneAudioRecorder exposes to other iframes, without requiring them to import
// anything implementation-specific.

export interface IAudioRecorder {
    autoSegmentBasedOnTextLength: () => number[];
    markAudioSplit: () => void;
    setShowingImageDescriptions: (boolean) => void;
    hideTool: () => void;
    removeRecordingSetup: () => void;
    getUpdateMarkupAction: () => Promise<() => void>;
    setupForRecordingAsync: () => Promise<void>;
    newPageReady: (
        deshroudPhraseDelimiters?:
            | ((page: HTMLElement | null) => void)
            | undefined
    ) => Promise<void>;
}
