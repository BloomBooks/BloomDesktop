/// <reference path="../toolbox.ts" />
var LeveledReaderModel = (function () {
    function LeveledReaderModel() {
    }
    LeveledReaderModel.prototype.restoreSettings = function (opts) {
        if (opts['leveledReaderState']) {
            var state = libsynphony.dbGet('drt_state');
            if (!state)
                state = new DRTState();
            state.level = parseInt(opts['leveledReaderState']);
            libsynphony.dbSet('drt_state', state);
        }
    };
    LeveledReaderModel.prototype.configureElements = function (container) { };
    LeveledReaderModel.prototype.showTool = function () {
        // change markup based on visible options
        model.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!model.setMarkupType(2))
            model.doMarkup();
    };
    LeveledReaderModel.prototype.hideTool = function () {
        model.setMarkupType(0);
    };
    LeveledReaderModel.prototype.name = function () { return 'leveledReaderTool'; };
    return LeveledReaderModel;
})();
tabModels.push(new LeveledReaderModel());
//# sourceMappingURL=leveledReader.js.map