import {ITabModel} from "../toolbox";
import {ToolBox} from "../toolbox";

class TalkingBookModel implements ITabModel {
    restoreSettings(settings: string) {}

    configureElements(container: HTMLElement) {}

    showTool() {
        initializeTalkingBookTool();
        audioRecorder.setupForRecording();
    }

    hideTool() {
        audioRecorder.removeRecordingSetup();
    }

    updateMarkup() {
        audioRecorder.updateMarkupAndControlsToCurrentText();
    }

    name() { return 'talkingBookTool'; }
}

ToolBox.getTabModels().push(new TalkingBookModel());
