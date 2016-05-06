/// <reference path="../../toolbox.ts" />
import { theOneReaderToolsModel, DRTState, } from "../readerToolsModel";
import { beginInitializeLeveledReaderTool} from "../readerTools";
import {ITabModel} from "../../toolbox";
import {ToolBox} from "../../toolbox";
import {theOneLibSynphony}  from '../libSynphony/synphony_lib';

export default class LeveledReaderToolboxPanel implements ITabModel {
    beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            if (opts['leveledReaderState']) {
                theOneReaderToolsModel.setLevelNumber(parseInt(opts['leveledReaderState']));
            }
        });
    }

    configureElements(container: HTMLElement) {}

    showTool() {
        // change markup based on visible options
        theOneReaderToolsModel.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!theOneReaderToolsModel.setMarkupType(2)) theOneReaderToolsModel.doMarkup();
    }

    hideTool() {
        theOneReaderToolsModel.setMarkupType(0);
    }

    updateMarkup() {
        theOneReaderToolsModel.doMarkup();
    }

    name() { return 'leveledReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new LeveledReaderToolboxPanel());