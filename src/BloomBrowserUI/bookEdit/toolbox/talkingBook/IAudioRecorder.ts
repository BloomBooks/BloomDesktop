// To minimize code shared between iframes, this defines an interface of the functions
// that theOneAudioRecorder exposes to other iframes, without requiring them to import
// anything implementation-specific.

export interface IAudioRecorder {
    autoSegmentBasedOnTextLength: () => void;
}
