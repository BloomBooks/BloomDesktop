// To minimize code shared between iframes, this defines an interface of the functions
// that theOneAudioRecorder exposes to other iframes, without requiring them to import
// anything implementation-specific.

import { RecordingMode } from "./recordingMode"; // only holds the RecordingMode enum
import { TalkingBookUiState } from "./TalkingBookUiState";

export interface IAudioRecorder {
    autoSegmentBasedOnTextLength: () => number[];
    markAudioSplit: () => void;
    insertSegmentMarker(): void;
    setShowPlaybackOrder(isOn: boolean): Promise<void>;
    setShowingImageDescriptions: (boolean) => void;
    setRecordingMode(recordingMode: RecordingMode): Promise<void>;
    handleImportRecordingClick(): void;
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
    setLevelCanvas(canvas: HTMLCanvasElement | null): void;
    closeDeviceSelectMenu(): void;
    changeInputDevice(): void;
    setInputDevice(device: any): void;
    startRecordCurrentAsync(): Promise<void>;
    endRecordCurrentAsync(): Promise<void>;
    togglePlayCurrentAsync(): Promise<void>;
    playESpeakPreview(): void;
    showAdjustTimingsDialog(): Promise<void>;
    moveToNextAudioElement(): Promise<void>;
    moveToPrevAudioElementAsync(): Promise<void>;
    clearRecordingAsync(): Promise<void>;
    listenAsync(canvasToExclude?: HTMLElement): Promise<void>;
    getTalkingBookUiState(): TalkingBookUiState;
    registerStateListener(
        listener: (state: TalkingBookUiState) => void,
    ): () => void;
}
