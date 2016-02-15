import {ITabModel} from "../toolbox";
import {ToolBox} from "../toolbox";
import * as AudioRecorder from './audioRecording';

class TalkingBookModel implements ITabModel {
    restoreSettings(settings: string) {}

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
