/// <reference path="../../toolbox.ts" />
import { ReaderToolsModel, DRTState, } from "../readerToolsModel";
import { beginInitializeLeveledReaderTool} from "../readerTools";
import {ITabModel} from "../../toolbox";
import {ToolBox} from "../../toolbox";
import {theOneLibSynphony}  from '../libSynphony/synphony_lib';

export default class LeveledReaderToolboxPanel implements ITabModel {
    beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            if (opts['leveledReaderState']) {
                ReaderToolsModel.setLevelNumber(parseInt(opts['leveledReaderState']));
            }
        });
    }

    configureElements(container: HTMLElement) {}

    showTool() {
        // change markup based on visible options
        ReaderToolsModel.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!ReaderToolsModel.setMarkupType(2)) ReaderToolsModel.doMarkup();
    }

    hideTool() {
        ReaderToolsModel.setMarkupType(0);
    }

    updateMarkup() {
        ReaderToolsModel.doMarkup();
    }

    name() { return 'leveledReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new LeveledReaderToolboxPanel());