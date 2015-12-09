/// <reference path="../toolbox.ts" />

class LeveledReaderModel implements ITabModel {
    restoreSettings(opts: string) {
        if (opts['leveledReaderState']) {
            var state = libsynphony.dbGet('drt_state');
            if (!state) state = new DRTState();
            state.level = parseInt(opts['leveledReaderState']);
            libsynphony.dbSet('drt_state', state);
        }
    }

    configureElements(container: HTMLElement) {}
}

tabModels.push(new LeveledReaderModel());