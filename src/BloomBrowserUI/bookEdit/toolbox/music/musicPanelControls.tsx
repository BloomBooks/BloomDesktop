import * as React from "react";
import * as ReactDOM from "react-dom";
import { Slider } from "../../../react_components/slider";
import { H1, Div, IUILanguageAwareProps, Label } from "../../../react_components/l10n";
import { Radio } from "../../../react_components/Radio";
import axios from 'axios';

interface IMusicState {
    activeRadioValue: string;
    musicVolume: number; // 0-1.0
    audioEnabled: boolean;
    musicName: string;
}

export default class MusicPanelControls extends React.Component<{}, IMusicState> {
    constructor() {
        super();
        this.state = this.getStateFromHtml();
    }

    getStateFromHtml(): IMusicState {
        let audioStr = this.getPage().find(".bloom-page").attr("data-music");
        let hasMusicAttr = typeof (audioStr) == typeof (""); // may be false or undefined if missing
        if (!audioStr) {
            audioStr = ""; // null won't handle split
        }
        const state = { activeRadioValue: "continueMusic", musicVolume: 1.0, audioEnabled: false, musicName: "" }; // default state
        if (!hasMusicAttr) {
            // No data-music attr at all is our default state, continue from previous page
            // (including possibly no audio, if previous page had none). If audio is set on a previous
            // page, it can flow over into this one.
        } else if (audioStr) {
            // If we have a non-empty music attr, we're setting new music right here.
            state.activeRadioValue = "newMusic";
            state.audioEnabled = true;
            state.musicVolume = this.getAudioVolume(audioStr);
            state.musicName = this.getDisplayNameOfMusicFile(audioStr);
        } else {
            // If we have the attribute, but the value is empty, we're explicitly turning it off.
            state.activeRadioValue = "noMusic";
        }
        return state;
    }

    // This is not very react-ive. In theory, I think, if things can be updated from outside the control,
    // they are props, not state. But then things that can be updated from inside it are state.
    // All these things can be affected from outside (e.g., when we change pages), while the purpose of
    // the control is to change them. Probably in an ideal world, all our state would be props, and we
    // would issue events when we want them changed, and if our parent wants our display to change
    // accordingly, it would change our props. But at least for now, this is the outer boundary of the
    // react stuff, and allowing this component to know how to update the HTML when things change and
    // itself when the HTML changes lets it encapsulate a lot more of the functionality. Hence this
    // method (and the fact that various changes update the HTML rather than raising events).
    public updateStateFromHtml(): void {
        this.setState(this.getStateFromHtml());
    }

    public render() {
        return (
            <div className="musicBody">
                <Div className="musicHelp" l10nKey="EditTab.Toolbox.Music.Overview">You can set up background music to play with this page when the book is viewed in the Bloom Reader app.</Div>

                <Radio wrapClassName="musicOption" labelClassName="musicLabelWrapper" inputClassName="musicButton" l10nKey="EditTab.Toolbox.Music.NoMusic" group="music" value="noMusic"
                    change={val => this.setRadio(val)} groupValue={this.state.activeRadioValue}>No Music</Radio>
                <Radio wrapClassName="musicOption" labelClassName="musicLabelWrapper" inputClassName="musicButton" l10nKey="EditTab.Toolbox.Music.ContinueMusic" group="music" value="continueMusic"
                    change={val => this.setRadio(val)} groupValue={this.state.activeRadioValue}>Continue music from previous page</Radio>
                <div className="musicChooseWrapper">
                    <Radio wrapClassName="musicOption" labelClassName="musicLabelWrapper" inputClassName="musicButton" l10nKey="EditTab.Toolbox.Music.NewMusic" group="music" value="newMusic"
                        change={val => this.setRadio(val)} groupValue={this.state.activeRadioValue}>Start new music</Radio>
                    <Label className="musicChooseFile" l10nKey="EditTab.Toolbox.Music.Choose" onClick={() => this.chooseMusicFile()}>Choose...</Label>
                </div>

                <div className="button-label-wrapper" id="musicOuterWrapper">
                    <div id="musicPlayAndLabelWrapper">
                        <div className="musicButtonWrapper">
                            <button id="musicPreview" className="music-button ui-button enabled" onClick={() => this.previewMusic()} />
                        </div>
                        <div id="musicFilename" >{this.state.musicName}</div>
                    </div>
                    <div id="musicVolumePercent" style={{ visibility: this.state.audioEnabled ? "visible" : "hidden" }}>{Math.round(100 * this.state.musicVolume)}%</div>
                    <div id="musicSetVolume">
                        <img className="speaker-volume" src="speaker-volume.png" />
                        <div className="bgSliderWrapper">
                            <Slider id="musicVolumeSlider" value={100 * this.state.musicVolume} enabled={this.state.audioEnabled} onChange={
                                value => this.sliderMoved(value)} />
                        </div>
                    </div>
                </div>
                {   // preload=none prevents the audio element from asking for the audio as soon as it gets a new src value,
                    // which in BL-3153 was faster than the c# thread writing the file could finish with it.
                    // As an alternative, a settimeout() in the javascript also worked, but
                    // this seems more durable. By the time the user can click Play, we'll be done.}
                }
                <audio id="musicPlayer" preload="none" />
            </div>
        );
    }

    previewMusic() {
        let audioStr = this.getPage().find(".bloom-page").attr("data-music");
        if (!audioStr) {
            return;
        }
        var player = $('#musicPlayer');
        var bookSrc = this.getPageFrame().src;
        var index = bookSrc.lastIndexOf('/');
        var bookFolderUrl = bookSrc.substring(0, index + 1);
        var musicUrl = encodeURI(bookFolderUrl + 'audio/' + audioStr);
        // The ?nocache argument is ignored, except that it ensures each time we do this,
        // src is a different URL, so the player treats it as a new sound to play.
        // Without this it may not play if it hasn't changed.
        player.attr('src', musicUrl + "?nocache=" + new Date().getTime());
        const rawPlayer = document.getElementById('musicPlayer') as HTMLMediaElement;
        rawPlayer.volume = this.getAudioVolume(audioStr);
        rawPlayer.play();
    }

    setRadio(val: string) {
        if (val === this.state.activeRadioValue)
            return;
        const audioEnabled = val === "newMusic";
        this.setState({ activeRadioValue: val, audioEnabled: audioEnabled, musicName: "" });
        if (!audioEnabled) {
            const rawPlayer = (document.getElementById('musicPlayer') as HTMLMediaElement);
            rawPlayer.pause();
        }
        switch (val) {
            case "noMusic":
                this.getPage().find(".bloom-page").attr("data-music", "");
                break;
            case "continueMusic":
                this.getPage().find(".bloom-page").removeAttr("data-music");
                break;
            // choosing the third button doesn't change anything, until you actually choose a file. 
        }
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


    public getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById('page') as HTMLIFrameElement;
    }

    // The body of the editable page, a root for searching for document content.
    public getPage(): JQuery {
        var page = this.getPageFrame();
        if (!page) return null;
        return $(page.contentWindow.document.body);
    }

    // Position is a number between 0 and 100
    sliderMoved(position: number): void {
        //$("#musicVolumePercent").text(position + "%");
        this.getPage().find(".bloom-page").attr("data-musicvolume", position / 100);
        const rawPlayer = (document.getElementById('musicPlayer') as HTMLMediaElement);
        rawPlayer.volume = position / 100;
        this.setState((prevState, props) => { return { musicVolume: position / 100 }; });
    }

    chooseMusicFile() {
        axios.get("/bloom/api/music/ui/chooseFile").then(result => {
            var fileName = result.data;
            if (!fileName) {
                return;
            }
            this.getPage().find(".bloom-page").attr("data-music", fileName);
            this.setState({ activeRadioValue: "newMusic", audioEnabled: true, musicName: this.getDisplayNameOfMusicFile(fileName) });
        });
    }

    getDisplayNameOfMusicFile(fileName: string) {
        return fileName.split('.')[0];
    }

    public static setup(root): MusicPanelControls {
        return ReactDOM.render(
            <MusicPanelControls />,
            root
        );
    }
}