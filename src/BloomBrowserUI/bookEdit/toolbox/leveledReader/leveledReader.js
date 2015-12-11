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
    LeveledReaderModel.prototype.showTool = function (ui) {
        // change markup based on visible options
        model.setMarkupType(ui.newHeader.data('markuptype'));
    };
    LeveledReaderModel.prototype.hideTool = function (ui) {
        model.setMarkupType(ui.newHeader.data('markuptype'));
    };
    LeveledReaderModel.prototype.name = function () { return 'leveledReaderTool'; };
    return LeveledReaderModel;
})();
tabModels.push(new LeveledReaderModel());
//# sourceMappingURL=leveledReader.js.map