/// <reference path="../../toolbox.ts" />
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import {
    beginInitializeLeveledReaderTool,
    createToggle,
    isToggleOff
} from "../readerTools";
import { ITool } from "../../toolbox";
import { get } from "../../../../utils/bloomApi";

export class LeveledReaderToolboxTool implements ITool {
    public makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    public requiresToolId(): boolean {
        return false;
    }
    public beginRestoreSettings(opts: string): JQueryPromise<void> {
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
                get("readers/io/defaultLevel", result => {
                    // Presumably a brand new book. We'd better save the settings we come up with in it.
                    getTheOneReaderToolsModel().setLevelNumber(
                        parseInt(result.data, 10)
                    );
                });
            }
        });
    }

    public configureElements(container: HTMLElement) {
        // Leveled reader makes use of the setup in this.setupReaderKeyAndFocusHandlers(container).
        // This would be the place to call it, but it is called by decodableReaderToolboxTool.ts' configureElements().
        // And configureElements gets called for every tool, whether or not that tool is open,
        // so it always get initialized by decodableReaderToolboxTool.ts, and we don't want to call it twice.
    }
    public isAlwaysEnabled(): boolean {
        return false;
    }
    public isExperimental(): boolean {
        return false;
    }

    public showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.

        const isForLeveled = true;
        createToggle(isForLeveled);
    }

    public hideTool() {
        // nothing to do here (if this class eventually extends our React Adaptor, this can be removed.)
    }

    public detachFromPage() {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    public newPageReady() {
        // Often we could get away without reloading this, but we might
        // have just deleted a page, or duplicated one, or pasted one...
        getTheOneReaderToolsModel().clearWholeBookCache();
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by detachFromPage() on the old page.
        // So we do want to set the appropriate markup, but if the toggle is off, we want the markup off.
        const isForLeveled = true;
        getTheOneReaderToolsModel().setMarkupType(
            isToggleOff(isForLeveled) ? 0 : 2
        );
        // usually updateMarkup will do this, unless we are coming from showTool
        getTheOneReaderToolsModel().doMarkup();
    }

    public updateMarkup() {
        // Don't let this lower-level code create ckeditor bookmarks in this case.
        // We've already created them in toolbox.ts which calls this.
        const createCkEditorBookMarks = false;
        getTheOneReaderToolsModel().doMarkup(createCkEditorBookMarks);
    }
    public async updateMarkupAsync() {
        // If you implement this, you may need to do something like cleanUpCkEditorHtml() in audioRecording.ts.
        throw "not implemented...use updateMarkup";
        return () => undefined;
    }

    public isUpdateMarkupAsync(): boolean {
        return false;
    }

    public id() {
        return "leveledReader";
    }

    public hasRestoredSettings: boolean;

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    public finishToolLocalization(paneDOM: HTMLElement) {
        // Unneeded in Leveled Reader, since Bloom.web.ExternalLinkController
        // 'translates' external links to include the current UI language.
    }
}
