var TalkingBookModel = (function () {
    function TalkingBookModel() {
    }
    TalkingBookModel.prototype.restoreSettings = function (settings) { };
    TalkingBookModel.prototype.configureElements = function (container) { };
    TalkingBookModel.prototype.showTool = function () {
        initializeTalkingBookTool();
        audioRecorder.setupForRecording();
    };
    TalkingBookModel.prototype.hideTool = function () {
        audioRecorder.removeRecordingSetup();
    };
    TalkingBookModel.prototype.updateMarkup = function () {
        audioRecorder.updateMarkupAndControlsToCurrentText();
    };
    TalkingBookModel.prototype.name = function () { return 'talkingBookTool'; };
    return TalkingBookModel;
})();
tabModels.push(new TalkingBookModel());
//# sourceMappingURL=talkingBook.js.map