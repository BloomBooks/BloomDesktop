/// <reference path="../toolbox.ts" />

class LeveledReaderModel implements ITabModel {
    restoreSettings(opts: string): JQueryPromise<void> {
        if (!model) model = new ReaderToolsModel();
        return initializeLeveledReaderTool().then(() => {
            if (opts['leveledReaderState']) {
                var state = libsynphony.dbGet('drt_state');
                if (!state) state = new DRTState();
                state.level = parseInt(opts['leveledReaderState']);
                libsynphony.dbSet('drt_state', state);
            }
        });
    }

    configureElements(container: HTMLElement) {}

    showTool() {
        // change markup based on visible options
        model.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!model.setMarkupType(2)) model.doMarkup();
    }

    hideTool() {
        model.setMarkupType(0);
    }

    updateMarkup() {
        model.doMarkup();
    }

    name() {return 'leveledReader';}

    hasRestoredSettings: boolean;
}

tabModels.push(new LeveledReaderModel());