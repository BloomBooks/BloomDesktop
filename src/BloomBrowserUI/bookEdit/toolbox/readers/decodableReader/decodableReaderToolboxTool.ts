/// <reference path="../../toolbox.ts" />
/// <reference path="../readerToolsModel.ts" />

import { DRTState, getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { ITabModel } from "../../toolbox";
import { ToolBox } from "../../toolbox";
import { theOneLibSynphony } from './../libSynphony/synphony_lib';
import theOneLocalizationManager from '../../../../lib/localizationManager/localizationManager';


export default class DecodableReaderToolboxTool implements ITabModel {
    makeRootElements(): JQuery {
        throw new Error("Method not implemented.");
    }
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            if (settings['decodableReaderState']) {
                var state = new DRTState();
                var decState = settings['decodableReaderState'];
                if (decState.startsWith("stage:")) {
                    var parts = decState.split(";");
                    var stage = parseInt(parts[0].substring("stage:".length));
                    var sort = parts[1].substring("sort:".length);
                    getTheOneReaderToolsModel().setSort(sort);
                    getTheOneReaderToolsModel().setStageNumber(stage);
                } else {
                    // old state
                    getTheOneReaderToolsModel().setStageNumber(parseInt(decState));
                }
            }
        });
    }
    isAlwaysEnabled(): boolean {
        return false;
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            getTheOneReaderToolsModel().doMarkup();
        });

        $(container).find('.bloom-editable').focusin(function () {
            getTheOneReaderToolsModel().noteFocus(this); // 'This' is the element that just got focus.
        });

        $(container).find('.bloom-editable').keydown(function (e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (getTheOneReaderToolsModel().currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        getTheOneReaderToolsModel().redo();
                    } else {
                        getTheOneReaderToolsModel().undo();
                    }
                    return false;
                }
            }
        });
    }

    // Some things were impossible to do i18n on via the jade/pug
    // This gives us a hook to finish up the more difficult spots
    finishTabPaneLocalization(paneDOM: HTMLElement) {
        // DRT has sort buttons with tooltips that are HTML 'i' elements with 'title' attributes.
        // Update those 'title' attributes from localizationManager.

        var doc = paneDOM.ownerDocument;
        theOneLocalizationManager.asyncGetText('EditTab.Toolbox.DecodableReaderTool.SortAlphabetically', 'Sort alphabetically', "")
            .done(function (result) {
                $(doc.getElementById('sortAlphabetic')).find('i').attr('title', result);
            });

        theOneLocalizationManager.asyncGetText('EditTab.Toolbox.DecodableReaderTool.SortByWordLength', 'Sort by word length', "")
            .done(function (result) {
                $(doc.getElementById('sortLength')).find('i').attr('title', result);
            });

        theOneLocalizationManager.asyncGetText('EditTab.Toolbox.DecodableReaderTool.SortByFrequency', 'Sort by frequency', "")
            .done(function (result) {
                // there are actually two here, but JQuery nicely just does it
                $(doc.getElementById('sortFrequency')).find('i').attr('title', result);
            });
    }

    configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }

    showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        if (!getTheOneReaderToolsModel().setMarkupType(1)) getTheOneReaderToolsModel().doMarkup();
    }

    hideTool() {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    updateMarkup() {
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by hideTool() on the old page.
        getTheOneReaderToolsModel().setMarkupType(1);
        getTheOneReaderToolsModel().doMarkup();
    }

    name() { return 'decodableReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new DecodableReaderToolboxTool());

