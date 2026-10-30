import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import { DecodableReaderToolControls } from "./DecodableReaderToolControls";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { get } from "../../../../utils/bloomApi";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { isLongPressEvaluating, IToolboxSettings } from "../../toolbox";
import $ from "jquery";

// This class renders the DecodableReaderToolControls React component
// in the toolbox, and implements all the functionality/logic needed
// for detaching the tool, reattaching the tool, updating the markup,
// restoring the tool's state (which includes the saved stage
// number, sorting type, and current graphemes and words for that stage),
// and handling certain events such as focsuing in an element (such as
// a textbox) and focusing out of an element.
export class DecodableReaderTool extends ToolboxToolReactAdaptor {
    public renderPanel(): JSX.Element {
        return (
            <div>
                <DecodableReaderToolControls />
            </div>
        );
    }
    public id(): string {
        return "decodableReader";
    }
    /** The icon for this tool's header in the toolbox. */
    public iconPath(): string {
        return "/bloom/images/keys-white.png";
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
    }
    /** Mark up the page again after typing; the toolbox's markup pass calls this. */
    public updateMarkup() {
        getTheOneReaderToolsModel().doMarkup();
    }
    public showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        // Toggle render is handled in newPageReady(), where page reader classes are settled.
    }
    /** Restores the stage (and sort order) this book was last using; see ITool. */
    public async beginRestoreSettings(
        settings: IToolboxSettings,
    ): Promise<void> {
        // This restore supersedes any stage still waiting for the settings; see
        // forgetStageAwaitingSynphony().
        getTheOneReaderToolsModel().forgetStageAwaitingSynphony();
        await beginInitializeDecodableReaderTool();
        const model = getTheOneReaderToolsModel();
        // settings can be undefined/null at runtime for a book with no saved toolbox
        // settings; guard before indexing so we fall through to the default-stage path
        // instead of throwing an unhandled rejection that aborts tool activation (the
        // leveled reader hit exactly this: Sentry BLOOM-DESKTOP-FFH).
        const decodableReaderState = settings?.["decodableReaderState"];
        // This wrapper function ensures that we still finish restoring,
        // even in the very unlikely case that setStageNumber fails.
        const runStageRestore = async (
            work: () => void | Promise<unknown>,
        ): Promise<void> => {
            try {
                await work();
            } catch {
                // Nothing useful we can do about it; don't hold up showing the tool.
            }
        };

        if (decodableReaderState) {
            if (decodableReaderState.startsWith("stage:")) {
                const parts = decodableReaderState.split(";");
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
                    return;
                }
                await runStageRestore(() => model.setStageNumber(stage, true));
            } else {
                // old state
                const stage = parseInt(decodableReaderState, 10);
                if (model.stageNumber === stage) {
                    return;
                }
                await runStageRestore(() => model.setStageNumber(stage, true));
            }
            return;
        }

        await new Promise<void>((resolve) => {
            get(
                "readers/io/defaultStage",
                (result) => {
                    // Presumably a brand new book. We'd better save the settings we come up with in it.
                    const stage = parseInt(result.data, 10);
                    if (model.stageNumber === stage) {
                        resolve();
                        return;
                    }
                    runStageRestore(() => model.setStageNumber(stage)).then(
                        () => resolve(),
                    );
                },
                () => resolve(),
            );
        });
    }
    /**
     * Mark up the page again when a box loses focus, so that the highlights match its text even
     * when the focus left before the markup pass that follows typing. doMarkup only reads the
     * boxes, so this writes nothing that the page history records, wherever the focus goes,
     * the toolbox included.
     */
    public setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container)
            .find(".bloom-editable")
            .focusout(() => {
                if (window?.top?.[isLongPressEvaluating]) {
                    // This gets raised, for no reason I can see, when you click on one
                    // of the longpress buttons. The bloom-editable is the target. It makes no
                    // sense that it should be losing focus at this point, but it happens.
                    // Markup will be sorted out when the long press is done (at keyup), so
                    // there is nothing to gain from doing it now.
                    return;
                }
                getTheOneReaderToolsModel().doMarkup();
            });
    }
    public configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }
}
