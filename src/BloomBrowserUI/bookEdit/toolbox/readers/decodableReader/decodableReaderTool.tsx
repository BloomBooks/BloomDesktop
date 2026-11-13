import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import { DecodableReaderToolControls } from "./DecodableReaderToolControls";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { removeReaderMarkup } from "../removeReaderMarkup";
import { get } from "../../../../utils/bloomApi";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { isLongPressEvaluating, IToolboxSettings } from "../../toolbox";
import { updateMarkupAfterUndoOrRedo } from "../../pageEditingMarkup";
import StyleEditor from "../../../StyleEditor/StyleEditor";
import $ from "jquery";

// This class renders the DecodableReaderToolControls React component
// in the toolbox, and implements all the functionality/logic needed
// for detaching the tool, reattaching the tool, updating the markup,
// restoring the tool's state (which includes the saved stage
// number, sorting type, and current graphemes and words for that stage),
// and handling certain events such as focsuing in an element (such as
// a textbox), focusing out of an element, and undo/redo.
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
    // See removeReaderMarkup.
    public removeToolMarkup(pageOrClone: HTMLElement): void {
        removeReaderMarkup(pageOrClone);
    }

    public detachFromPage(): void {
        super.detachFromPage();
        // Stop further typing from being marked up again.
        getTheOneReaderToolsModel().setMarkupType(0);
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
    /** Restores the stage (and sort order) this book was last using; see ITool. */
    public beginRestoreSettings(settings: IToolboxSettings): Promise<void> {
        return getTheOneReaderToolsModel().whileRestoringPhase(() =>
            this.restoreSettings(settings),
        );
    }

    private async restoreSettings(settings: IToolboxSettings): Promise<void> {
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
    public setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container)
            .find(".bloom-editable")
            .focusout((event) => {
                let createCkEditorBookMarks = true;
                // We don't want to create bookmarks if the focus is staying in this page: going to
                // another text box, or to nothing at all, as when the user clicks the dark area
                // around the page. Restoring the bookmark puts the focus back into the text box
                // we are leaving, so switching text boxes would be impossible. And when it pulls
                // the focus back from nowhere, ckeditor (whose own focus-out handler runs after
                // this one and locks its selection) is left focused with that locked selection,
                // and since the box never loses the focus again, nothing unlocks it. Every later
                // markup pass then records the caret where the lock says it is, and so puts the
                // caret back where it was before the click away, wherever the user has clicked
                // or typed since (BL-16916).
                // Bookmarks are only worth having when the focus leaves Bloom's window (for
                // another program), from where it can come back to this box without a click
                // that puts the caret somewhere. A click anywhere in the window, the toolbox
                // included, counts as staying: while this event runs, the page's document
                // still has the focus, since the browser hands it to the toolbox's frame only
                // afterwards.
                if (event.target.ownerDocument.hasFocus())
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
                        // Both of those restore a saved innerHTML, which replaces the text
                        // nodes our highlights are painted over, so the markup has to be
                        // redone before they can paint anything again (BL-16558).
                        updateMarkupAfterUndoOrRedo();
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
