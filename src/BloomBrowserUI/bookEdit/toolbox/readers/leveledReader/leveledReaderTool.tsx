import { get } from "../../../../utils/bloomApi";
import { renderRoot } from "../../../../utils/reactRender";
import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { beginInitializeLeveledReaderTool } from "../readerTools";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { LeveledReaderToolControls } from "./LeveledReaderToolControls";

export class LeveledReaderTool extends ToolboxToolReactAdaptor {
    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");

        renderRoot(<LeveledReaderToolControls />, root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return "leveledReader";
    }

    public beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            const restoreDone = $.Deferred<void>();
            const leveledReaderState = (
                opts as unknown as Record<string, string>
            )["leveledReaderState"];
            if (leveledReaderState) {
                // The true passed here prevents re-saving the state we just read.
                // One non-obvious implication is that simply opening a level-4 book
                // will not switch the default level for new books to 4. That only
                // happens when you CHANGE the level in the toolbox.
                getTheOneReaderToolsModel().setLevelNumber(
                    parseInt(leveledReaderState, 10),
                    true,
                );
                restoreDone.resolve();
            } else {
                get(
                    "readers/io/defaultLevel",
                    (result) => {
                        // Presumably a brand new book. We'd better save the settings we come up with in it.
                        getTheOneReaderToolsModel().setLevelNumber(
                            parseInt(result.data, 10),
                        );
                        restoreDone.resolve();
                    },
                    () => restoreDone.resolve(),
                );
            }

            return restoreDone.promise();
        });
    }

    public newPageReady(): void {
        const model = getTheOneReaderToolsModel();
        model.clearWholeBookCache();
        model.setMarkupType(isReaderToolEnabledOnCurrentPage(true) ? 2 : 0);
        model.updateControlContents();
        // usually updateMarkup will do this, unless we are coming from showTool
        model.doMarkup();
    }

    public detachFromPage(): void {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    public updateMarkup() {
        // Don't let this lower-level code create ckeditor bookmarks in this case.
        // We've already created them in toolbox.ts which calls this.
        const createCkEditorBookMarks = false;
        getTheOneReaderToolsModel().updateControlContents();
        getTheOneReaderToolsModel().doMarkup(createCkEditorBookMarks);
    }

    public showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        // Toggle render is handled in newPageReady(), where page reader classes are settled.
    }
}
