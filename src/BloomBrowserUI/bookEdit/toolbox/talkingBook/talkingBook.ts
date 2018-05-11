import { ITool } from "../toolbox";
import { ToolBox } from "../toolbox";
import * as AudioRecorder from './audioRecording';
import { theOneAudioRecorder } from './audioRecording';

export default class TalkingBookTool implements ITool {
    makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    isAlwaysEnabled(): boolean {
        return true;
    }

    configureElements(container: HTMLElement) { }

    showTool() {
        AudioRecorder.initializeTalkingBookTool();
        AudioRecorder.theOneAudioRecorder.setupForRecording();
    }

    hideTool() {
        // not quite sure how this can be called when never initialized, but if
        // we don't have the object we certainly can't use it.
        if (AudioRecorder.theOneAudioRecorder) {
            AudioRecorder.theOneAudioRecorder.removeRecordingSetup();
        }
    }

    // Called whenever the user edits text.
    updateMarkup() {
        AudioRecorder.theOneAudioRecorder.updateMarkupAndControlsToCurrentText();
    }

    // Called when a new page is loaded.
    newPageReady() {
        AudioRecorder.theOneAudioRecorder.addAudioLevelListener(); // keeps the peak audio level monitor functioning.
    }

    id() { return "talkingBook"; }

    hasRestoredSettings: boolean;

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    finishToolLocalization(paneDOM: HTMLElement) {
        // So far unneeded in talkingBook
    }
}
