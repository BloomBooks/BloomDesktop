if (typeof ($) === "function") {
    // Running for real, and jquery properly loaded first
    $(document).ready(function () {
        initializeTalkingBookTool();
    });
}
var TalkingBookModel = (function () {
    function TalkingBookModel() {
    }
    TalkingBookModel.prototype.restoreSettings = function (settings) { };
    TalkingBookModel.prototype.configureElements = function (container) { };
    TalkingBookModel.prototype.showTool = function (ui) {
        audioRecorder.setupForRecording();
    };
    TalkingBookModel.prototype.hideTool = function (ui) {
        audioRecorder.removeRecordingSetup();
    };
    TalkingBookModel.prototype.name = function () { return 'talkingBookTool'; };
    return TalkingBookModel;
})();
tabModels.push(new TalkingBookModel());
//# sourceMappingURL=talkingBook.js.map