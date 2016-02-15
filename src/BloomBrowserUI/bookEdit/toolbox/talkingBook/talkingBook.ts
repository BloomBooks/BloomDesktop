import {ITabModel} from "../toolbox";
import {ToolBox} from "../toolbox";
import * as AudioRecorder from './audioRecording';
import {theOneAudioRecorder} from './audioRecording';

export default class TalkingBookModel implements ITabModel {
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

    // Called by CSharp, and I'd rather not have C# aware of the AudioRecorder object.
    static getTheOneAudioRecorder() {
        return theOneAudioRecorder;
    }
}

ToolBox.getTabModels().push(new TalkingBookModel());
