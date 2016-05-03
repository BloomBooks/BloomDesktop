/// <reference path="../../toolbox.ts" />
import { ReaderToolsModel, DRTState, } from "../decodableReader/readerToolsModel";
import { beginInitializeLeveledReaderTool} from "../decodableReader/readerTools";
import {ITabModel} from "../../toolbox";
import {ToolBox} from "../../toolbox";
import {theOneLibSynphony}  from '../libSynphony/synphony_lib';

export default class LeveledReaderModelToolboxPanel implements ITabModel {
    beginRestoreSettings(opts: string): JQueryPromise<void> {
        if (!ReaderToolsModel.model) ReaderToolsModel.model = new ReaderToolsModel();
        return beginInitializeLeveledReaderTool().then(() => {
            if (opts['leveledReaderState']) {
                ReaderToolsModel.model.setLevelNumber(parseInt(opts['leveledReaderState']));
            }
        });
    }

    configureElements(container: HTMLElement) {}

    showTool() {
        // change markup based on visible options
        ReaderToolsModel.model.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!ReaderToolsModel.model.setMarkupType(2)) ReaderToolsModel.model.doMarkup();
    }

    hideTool() {
        ReaderToolsModel.model.setMarkupType(0);
    }

    updateMarkup() {
        ReaderToolsModel.model.doMarkup();
    }

    name() { return 'leveledReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new LeveledReaderModelToolboxPanel());