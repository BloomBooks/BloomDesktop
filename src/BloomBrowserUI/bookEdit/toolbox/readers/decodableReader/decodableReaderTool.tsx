import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import { DecodableReaderToolControls } from "./DecodableReaderToolControls";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { get } from "../../../../utils/bloomApi";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { renderRoot } from "../../../../utils/reactRender";

const model = getTheOneReaderToolsModel();

// this new version of the decodable reader tool re-implements some
// of the methods that were implemented in decodableReaderToolboxTool.ts,
// so that everything would load properly and the markup would work as
// usual.
export class DecodableReaderTool extends ToolboxToolReactAdaptor {
    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");

        renderRoot(<DecodableReaderToolControls />, root);
        return root as HTMLDivElement;
    }
    public id(): string {
        return "decodableReader";
    }
    public newPageReady(): void {
        model.setMarkupType(isReaderToolEnabledOnCurrentPage(false) ? 1 : 0);
        model.updateControlContents();
        // usually updateMarkup will do this, unless we are coming from showTool
        model.doMarkup();
    }
    public detachFromPage(): void {
        model.setMarkupType(0);
    }
    public updateMarkup() {
        // Don't let this lower-level code create ckeditor bookmarks in this case.
        // We've already created them in toolbox.ts which calls this.
        const createCkEditorBookMarks = false;
        model.doMarkup(createCkEditorBookMarks);
    }
    public showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        // Toggle render is handled in newPageReady(), where page reader classes are settled.
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            const restoreDone = $.Deferred<void>();
            const decodableReaderState = (
                settings as unknown as Record<string, string>
            )["decodableReaderState"];
            // This wrapper function keeps Devin happy by ensuring that the promise gets resolved,
            // even in the very unlikely case that setStageNumber fails.
            const runStageRestore = (
                work: () => void | Promise<unknown>,
            ): void => {
                try {
                    Promise.resolve(work()).then(
                        () => restoreDone.resolve(),
                        () => restoreDone.resolve(),
                    );
                } catch {
                    restoreDone.resolve();
                }
            };

            if (decodableReaderState) {
                const decState = decodableReaderState;
                if (decState.startsWith("stage:")) {
                    const parts = decState.split(";");
                    const stage = parseInt(parts[0].substring("stage:".length));
                    const sort = parts[1].substring("sort:".length);
                    // The true's passed here prevent re-saving the state we just read.
                    // One non-obvious implication is that simply opening a stage-4 book
                    // will not switch the default stage for new books to 4. That only
                    // happens when you CHANGE the stage in the toolbox.
                    if (model.sort !== sort) {
                        model.setSort(sort, true);
                    }
                    if (model.stageNumber === stage) {
                        restoreDone.resolve();
                        return restoreDone.promise();
                    }
                    runStageRestore(() => model.setStageNumber(stage, true));
                } else {
                    // old state
                    const stage = parseInt(decState, 10);
                    if (model.stageNumber === stage) {
                        restoreDone.resolve();
                        return restoreDone.promise();
                    }
                    runStageRestore(() => model.setStageNumber(stage, true));
                }
            } else {
                get(
                    "readers/io/defaultStage",
                    (result) => {
                        // Presumably a brand new book. We'd better save the settings we come up with in it.
                        const stage = parseInt(result.data, 10);
                        if (model.stageNumber === stage) {
                            restoreDone.resolve();
                            return;
                        }
                        runStageRestore(() => model.setStageNumber(stage));
                    },
                    () => restoreDone.resolve(),
                );
            }

            return restoreDone.promise();
        });
    }
}
