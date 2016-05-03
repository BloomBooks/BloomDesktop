/// <reference path="../../toolbox.ts" />
/// <reference path="../readerToolsModel.ts" />

import {DRTState, ReaderToolsModel, MarkupType} from "../readerToolsModel";
import {beginInitializeDecodableReaderTool} from "../readerTools";
import {ITabModel} from "../../toolbox";
import {ToolBox} from "../../toolbox";
import {theOneLibSynphony}  from './../libSynphony/synphony_lib';

export default class DecodableReaderToolboxPanel implements ITabModel {
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        if (!ReaderToolsModel.model) ReaderToolsModel.model = new ReaderToolsModel();
        return beginInitializeDecodableReaderTool().then(() => {
            if (settings['decodableReaderState']) {
                var state = theOneLibSynphony.dbGet('drt_state');
                if (!state) state = new DRTState();
                var decState = settings['decodableReaderState'];
                if (decState.startsWith("stage:")) {
                    var parts = decState.split(";");
                    var stage = parseInt(parts[0].substring("stage:".length));
                    var sort = parts[1].substring("sort:".length);
                    ReaderToolsModel.model.setSort(sort);
                    ReaderToolsModel.model.setStageNumber(stage);
                } else {
                    // old state
                    ReaderToolsModel.model.setStageNumber(parseInt(decState));
                }
            }
        });
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            if (ReaderToolsModel.model) {
                ReaderToolsModel.model.doMarkup();
            }
        });

        $(container).find('.bloom-editable').focusin(function () {
            if (ReaderToolsModel.model) {
                ReaderToolsModel.model.noteFocus(this); // 'This' is the element that just got focus.
            }
        });

        $(container).find('.bloom-editable').keydown(function(e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (ReaderToolsModel.model.currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        ReaderToolsModel.model.redo();
                    } else {
                        ReaderToolsModel.model.undo();
                    }
                    return false;
                }
            }
        });
    }

    configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }

    showTool() {
        // change markup based on visible options
        ReaderToolsModel.model.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!ReaderToolsModel.model.setMarkupType(1)) ReaderToolsModel.model.doMarkup();
    }

    hideTool() {
        ReaderToolsModel.model.setMarkupType(0);
    }

    updateMarkup() {
        ReaderToolsModel.model.doMarkup();
    }

    name() { return 'decodableReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new DecodableReaderToolboxPanel());

