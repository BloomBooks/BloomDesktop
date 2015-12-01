/// <reference path="../toolbox.ts" />
var DecodableReaderModel = (function () {
    function DecodableReaderModel() {
    }
    DecodableReaderModel.prototype.restoreSettings = function (opts) {
        if (opts['decodableReaderState']) {
            var state = libsynphony.dbGet('drt_state');
            if (!state)
                state = new DRTState();
            var decState = opts['decodableReaderState'];
            if (decState.startsWith("stage:")) {
                var parts = decState.split(";");
                state.stage = parseInt(parts[0].substring("stage:".length));
                var sort = parts[1].substring("sort:".length);
                model.setSort(sort);
            }
            else {
                // old state
                state.stage = parseInt(decState);
            }
            libsynphony.dbSet('drt_state', state);
        }
    };
    return DecodableReaderModel;
})();
tabModels.push(new DecodableReaderModel());
//# sourceMappingURL=decodableReader.js.map