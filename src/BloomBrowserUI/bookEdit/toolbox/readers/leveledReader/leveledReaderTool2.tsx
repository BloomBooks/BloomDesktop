import { renderRoot } from "../../../../utils/reactRender";
import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import { LeveledReaderToolControls } from "./LeveledReaderToolControls2";
import {
    beginInitializeLeveledReaderTool,
    setReaderToolContentShown,
    setReaderToolToggleShown,
} from "../readerTools";
import { getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { get } from "../../../../utils/bloomApi";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { isLongPressEvaluating } from "../../toolbox";
import StyleEditor from "../../../StyleEditor/StyleEditor";
import $ from "jquery";

export class LeveledReaderTool extends ToolboxToolReactAdaptor {
    private pendingRevealAnimationFrameId: number | undefined;
    private revealGeneration = 0;

    private static readonly kActualCountElementIds = [
        "actualWordsPerPage",
        "actualWordCount",
        "actualSentenceCount",
    ];

    // This class renders the LeveledReaderToolControls React component
    // in the toolbox, while preserving the old leveled-reader lifecycle
    // behavior for restoring settings, updating markup, and coordinating
    // current-page/book statistics with the shared reader tools model.
    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");

        renderRoot(<LeveledReaderToolControls />, root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return "leveledReader2";
    }

    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            const restoreDone = $.Deferred<void>();
            const leveledReaderState = (
                settings as unknown as Record<string, string>
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

    public showTool(): void {
        getTheOneReaderToolsModel().setCkEditorLoaded();
    }

    public detachFromPage(): void {
        this.cancelPendingReveal();
        this.revealGeneration++;
        this.setLeveledToggleShown(true);
        getTheOneReaderToolsModel().setMarkupType(MarkupType.None);
    }

    public newPageReady(): void {
        const shouldShowContent = isReaderToolEnabledOnCurrentPage(true);
        this.showWhenActualCountsReady(shouldShowContent);

        const model = getTheOneReaderToolsModel();
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by detachFromPage() on the old page.
        // So we do want to set the appropriate markup, but if the toggle is off, we want the markup off.
        model.setMarkupType(shouldShowContent ? 2 : 0);
        model.clearWholeBookCache();
        model.updateControlContents();
        // usually updateMarkup will do this, unless we are coming from showTool
        model.doMarkup();
    }

    public updateMarkup(): void {
        // Don't let this lower-level code create ckeditor bookmarks in this case.
        // We've already created them in toolbox.ts which calls this.
        const createCkEditorBookMarks = false;
        getTheOneReaderToolsModel().doMarkup(createCkEditorBookMarks);
    }

    public configureElements(container: HTMLElement): void {
        this.setupReaderKeyAndFocusHandlers(container);
    }

    private cancelPendingReveal(): void {
        if (this.pendingRevealAnimationFrameId !== undefined) {
            window.cancelAnimationFrame(this.pendingRevealAnimationFrameId);
            this.pendingRevealAnimationFrameId = undefined;
        }
    }

    private areActualCountsReady(): boolean {
        return LeveledReaderTool.kActualCountElementIds.every((id) => {
            const value = document.getElementById(id)?.textContent?.trim();
            return !!value && value !== "-";
        });
    }

    private setLeveledContentShown(isShown: boolean): void {
        setReaderToolContentShown(true, isShown);
    }

    private setLeveledToggleShown(isShown: boolean): void {
        setReaderToolToggleShown(true, isShown);
    }

    private showWhenActualCountsReady(shouldShowContent: boolean): void {
        this.cancelPendingReveal();
        const generation = ++this.revealGeneration;

        if (!shouldShowContent) {
            this.setLeveledContentShown(false);
            this.setLeveledToggleShown(true);
            return;
        }

        // Leveled reader needs a second pass to populate the Actual count columns.
        // Keep both the stats pane and its switch hidden until those values are ready,
        // then reveal the final settled UI in one shot.
        this.setLeveledContentShown(false);
        this.setLeveledToggleShown(false);
        const startMs = Date.now();
        const maxWaitMs = 3000;

        const tryReveal = () => {
            if (generation !== this.revealGeneration) {
                return;
            }

            if (
                this.areActualCountsReady() ||
                Date.now() - startMs >= maxWaitMs
            ) {
                this.setLeveledContentShown(true);
                this.setLeveledToggleShown(true);
                this.pendingRevealAnimationFrameId = undefined;
                return;
            }

            this.pendingRevealAnimationFrameId =
                window.requestAnimationFrame(tryReveal);
        };

        tryReveal();
    }

    private setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
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
                if ((e.keyCode === 90 || e.keyCode === 89) && e.ctrlKey) {
                    // ctrl-z or ctrl-Y
                    if (
                        getTheOneReaderToolsModel().currentMarkupType !==
                        MarkupType.None
                    ) {
                        e.preventDefault();
                        if (e.shiftKey || e.keyCode === 89) {
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
}
