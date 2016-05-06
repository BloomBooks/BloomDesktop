/// <reference path="../../toolbox.ts" />
/// <reference path="../readerToolsModel.ts" />

import {DRTState, ReaderToolsModel, MarkupType} from "../readerToolsModel";
import {beginInitializeDecodableReaderTool} from "../readerTools";
import {ITabModel} from "../../toolbox";
import {ToolBox} from "../../toolbox";
import {theOneLibSynphony}  from './../libSynphony/synphony_lib';

export default class DecodableReaderToolboxPanel implements ITabModel {
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            if (settings['decodableReaderState']) {
                var state = theOneLibSynphony.dbGet('drt_state');
                if (!state) state = new DRTState();
                var decState = settings['decodableReaderState'];
                if (decState.startsWith("stage:")) {
                    var parts = decState.split(";");
                    var stage = parseInt(parts[0].substring("stage:".length));
                    var sort = parts[1].substring("sort:".length);
                    ReaderToolsModel.setSort(sort);
                    ReaderToolsModel.setStageNumber(stage);
                } else {
                    // old state
                    ReaderToolsModel.setStageNumber(parseInt(decState));
                }
            }
        });
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            ReaderToolsModel.doMarkup();
        });

        $(container).find('.bloom-editable').focusin(function () {
            ReaderToolsModel.noteFocus(this); // 'This' is the element that just got focus.
        });

        $(container).find('.bloom-editable').keydown(function(e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (ReaderToolsModel.currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        ReaderToolsModel.redo();
                    } else {
                        ReaderToolsModel.undo();
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
        ReaderToolsModel.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!ReaderToolsModel.setMarkupType(1)) ReaderToolsModel.doMarkup();
    }

    hideTool() {
        ReaderToolsModel.setMarkupType(0);
    }

    updateMarkup() {
        ReaderToolsModel.doMarkup();
    }

    name() { return 'decodableReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new DecodableReaderToolboxPanel());

