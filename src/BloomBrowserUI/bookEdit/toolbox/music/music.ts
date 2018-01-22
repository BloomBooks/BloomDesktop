// This class supports specifying background audio (typically music) for bloom pages

import * as JQuery from 'jquery';
import * as $ from 'jquery';
import { ITabModel } from "../toolbox";
import { ToolBox } from "../toolbox";
import { EditableDivUtils } from "../../js/editableDivUtils";
import { getPageFrameExports } from '../../js/bloomFrames';
import MusicPanelControls from './musicPanelControls';
import * as React from "react";;
import * as ReactDOM from "react-dom";

export default class Music implements ITabModel {
    reactControls: MusicPanelControls;
    makeRootElements(): JQuery {
        var parts = $("<h3 data-panelId='musicTool' data-i18n='EditTab.Toolbox.Music.Heading'> Music Tool</h3><div data-panelId='musicTool' class='musicBody'/>");
        this.reactControls = MusicPanelControls.setup(parts[1]);
        return parts;
    }
    isAlwaysEnabled(): boolean {
        return false;
    }
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        var result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    configureElements(container: HTMLElement) {
    }
    showTool() {
        this.updateMarkup();
    }
    hideTool() {
        const rawPlayer = (<HTMLMediaElement>document.getElementById('musicPlayer'));
        rawPlayer.pause();
    }
    updateMarkup() {
        // This isn't exactly updating the markup, but it needs to happen when we switch pages,
        // just like updating markup. Using this hook does mean it will (unnecessarily) happen
        // every time the user pauses typing while this tool is active. I don't much expect people
        // to be editing the book and configuring background music at the same time, so I'm not
        // too worried. If it becomes a performance problem, we could enhance ITabModel with a
        // function that is called just when the page switches.
        this.reactControls.updateStateFromHtml();
    }

    name(): string {
        return 'music';
    }
    // required for ITabModel interface
    hasRestoredSettings: boolean;
    finishTabPaneLocalization(pane: HTMLElement) {
    }
}

// Make the one instance of this class and register it with the master toolbox.
ToolBox.getTabModels().push(new Music());


