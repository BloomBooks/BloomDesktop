import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import { DecodableReaderToolControls } from "./DecodableReaderToolControls";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { get } from "../../../../utils/bloomApi";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { renderRoot } from "../../../../utils/reactRender";
import {
    applyToolboxStateToUpdatedPage,
    isLongPressEvaluating,
} from "../../toolbox";
import StyleEditor from "../../../StyleEditor/StyleEditor";

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
        const model = getTheOneReaderToolsModel();
        model.setMarkupType(isReaderToolEnabledOnCurrentPage(false) ? 1 : 0);
        model.updateControlContents();
        // usually updateMarkup will do this, unless we are coming from showTool
        model.doMarkup();
    }
    public detachFromPage(): void {
        getTheOneReaderToolsModel().setMarkupType(0);
        applyToolboxStateToUpdatedPage();
    }
    public updateMarkup() {
        // Don't let this lower-level code create ckeditor bookmarks in this case.
        // We've already created them in toolbox.ts which calls this.
        const createCkEditorBookMarks = false;
        getTheOneReaderToolsModel().doMarkup(createCkEditorBookMarks);
    }
    public showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        // Toggle render is handled in newPageReady(), where page reader classes are settled.
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            const restoreDone = $.Deferred<void>();
            const model = getTheOneReaderToolsModel();
            const decodableReaderState = (
                settings as unknown as Record<string, string>
            )["decodableReaderState"];
            // This wrapper function ensures that the promise gets resolved,
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
    public setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container)
            .find(".bloom-editable")
            .focusout((event) => {
                let createCkEditorBookMarks = true;
                // We don't want to create bookmarks if we are switching from one text box on the page to another.
                // Otherwise, we prevent switching text boxes altogether because the restoration of the bookmark
                // will put focus back into the text box we are trying to leave.
                // relatedTarget is what we are switching to, if anything.
                if (
                    event.relatedTarget &&
                    event.relatedTarget !== event.target &&
                    event.relatedTarget.matches(".bloom-editable")
                )
                    createCkEditorBookMarks = false;
                // If the Format (Styles) dialog is showing, then we don't want to create
                // bookmarks.  The div#format-toolbar is instantiated only when the dialog
                // is showing.  See BL-13043.
                else if (StyleEditor.isStyleDialogOpen())
                    createCkEditorBookMarks = false;
                if (window?.top?.[isLongPressEvaluating]) {
                    // This gets raised, for no reason I can see, when you click on one
                    // of the longpress buttons. The bloom-editable is the target. It makes no
                    // sense that it should be losing focus at this point, but it happens.
                    // Markup will be sorted out when the long press is done (at keyup).
                    // Letting it happen now seems to contribute to the insertion point jumping
                    // back to previous locations (BL-12889), possibly because the call here
                    // creates a bookmark but doesn't use and remove it.
                    return;
                }
                getTheOneReaderToolsModel().doMarkup(createCkEditorBookMarks);
            });

        $(container)
            .find(".bloom-editable")
            .focusin(function () {
                getTheOneReaderToolsModel().noteFocus(this); // 'This' is the element that just got focus.
            });

        $(container)
            .find(".bloom-editable")
            .keydown((e): boolean => {
                if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) {
                    // ctrl-z or ctrl-Y
                    if (
                        getTheOneReaderToolsModel().currentMarkupType !==
                        MarkupType.None
                    ) {
                        e.preventDefault();
                        if (e.shiftKey || e.keyCode == 89) {
                            // ctrl-shift-z or ctrl-y
                            getTheOneReaderToolsModel().redo();
                        } else {
                            getTheOneReaderToolsModel().undo();
                        }
                        return false;
                    }
                }
                return true;
            });
    }
    public configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }
}
