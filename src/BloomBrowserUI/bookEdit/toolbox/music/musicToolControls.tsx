import * as React from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { Div, Label } from "../../../react_components/l10nComponents";
import { RadioGroup, Radio } from "../../../react_components/radio";
import { BloomApi } from "../../../utils/bloomApi";
import Slider from "rc-slider";
import AudioRecording from "../talkingBook/audioRecording";
import "./music.less";

interface IMusicState {
    activeRadioValue: string;
    volume: number; // 0.0..1.0
    volumeSliderPosition: number; // 1..100
    audioEnabled: boolean;
    musicName: string;
    playing: boolean;
}

// This react class implements the UI for the music toolbox.
// Note: this file is included in toolboxBundle.js because webpack.config says to include all
// tsx files in bookEdit/toolbox.
// The toolbox is included in the list of tools because of the one line of immediately-executed code
// which passes an instance of MusicToolAdapter to ToolBox.registerTool();
export class MusicToolControls extends React.Component<{}, IMusicState> {
    public readonly state: IMusicState = {
        activeRadioValue: "continueMusic",
        volume: MusicToolControls.kDefaultVolumeFraction,
        volumeSliderPosition: MusicToolControls.convertVolumeToSliderPosition(
            MusicToolControls.kDefaultVolumeFraction
        ),
        audioEnabled: false,
        musicName: "",
        playing: false
    };
    // duplicates information in HtmlDom.cs
    // The names of the attributes (of the main page div) which store the background
    // audio file name (relative to the audio folder) and the volume (a fraction
    // of full volume).
    private static musicAttrName = "data-backgroundaudio";
    private static musicVolumeAttrName =
        MusicToolControls.musicAttrName + "volume";
    private static kDefaultVolumeFraction = 0.5;
    private static narrationPlayer: AudioRecording | undefined;
    private addedListenerToPlayer: boolean;
    public componentDidMount() {
        this.setState(this.getStateFromHtmlOfPage());
    }

    public pausePlayer() {
        const rawPlayer = document.getElementById(
            "musicPlayer"
        ) as HTMLMediaElement;
        if (rawPlayer) {
            rawPlayer.pause();
        }
    }

    // This method must return a valid parameter for setState().
    // But we can't do the previous way of setting this.state and THEN using our state to setState().
    public getStateFromHtmlOfPage(): IMusicState {
        const musicState: any = {};
        let audioFileName = ToolboxToolReactAdaptor.getBloomPageAttr(
            MusicToolControls.musicAttrName
        );
        const hasMusicAttr = typeof audioFileName === typeof ""; // may be false or undefined if missing
        if (!audioFileName) {
            audioFileName = ""; // null won't handle split
        }
        if (!hasMusicAttr) {
            // No data-backgroundAudio attr at all is our default state, continue from previous page
            // (including possibly no audio, if previous page had none). If audio is set on a previous
            // page, it can flow over into this one.
            musicState.activeRadioValue = "continueMusic";
            musicState.musicName = "";
            musicState.audioEnabled = false;
        } else if (audioFileName) {
            // If we have a non-empty music attr, we're setting new music right here.
            musicState.activeRadioValue = "newMusic";
            musicState.audioEnabled = true;
            musicState.volume = MusicToolControls.getPlayerVolumeFromAttributeOnPage(
                audioFileName
            );
            musicState.volumeSliderPosition = MusicToolControls.convertVolumeToSliderPosition(
                musicState.volume
            );
            musicState.musicName = this.getDisplayNameOfMusicFile(
                audioFileName
            );
        } else {
            // If we have the attribute, but the value is empty, we're explicitly turning it off.
            musicState.activeRadioValue = "noMusic";
            musicState.musicName = "";
            musicState.audioEnabled = false;
            this.pausePlaying(); // pauses player and sets playing state to false
        }
        return musicState as IMusicState;
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
    public updateBasedOnContentsOfPage(): void {
        this.setState(this.getStateFromHtmlOfPage());
    }

    public render() {
        return (
            <div className="musicBody">
                <Div
                    className="musicHelp"
                    l10nKey="EditTab.Toolbox.Music.Overview"
                >
                    You can set up background music to play with this page when
                    the book is viewed in the Bloom Reader app.
                </Div>
                <RadioGroup
                    onChange={val => this.setRadio(val)}
                    value={this.state.activeRadioValue}
                >
                    <Radio
                        l10nKey="EditTab.Toolbox.Music.NoMusic"
                        value="noMusic"
                    >
                        No Music
                    </Radio>
                    <Radio
                        l10nKey="EditTab.Toolbox.Music.ContinueMusic"
                        value="continueMusic"
                    >
                        Continue music from previous page
                    </Radio>
                    <div className="musicChooseWrapper">
                        <Radio
                            l10nKey="EditTab.Toolbox.Music.NewMusic"
                            value="newMusic"
                        >
                            Start new music
                        </Radio>
                        <Label
                            className="musicChooseFile"
                            l10nKey="EditTab.Toolbox.Music.Choose"
                            onClick={() => this.chooseMusicFile()}
                        >
                            Choose...
                        </Label>
                    </div>
                </RadioGroup>
                <div
                    className={
                        "button-label-wrapper" +
                        (this.state.audioEnabled ? "" : " disabled")
                    }
                    id="musicOuterWrapper"
                >
                    <div id="musicPlayAndLabelWrapper">
                        <div className="musicButtonWrapper">
                            <button
                                id="musicPreview"
                                className={
                                    "music-button ui-button enabled" +
                                    (this.state.playing ? " playing" : "")
                                }
                                onClick={() => this.previewMusic()}
                            />
                        </div>
                        <div id="musicFilename">{this.state.musicName}</div>
                    </div>
                    <div
                        id="musicVolumePercent"
                        style={{
                            visibility: this.state.audioEnabled
                                ? "visible"
                                : "hidden"
                        }}
                    >
                        {Math.round(this.state.volume * 100)}%
                    </div>
                    <div id="musicSetVolume">
                        <img
                            className="speaker-volume"
                            src="speaker-volume.svg"
                        />
                        <div className="bgSliderWrapper">
                            <Slider
                                className="musicVolumeSlider"
                                value={this.state.volumeSliderPosition}
                                disabled={!this.state.audioEnabled}
                                onChange={value => this.sliderMoved(value)}
                            />
                        </div>
                    </div>
                </div>
                {
                    // preload=none prevents the audio element from asking for the audio as soon as it gets a new src value,
                    // which in BL-3153 was faster than the c# thread writing the file could finish with it.
                    // As an alternative, a settimeout() in the javascript also worked, but
                    // this seems more durable. By the time the user can click Play, we'll be done.}
                }
                <audio id="musicPlayer" preload="none" />
            </div>
        );
    }

    private getPlayer(): HTMLMediaElement {
        return document.getElementById("musicPlayer") as HTMLMediaElement;
    }

    private previewMusic() {
        const player = this.getPlayer();
        if (!this.addedListenerToPlayer) {
            player.addEventListener("ended", () =>
                this.setState({ playing: false })
            );
            this.addedListenerToPlayer = true;
        }
        MusicToolControls.previewBackgroundMusic(
            player,
            () => this.state.playing,
            playing => this.setState({ playing: playing })
        );
    }

    public static previewBackgroundMusic(
        player: HTMLMediaElement,
        currentlyPlaying: () => boolean, // caller function for testing whether already playing
        setPlayState: (boolean) => void
    ) {
        // call-back function for changing playing state
        // automatic indentation is weird here. This is the start of the body of previewBackgroundMusic,
        // not of setPlayState, which is just a function parameter.
        const audioFileName = ToolboxToolReactAdaptor.getBloomPageAttr(
            this.musicAttrName
        );
        if (!audioFileName) {
            return;
        }
        if (currentlyPlaying()) {
            player.pause();
            if (this.narrationPlayer) {
                this.narrationPlayer.stopListen();
                this.narrationPlayer = undefined;
            }
            setPlayState(false);
            return;
        }
        const bookSrc = ToolboxToolReactAdaptor.getPageFrame().src;
        const index = bookSrc.lastIndexOf("/");
        const bookFolderUrl = bookSrc.substring(0, index + 1);
        const musicUrl = encodeURI(bookFolderUrl + "audio/" + audioFileName);
        // The ?nocache argument is ignored, except that it ensures each time we do this,
        // src is a different URL, so the player treats it as a new sound to play.
        // Without this it may not play if it hasn't changed.
        player.setAttribute(
            "src",
            musicUrl + "?nocache=" + new Date().getTime()
        );
        player.volume = this.getPlayerVolumeFromAttributeOnPage(audioFileName);
        player.play();
        // Play the audio during animation
        this.narrationPlayer = new AudioRecording();
        this.narrationPlayer.setupForListen();
        this.narrationPlayer.listen();
        setPlayState(true);
    }

    private pausePlaying() {
        this.pausePlayer();
        this.setState({ playing: false });
    }

    private setRadio(val: string) {
        const audioEnabled =
            val === "newMusic" && this.state.musicName.length > 0;
        this.setState({
            activeRadioValue: val,
            audioEnabled: audioEnabled
        });
        switch (val) {
            case "noMusic":
                ToolboxToolReactAdaptor.setBloomPageAttr(
                    MusicToolControls.musicAttrName,
                    ""
                );
                this.setState({ musicName: "" });
                this.pausePlaying(); // pauses player and sets playing state to false
                break;
            case "continueMusic":
                const bloomPage = ToolboxToolReactAdaptor.getBloomPage();
                if (bloomPage) {
                    bloomPage.removeAttribute(MusicToolControls.musicAttrName);
                }
                this.setState({ musicName: "" });
                // In any case don't change the state of playing/not playing
                break;
            // choosing the third button doesn't change anything, until you actually choose a file.
        }
    }

    // Get the audio volume. The value of audioFileName is checked to determine whether data-backgroundAudioVolume
    // is used at all. If anything goes wrong, or we're not specifying new music for this page,
    // we return the default volume.
    private static getPlayerVolumeFromAttributeOnPage(
        audioFileName: string
    ): number {
        const audioVolumeStr = ToolboxToolReactAdaptor.getBloomPageAttr(
            this.musicVolumeAttrName
        );
        let audioVolumeFraction: number =
            MusicToolControls.kDefaultVolumeFraction;
        if (audioFileName && audioVolumeStr) {
            try {
                audioVolumeFraction = parseFloat(audioVolumeStr);
            } catch (e) {
                audioVolumeFraction = MusicToolControls.kDefaultVolumeFraction;
            }
            if (
                isNaN(audioVolumeFraction) ||
                audioVolumeFraction > 1.0 ||
                audioVolumeFraction < 0.0
            ) {
                audioVolumeFraction = MusicToolControls.kDefaultVolumeFraction;
            }
        }
        return audioVolumeFraction;
    }

    // Instead of a linear progression, we make the slider be very insensitive on the left,
    // so that you have more control at the low volumes that are likely for background music.
    // We are using 3 but 2 is also ok. Using 3 because the effect feels right to me (subjectively,
    // with a very small sample size).
    private static kLowEndVolumeSensitivity: number = 3;

    // Position is always 0 to 100, and resulting volume is always 0.0 to 1.0, via a non-linear function
    private static convertSliderPositionToVolume(
        position1To100: number
    ): number {
        return Math.pow(
            position1To100 / 100,
            MusicToolControls.kLowEndVolumeSensitivity
        );
    }

    // Volume is always 0.0 to 1.0, and the resulting position is always 0 to 100
    // In between is a non-linear function.
    private static convertVolumeToSliderPosition(
        volumeFraction: number
    ): number {
        return Math.round(
            Math.pow(
                volumeFraction,
                1 / MusicToolControls.kLowEndVolumeSensitivity
            ) * 100
        );
    }

    private sliderMoved(position1to100: number): void {
        const volume = MusicToolControls.convertSliderPositionToVolume(
            position1to100
        );
        ToolboxToolReactAdaptor.setBloomPageAttr(
            MusicToolControls.musicVolumeAttrName,
            volume.toString()
        );
        this.getPlayer().volume = volume;
        this.setState((prevState, props) => {
            return { volume: volume, volumeSliderPosition: position1to100 };
        });
    }

    private chooseMusicFile() {
        BloomApi.get("music/ui/chooseFile", result => {
            const fileName = result.data;
            if (!fileName) {
                return;
            }
            ToolboxToolReactAdaptor.setBloomPageAttr(
                MusicToolControls.musicAttrName,
                fileName
            );
            this.setState({
                activeRadioValue: "newMusic",
                audioEnabled: true,
                musicName: this.getDisplayNameOfMusicFile(fileName)
            });
        });
    }

    private getDisplayNameOfMusicFile(fileName: string) {
        return fileName.split(".")[0];
    }
}

// This class implements the ITool interface through our adaptor's abstract methods by calling
// the appropriate MusicToolControls methods.
export class MusicToolAdaptor extends ToolboxToolReactAdaptor {
    // Resist the temptation to change null to undefined here.
    // This type has to match the 'ref' attribute below, which has "| null".
    private controlsElement: MusicToolControls | null;

    public makeRootElement(): HTMLDivElement {
        return super.adaptReactElement(
            <MusicToolControls
                ref={renderedElement =>
                    (this.controlsElement = renderedElement)
                }
            />
        );
    }

    public id(): string {
        return "music";
    }

    public showTool() {
        if (this.controlsElement) {
            this.controlsElement.updateBasedOnContentsOfPage();
        }
    }
    public hideTool() {
        if (this.controlsElement) {
            this.controlsElement.pausePlayer();
        }
    }
    public newPageReady() {
        if (this.controlsElement) {
            this.controlsElement.updateBasedOnContentsOfPage();
        }
    }
}
