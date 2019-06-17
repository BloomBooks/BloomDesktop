/// <reference path="../../toolbox.ts" />
/// <reference path="../readerToolsModel.ts" />

import {
    DRTState,
    getTheOneReaderToolsModel,
    MarkupType
} from "../readerToolsModel";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { ITool } from "../../toolbox";
import theOneLocalizationManager from "../../../../lib/localizationManager/localizationManager";
import { BloomApi } from "../../../../utils/bloomApi";

export class DecodableReaderToolboxTool implements ITool {
    public makeRootElement(): HTMLDivElement {
        throw new Error("Method not implemented.");
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            if (settings["decodableReaderState"]) {
                var state = new DRTState();
                var decState = settings["decodableReaderState"];
                if (decState.startsWith("stage:")) {
                    var parts = decState.split(";");
                    var stage = parseInt(parts[0].substring("stage:".length));
                    var sort = parts[1].substring("sort:".length);
                    // The true's passed here prevent re-saving the state we just read.
                    // One non-obvious implication is that simply opening a stage-4 book
                    // will not switch the default stage for new books to 4. That only
                    // happens when you CHANGE the stage in the toolbox.
                    getTheOneReaderToolsModel().setSort(sort, true);
                    getTheOneReaderToolsModel().setStageNumber(stage, true);
                    console.log(
                        "set stage in beginRestoreSettings to " + stage
                    );
                } else {
                    // old state
                    getTheOneReaderToolsModel().setStageNumber(
                        parseInt(decState, 10),
                        true
                    );
                }
            } else {
                BloomApi.get("readers/io/defaultStage", result => {
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
            .focusout(() => {
                getTheOneReaderToolsModel().doMarkup();
            });

        $(container)
            .find(".bloom-editable")
            .focusin(function() {
                getTheOneReaderToolsModel().noteFocus(this); // 'This' is the element that just got focus.
            });

        $(container)
            .find(".bloom-editable")
            .keydown(
                (e): boolean => {
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
                }
            );
    }

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    public finishToolLocalization(paneDOM: HTMLElement) {
        // DRT has sort buttons with tooltips that are HTML 'i' elements with 'title' attributes.
        // Update those 'title' attributes from localizationManager.

        var doc = paneDOM.ownerDocument;
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
    }

    public newPageReady() {
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by detachFromPage() on the old page.
        getTheOneReaderToolsModel().setMarkupType(1);
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
        getTheOneReaderToolsModel().doMarkup();
    }

    public id() {
        return "decodableReader";
    }

    public hasRestoredSettings: boolean;
}
