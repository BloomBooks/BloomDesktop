/// <reference path="../../toolbox.ts" />
/// <reference path="../readerToolsModel.ts" />

import {DRTState, theOneReaderToolsModel, MarkupType} from "../readerToolsModel";
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
                    theOneReaderToolsModel.setSort(sort);
                    theOneReaderToolsModel.setStageNumber(stage);
                } else {
                    // old state
                    theOneReaderToolsModel.setStageNumber(parseInt(decState));
                }
            }
        });
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            theOneReaderToolsModel.doMarkup();
        });

        $(container).find('.bloom-editable').focusin(function () {
            theOneReaderToolsModel.noteFocus(this); // 'This' is the element that just got focus.
        });

        $(container).find('.bloom-editable').keydown(function(e) {
            if ((e.keyCode == 90 || e.keyCode == 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (theOneReaderToolsModel.currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode == 89) { // ctrl-shift-z or ctrl-y
                        theOneReaderToolsModel.redo();
                    } else {
                        theOneReaderToolsModel.undo();
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
        theOneReaderToolsModel.setCkEditorLoaded(); // we don't call showTool until it is.
        if (!theOneReaderToolsModel.setMarkupType(1)) theOneReaderToolsModel.doMarkup();
    }

    hideTool() {
        theOneReaderToolsModel.setMarkupType(0);
    }

    updateMarkup() {
        theOneReaderToolsModel.doMarkup();
    }

    name() { return 'decodableReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new DecodableReaderToolboxPanel());

