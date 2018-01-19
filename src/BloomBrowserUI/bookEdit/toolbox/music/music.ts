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
import axios from 'axios';

export default class Music implements ITabModel {
    makeRootElements(): JQuery {
        var parts = $("<h3 data-panelId='musicTool' data-i18n='EditTab.Toolbox.Music.Heading'> Music Tool</h3><div data-panelId='musicTool' class='musicBody'/>");
        MusicPanelControls.setup(parts[1]);
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
        $("input.musicButton").change(() => this.backgroundRadioChanged());
        $('#musicPlayAndLabelWrapper').click(() => this.previewMusic());
        $('#musicChooseFile').click(ev => {
            this.chooseMusicFile();
        });
        this.updateMarkup();
    }
    hideTool() {
        const rawPlayer = (<HTMLMediaElement>document.getElementById('bgPlayer'));
        rawPlayer.pause();
    }
    updateMarkup() {
        // This isn't exactly updating the markup, but it needs to happen when we switch pages,
        // just like updating markup. Using this hook does mean it will (unnecessarily) happen
        // every time the user pauses typing while this tool is active. I don't much expect people
        // to be editing the book and configuring background music at the same time, so I'm not
        // too worried. If it becomes a performance problem, we could enhance ITabModel with a
        // function that is called just when the page switches.
        let audioStr = this.getPage().find(".bloom-page").attr("data-music");
        let hasBgAudioAttr = typeof (audioStr) == typeof (""); // may be false or undefined if missing
        if (!audioStr) {
            audioStr = ""; // null won't handle split
        }
        $("#musicFilename").text(audioStr.split('.')[0]); // Strip off extension
        const audioVolume = this.getAudioVolume(audioStr);
        $("#musicVolumeSlider").slider({
            value: audioVolume * 100,
            change: (event, ui) => { // possibly redundant
                this.sliderMoved(ui.value);
            },
            slide: (event, ui) => {
                this.sliderMoved(ui.value);
            },
            disabled: !hasBgAudioAttr // can only set volume on the page where we specify the music
        });
        if (!hasBgAudioAttr) {
            // No data-music attr at all is our default state, continue from previous page
            // (including possibly no audio, if previous page had none). If audio is set on a previous
            // page, it can flow over into this one.
            this.selectRadio("continueMusic");
            this.disableAudioControls();
        } else if (audioStr) {
            // If we have a non-empty BG audio attr, we're setting new music right here.
            this.selectRadio("newMusic");
            this.enableAudioControls();
        } else {
            // If we have the attribute, but the value is empty, we're explicitly turning it off.
            this.selectRadio("noMusic");
            this.disableAudioControls();
        }
        this.setBgAudioVolumePercent(audioVolume);
    }

    setBgAudioVolumePercent(audioVolume: number): void {
        $("#musicVolumePercent").text(Math.round(audioVolume * 100) + "%");
    }
    name(): string {
        return 'music';
    }
    // required for ITabModel interface
    hasRestoredSettings: boolean;
    finishTabPaneLocalization(pane: HTMLElement) {
    }

    // Get the audio volume. The value of the data-music attr, which is passed in,
    // is not the source of the volume, but does determine whether data-musicvolume
    // is used at all. If anything goes wrong, or we're not specifying new music for this page,
    // we just set it to 100%.
    getAudioVolume(audioStr: string): number {
        const audioVolumeStr = this.getPage().find(".bloom-page").attr("data-musicvolume");
        let audioVolume: number = 1.0;
        if (audioStr && audioVolumeStr) {
            try {
                audioVolume = parseFloat(audioVolumeStr);
            } catch (e) {
                audioVolume = 1.0;
            }
            if (isNaN(audioVolume) || audioVolume > 1.0 || audioVolume < 0.0) {
                audioVolume = 1.0;
            }
        }
        return audioVolume;
    }

    selectRadio(val: string): void {
        $("input[name='music]").prop("checked", false); // turn all off.
        $("input[value='" + val + "']").prop("checked", true); // desired one on
    }

    // Position is a number between 0 and 100
    sliderMoved(position: number): void {
        $("#musicVolumePercent").text(position + "%");
        this.getPage().find(".bloom-page").attr("data-musicvolume", position / 100);
        const rawPlayer = (<HTMLMediaElement>document.getElementById('bgPlayer'));
        rawPlayer.volume = position / 100;
    }

    backgroundRadioChanged() {
        const chosen = $("input[name='music']:checked").val();
        switch (chosen) {
            case "noMusic":
                this.getPage().find(".bloom-page").attr("data-music", "");
                this.disableAudioControls();
                break;
            case "continueMusic":
                this.getPage().find(".bloom-page").removeAttr("data-music");
                this.disableAudioControls();
                break;
            // choosing the third button doesn't change anything, until you actually choose a file.
        }
    }

    disableAudioControls() {
        this.getPage().find(".bloom-page").removeAttr("data-musicvolume");
        $("#musicVolumeSlider").slider("option", "disabled", true);
        $("#musicVolumeSlider").slider("option", "value", 100);
        this.setBgAudioVolumePercent(1.0);
        $("#musicFilename").text("");
        $('#musicPreview').addClass("disabled");
        const rawPlayer = (<HTMLMediaElement>document.getElementById('bgPlayer'));
        rawPlayer.pause();
    }

    enableAudioControls() {
        $("#musicVolumeSlider").slider("option", "disabled", false);
        $('#musicPreview').removeClass("disabled");
    }
    previewMusic() {
        let audioStr = this.getPage().find(".bloom-page").attr("data-music");
        if (!audioStr) {
            return;
        }
        var player = $('#bgPlayer');
        var bookSrc = this.getPageFrame().src;
        var index = bookSrc.lastIndexOf('/');
        var bookFolderUrl = bookSrc.substring(0, index + 1);
        var musicUrl = encodeURI(bookFolderUrl + 'audio/' + audioStr);
        // The ?nocache argument is ignored, except that it ensures each time we do this,
        // src is a different URL, so the player treats it as a new sound to play.
        // Without this it may not play if it hasn't changed.
        player.attr('src', musicUrl + "?nocache=" + new Date().getTime());
        const rawPlayer = (<HTMLMediaElement>document.getElementById('bgPlayer'));
        rawPlayer.volume = this.getAudioVolume(audioStr);
        rawPlayer.play();
    }

    chooseMusicFile() {
        this.selectRadio("newMusic");
        axios.get("/bloom/api/music/ui/chooseFile").then(result => {
            var fileName = result.data;
            if (!fileName) {
                return;
            }
            $("#musicFilename").text(fileName.split('.')[0]);
            this.getPage().find(".bloom-page").attr("data-music", fileName);
            this.enableAudioControls();
        });
    }

    public getPageFrame(): HTMLIFrameElement {
        return <HTMLIFrameElement>parent.window.document.getElementById('page');
    }

    // The body of the editable page, a root for searching for document content.
    public getPage(): JQuery {
        var page = this.getPageFrame();
        if (!page) return null;
        return $(page.contentWindow.document.body);
    }
}

// Make the one instance of this class and register it with the master toolbox.
ToolBox.getTabModels().push(new Music());


