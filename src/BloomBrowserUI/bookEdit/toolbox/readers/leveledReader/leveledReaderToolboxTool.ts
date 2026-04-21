/// <reference path="../../toolbox.ts" />
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import {
    beginInitializeLeveledReaderTool,
    createToggle,
    isToggleOff,
    setReaderToolContentShown,
    setReaderToolToggleShown,
} from "../readerTools";
import { ITool, ToolBox } from "../../toolbox";
import { get } from "../../../../utils/bloomApi";
import $ from "jquery";

export class LeveledReaderToolboxTool implements ITool {
    private pendingRevealAnimationFrameId: number | undefined;
    private revealGeneration = 0;

    private static readonly kActualCountElementIds = [
        "actualWordsPerPage",
        "actualWordCount",
        "actualSentenceCount",
    ];

    imageUpdated(img: HTMLImageElement | undefined): void {
        // No action needed for this tool
    }
    public makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    public requiresToolId(): boolean {
        return false;
    }
    public beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            const restoreDone = $.Deferred<void>();
            if (opts["leveledReaderState"]) {
                // The true passed here prevents re-saving the state we just read.
                // One non-obvious implication is that simply opening a level-4 book
                // will not switch the default level for new books to 4. That only
                // happens when you CHANGE the level in the toolbox.
                getTheOneReaderToolsModel().setLevelNumber(
                    parseInt(opts["leveledReaderState"], 10),
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
        // Toggle render is handled in newPageReady(), where page reader classes are settled.
    }

    public hideTool() {
        // nothing to do here (if this class eventually extends our React Adaptor, this can be removed.)
    }

    private cancelPendingReveal() {
        if (this.pendingRevealAnimationFrameId !== undefined) {
            window.cancelAnimationFrame(this.pendingRevealAnimationFrameId);
            this.pendingRevealAnimationFrameId = undefined;
        }
    }

    private areActualCountsReady(): boolean {
        return LeveledReaderToolboxTool.kActualCountElementIds.every((id) => {
            const value = document.getElementById(id)?.textContent?.trim();
            return !!value && value !== "-";
        });
    }

    private setLeveledContentShown(isShown: boolean) {
        setReaderToolContentShown(true, isShown);
    }

    private setLeveledToggleShown(isShown: boolean) {
        setReaderToolToggleShown(true, isShown);
    }

    private showWhenActualCountsReady(shouldShowContent: boolean) {
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

    public detachFromPage() {
        this.cancelPendingReveal();
        this.revealGeneration++;
        this.setLeveledToggleShown(true);
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    public newPageReady() {
        const page = ToolBox.getPage();
        if (!page) {
            return;
        }

        // Remount the toggle after the page is ready so it can pick up the
        // current page body's reader classes instead of the initial placeholder state.
        createToggle(true);

        const isForLeveled = true;
        const shouldShowContent =
            page.classList.contains("leveled-reader") ||
            !isToggleOff(isForLeveled);
        this.showWhenActualCountsReady(shouldShowContent);

        // Often we could get away without reloading this, but we might
        // have just deleted a page, or duplicated one, or pasted one...
        getTheOneReaderToolsModel().clearWholeBookCache();
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by detachFromPage() on the old page.
        // So we do want to set the appropriate markup, but if the toggle is off, we want the markup off.
        getTheOneReaderToolsModel().setMarkupType(shouldShowContent ? 2 : 0);
        getTheOneReaderToolsModel().updateControlContents();
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
