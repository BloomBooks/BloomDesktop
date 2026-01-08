// To minimize code shared between iframes, this defines an interface of the functions
// that theOneAudioRecorder exposes to other iframes, without requiring them to import
// anything implementation-specific.

import { RecordingMode } from "./recordingMode"; // only holds the RecordingMode enum

export interface IAudioRecorder {
    autoSegmentBasedOnTextLength: () => number[];
    markAudioSplit: () => void;
    setShowingImageDescriptions: (boolean) => void;
    removeRecordingSetup: () => void;
    getUpdateMarkupAction: () => Promise<() => void>;
    setupForRecordingAsync: () => Promise<void>;
    handleToolHiding: () => void;
    handleNewPageReady: (
        deshroudPhraseDelimiters?:
            | ((page: HTMLElement | null) => void)
            | undefined,
    ) => Promise<void>;
    bumpUp: (number) => void;
    bumpDown: (number) => void;
    getPageDocBody: () => HTMLElement | null;
    getCurrentTextBox: () => HTMLElement | null;
    recordingMode: RecordingMode;
}
