/// <reference path="./toolbox.ts" />
import * as React from "react";
import * as ReactDOM from "react-dom";
import { ToolBox, ITool } from "./toolbox";
import axios from "axios";

export class EditTool {
    hasRestoredSettings: boolean;
    // required for ITool interface
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    /* tslint:disable:no-empty */ // We need these to implement the interface, but don't need them to do anything.
    configureElements(container: HTMLElement) {
    }
    showTool() { }
    hideTool() { }
    updateMarkup() { }
    finishToolLocalization(pane: HTMLElement) {
    }
    isAlwaysEnabled() {
        return false;
    }
    /* tslint:enable:no-empty */
    id() {
        return "undefined tool";
    }
}
