import {ITabModel} from "../toolbox";
import {ToolBox} from "../toolbox";
import * as AudioRecorder from './audioRecording';
import {theOneAudioRecorder} from './audioRecording';

export default class TalkingBookModel implements ITabModel {
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    configureElements(container: HTMLElement) {}

    showTool() {
        AudioRecorder.initializeTalkingBookTool();
        AudioRecorder.theOneAudioRecorder.setupForRecording();
    }

    hideTool() {
        AudioRecorder.theOneAudioRecorder.removeRecordingSetup();
    }

    updateMarkup() {
        AudioRecorder.theOneAudioRecorder.updateMarkupAndControlsToCurrentText();
    }

    name() { return 'talkingBook'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new TalkingBookModel());
