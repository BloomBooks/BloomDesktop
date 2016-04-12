/// <reference path="../toolbox.ts" />

class LeveledReaderModel implements ITabModel {
    beginRestoreSettings(opts: string): JQueryPromise<void> {
        if (!model) model = new ReaderToolsModel();
        return beginInitializeLeveledReaderTool().then(() => {
            if (opts['leveledReaderState']) {
                model.setLevelNumber(parseInt(opts['leveledReaderState']));
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