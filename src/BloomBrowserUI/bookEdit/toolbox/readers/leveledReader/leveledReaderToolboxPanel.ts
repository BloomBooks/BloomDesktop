/// <reference path="../../toolbox.ts" />
import { getTheOneReaderToolsModel, DRTState, } from "../readerToolsModel";
import { beginInitializeLeveledReaderTool } from "../readerTools";
import { ITabModel } from "../../toolbox";
import { ToolBox } from "../../toolbox";

export default class LeveledReaderToolboxPanel implements ITabModel {
    beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            if (opts['leveledReaderState']) {
                getTheOneReaderToolsModel().setLevelNumber(parseInt(opts['leveledReaderState']));
            }
        });
    }

    configureElements(container: HTMLElement) { }

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

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    finishTabPaneLocalization(paneDOM: HTMLElement) {
        // Unneeded in Leveled Reader, since Bloom.web.ExternalLinkController
        // 'translates' external links to include the current UI language.
    }
}

ToolBox.getTabModels().push(new LeveledReaderToolboxPanel());
