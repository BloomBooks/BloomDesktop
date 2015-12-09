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
    return LeveledReaderModel;
})();
tabModels.push(new LeveledReaderModel());
//# sourceMappingURL=leveledReader.js.map