import { RecordingMode } from "./recordingMode";

export enum Status {
    Disabled, // Can't use button now (e.g., Play when there is no recording)
    Enabled, // Can use now, not the most likely thing to do next
    Expected, // The most likely/appropriate button to use next (e.g., Play right after recording)
    Active, // Button now active (Play while playing; Record while held down)
}

// ui state used by the React talking book tool. The engine (audioRecording.ts)
// manipulates this state and then calls a set state function provided by the React
// tool, so that the tool rerenders and updates its copy of the state to match what's
// been changed.
export interface TalkingBookUiState {
    buttons: Record<
        "record" | "play" | "split" | "next" | "prev" | "clear" | "listen",
        Status
    >;
    recordingMode: RecordingMode;
    hasAudio: boolean;
    hasRecordableDivs: boolean;
    haveACurrentTextboxModeRecording: boolean;
    inShowPlaybackOrderMode: boolean;
    showingImageDescriptions: boolean;
    inputDevice?: { iconSrc: string; title: string };
    shouldShowDeviceMenu: boolean;
    audioDevices: string[];
}
