/// <reference path="../../toolbox.ts" />
import { getTheOneReaderToolsModel, DRTState } from "../readerToolsModel";
import { beginInitializeLeveledReaderTool } from "../readerTools";
import { ITool } from "../../toolbox";
import { ToolBox } from "../../toolbox";
import { BloomApi } from "../../../../utils/bloomApi";

export class LeveledReaderToolboxTool implements ITool {
    makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            if (opts["leveledReaderState"]) {
                // The true passed here prevents re-saving the state we just read.
                // One non-obvious implication is that simply opening a level-4 book
                // will not switch the default level for new books to 4. That only
                // happens when you CHANGE the level in the toolbox.
                getTheOneReaderToolsModel().setLevelNumber(
                    parseInt(opts["leveledReaderState"], 10),
                    true
                );
            } else {
                BloomApi.get("api/readers/io/defaultLevel", result => {
                    // Presumably a brand new book. We'd better save the settings we come up with in it.
                    getTheOneReaderToolsModel().setLevelNumber(
                        parseInt(result.data, 10)
                    );
                });
            }
        });
    }

    configureElements(container: HTMLElement) {}
    isAlwaysEnabled(): boolean {
        return false;
    }
    isExperimental(): boolean {
        return false;
    }

    showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
    }

    hideTool() {
        // nothing to do here (if this class eventually extends our React Adaptor, this can be removed.)
    }

    detachFromPage() {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    newPageReady() {
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by detachFromPage() on the old page.
        getTheOneReaderToolsModel().setMarkupType(2);
        // usually updateMarkup will do this, unless we are coming from showTool
        getTheOneReaderToolsModel().doMarkup();
    }

    updateMarkup() {
        getTheOneReaderToolsModel().doMarkup();
    }

    id() {
        return "leveledReader";
    }

    hasRestoredSettings: boolean;

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    finishToolLocalization(paneDOM: HTMLElement) {
        // Unneeded in Leveled Reader, since Bloom.web.ExternalLinkController
        // 'translates' external links to include the current UI language.
    }
}
