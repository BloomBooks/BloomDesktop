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

    showTool(ui: any) {
        // change markup based on visible options
        model.setMarkupType(ui.newHeader.data('markuptype'));
    }

    hideTool(ui: any) {
        model.setMarkupType(ui.newHeader.data('markuptype'));
    }

    name() {return 'leveledReaderTool';}
}

tabModels.push(new LeveledReaderModel());