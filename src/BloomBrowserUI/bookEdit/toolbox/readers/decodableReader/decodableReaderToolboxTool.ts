/// <reference path="../../toolbox.ts" />
/// <reference path="../readerToolsModel.ts" />

import {
    DRTState,
    getTheOneReaderToolsModel,
    MarkupType
} from "../readerToolsModel";
import {
    beginInitializeDecodableReaderTool,
    createToggle,
    isToggleOff
} from "../readerTools";
import { isLongPressEvaluating, ITool } from "../../toolbox";
import theOneLocalizationManager from "../../../../lib/localizationManager/localizationManager";
import { get } from "../../../../utils/bloomApi";
import StyleEditor from "../../../StyleEditor/StyleEditor";

export class DecodableReaderToolboxTool implements ITool {
    public makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    public requiresToolId(): boolean {
        return false;
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            if (settings["decodableReaderState"]) {
                const state = new DRTState();
                const decState = settings["decodableReaderState"];
                if (decState.startsWith("stage:")) {
                    const parts = decState.split(";");
                    const stage = parseInt(parts[0].substring("stage:".length));
                    const sort = parts[1].substring("sort:".length);
                    // The true's passed here prevent re-saving the state we just read.
                    // One non-obvious implication is that simply opening a stage-4 book
                    // will not switch the default stage for new books to 4. That only
                    // happens when you CHANGE the stage in the toolbox.
                    getTheOneReaderToolsModel().setSort(sort, true);
                    getTheOneReaderToolsModel().setStageNumber(stage, true);
                } else {
                    // old state
                    getTheOneReaderToolsModel().setStageNumber(
                        parseInt(decState, 10),
                        true
                    );
                }
            } else {
                get("readers/io/defaultStage", result => {
                    // Presumably a brand new book. We'd better save the settings we come up with in it.
                    getTheOneReaderToolsModel().setStageNumber(
                        parseInt(result.data, 10)
                    );
                });
            }
        });
    }
    public isAlwaysEnabled(): boolean {
        return false;
    }
    public isExperimental(): boolean {
        return false;
    }

    public setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container)
            .find(".bloom-editable")
            .focusout(event => {
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
            .focusin(function() {
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

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    public finishToolLocalization(paneDOM: HTMLElement) {
        // DRT has sort buttons with tooltips that are HTML 'i' elements with 'title' attributes.
        // Update those 'title' attributes from localizationManager.

        const doc = paneDOM.ownerDocument;
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Toolbox.DecodableReaderTool.SortAlphabetically",
                "Sort alphabetically",
                ""
            )
            .done(result => {
                this.setTitleOfI(paneDOM, "sortAlphabetic", result);
            });

        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Toolbox.DecodableReaderTool.SortByWordLength",
                "Sort by word length",
                ""
            )
            .done(result => {
                this.setTitleOfI(paneDOM, "sortLength", result);
            });

        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Toolbox.DecodableReaderTool.SortByFrequency",
                "Sort by frequency",
                ""
            )
            .done(result => {
                // there are actually two here, but JQuery nicely just does it
                this.setTitleOfI(paneDOM, "sortFrequency", result);
            });
    }

    public setTitleOfI(paneDOM: HTMLElement, rootId: string, val: string) {
        // Apparently in some cases asyncGetText may return before the document is ready.
        if (!paneDOM || !paneDOM.ownerDocument) return;
        $(paneDOM.ownerDocument).ready(() => {
            if (!paneDOM || !paneDOM.ownerDocument) return;
            $(paneDOM.ownerDocument)
                .find("#" + rootId)
                .find("i")
                .attr("title", val);
        });
    }

    public configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }

    public showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.

        const isForLeveled = false;
        createToggle(isForLeveled);
    }

    public newPageReady() {
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by detachFromPage() on the old page.
        // So we do want to set the appropriate markup, but if the toggle is off, we want the markup off.
        const isForLeveled = false;
        getTheOneReaderToolsModel().setMarkupType(
            isToggleOff(isForLeveled) ? 0 : 1
        );
        // usually updateMarkup will do this, unless we are coming from showTool
        getTheOneReaderToolsModel().doMarkup();
    }

    public hideTool() {
        // nothing to do here (if this class eventually extends our React Adaptor, this can be removed.)
    }

    public detachFromPage() {
        getTheOneReaderToolsModel().setMarkupType(0);
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
        return "decodableReader";
    }

    public hasRestoredSettings: boolean;
}
