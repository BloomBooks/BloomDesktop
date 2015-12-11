if (typeof ($) === "function") {

    // Running for real, and jquery properly loaded first
    $(document).ready(function () {
        initializeTalkingBookTool();
    });
}

class TalkingBookModel implements ITabModel {
    restoreSettings(settings: string) {}

    configureElements(container: HTMLElement) {}

    showTool(ui:any) {
        audioRecorder.setupForRecording();
    }

    hideTool(ui:any) {
        audioRecorder.removeRecordingSetup();
    }

    name() { return 'talkingBookTool'; }
}

tabModels.push(new TalkingBookModel());
