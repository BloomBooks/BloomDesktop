if (typeof ($) === "function") {

    // Running for real, and jquery properly loaded first
    $(document).ready(function () {
        initializeTalkingBookTool();
    });
}

class TalkingBookModel implements ITabModel {
    restoreSettings(settings: string) {}

    configureElements(container: HTMLElement) {}

    showTool() {
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

tabModels.push(new TalkingBookModel());
