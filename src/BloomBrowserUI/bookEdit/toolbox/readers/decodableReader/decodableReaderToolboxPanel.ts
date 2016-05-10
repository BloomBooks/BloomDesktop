﻿/// <reference path="../../toolbox.ts" />
/// <reference path="../readerToolsModel.ts" />

import {DRTState, getTheOneReaderToolsModel, MarkupType} from "../readerToolsModel";
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
                    getTheOneReaderToolsModel().setSort(sort);
                    getTheOneReaderToolsModel().setStageNumber(stage);
                } else {
                    // old state
                    getTheOneReaderToolsModel().setStageNumber(parseInt(decState));
                }
            }
        });
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        // invoke function when a bloom-editable element loses focus.
        $(container).find('.bloom-editable').focusout(function () {
            getTheOneReaderToolsModel().doMarkup();
        });

        $(container).find('.bloom-editable').focusin(function () {
            getTheOneReaderToolsModel().noteFocus(this); // 'This' is the element that just got focus.
        });

        $(container).find('.bloom-editable').keydown(function(e) {
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
        getTheOneReaderToolsModel().doMarkup();
    }

    name() { return 'decodableReader'; }

    hasRestoredSettings: boolean;
}

ToolBox.getTabModels().push(new DecodableReaderToolboxPanel());

