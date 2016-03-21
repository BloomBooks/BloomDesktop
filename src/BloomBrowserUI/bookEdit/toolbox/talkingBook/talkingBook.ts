class TalkingBookModel implements ITabModel {
    restoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }

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

    name() { return 'talkingBook'; }

    hasRestoredSettings: boolean;
}

tabModels.push(new TalkingBookModel());
