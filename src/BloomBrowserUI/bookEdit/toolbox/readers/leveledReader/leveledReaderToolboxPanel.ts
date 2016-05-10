/// <reference path="../../toolbox.ts" />
import { getTheOneReaderToolsModel, DRTState, } from "../readerToolsModel";
import { beginInitializeLeveledReaderTool} from "../readerTools";
import {ITabModel} from "../../toolbox";
import {ToolBox} from "../../toolbox";
import {theOneLibSynphony}  from '../libSynphony/synphony_lib';

export default class LeveledReaderToolboxPanel implements ITabModel {
    beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            if (opts['leveledReaderState']) {
                getTheOneReaderToolsModel().setLevelNumber(parseInt(opts['leveledReaderState']));
            }
        });
    }

    configureElements(container: HTMLElement) {}

    showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        if (!getTheOneReaderToolsModel().setMarkupType(2)) getTheOneReaderToolsModel().doMarkup();
    }

    hideTool() {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    updateMarkup() {
        getTheOneReaderToolsModel().doMarkup();
    }

    name() { return 'leveledReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new LeveledReaderToolboxPanel());