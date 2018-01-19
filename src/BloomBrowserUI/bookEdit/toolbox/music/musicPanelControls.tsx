import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps } from "../../../react_components/l10n";
import { Radio } from "../../../react_components/Radio";

interface IMusicState {
    activeRadioValue: string;
    backgroundAudioVolume: number; // 0-1.0
}

export default class MusicPanelControls extends React.Component<{}, IMusicState> {
    public render() {
        return (
            <div className="musicBody">
                <Div className="musicHelp" l10nKey="EditTab.Toolbox.Music.Overview">You can set up background music to play with this page when the book is viewed in the Bloom Reader app.</Div>
                <Radio wrapClassName="musicOption" labelClassName="bgLabelWrapper" inputClassName="musicButton" l10nKey="EditTab.Toolbox.Music.NoMusic" group="music" value="noMusic">No Music</Radio>
                <Radio wrapClassName="musicOption" labelClassName="bgLabelWrapper" inputClassName="musicButton" l10nKey="EditTab.Toolbox.Music.ContinueMusic" group="music" value="continueMusic">Continue music from previous page</Radio>
                <Radio wrapClassName="musicOption" labelClassName="bgLabelWrapper" inputClassName="musicButton" l10nKey="EditTab.Toolbox.Music.NewMusic" group="music" value="newMusic">Start new music</Radio>
                <div className="button-label-wrapper" id="musicOuterWrapper">
                    <div id="musicPlayAndLabelWrapper">
                        <div className="musicButtonWrapper">
                            <button id="musicPreview" className="music-button ui-button enabled" />
                        </div>
                        <div id="musicFilename" />
                    </div>
                    <div id="musicVolumePercent">100%</div>
                    <div id="musicSetVolume">
                        <img className="speaker-volume" src="speaker-volume.png" />
                        <div className="bgSliderWrapper">
                            <div id="musicVolumeSlider" />
                        </div>
                    </div>
                </div>
                {   // preload=none prevents the audio element from asking for the audio as soon as it gets a new src value,
                    // which in BL-3153 was faster than the c# thread writing the file could finish with it.
                    // As an alternative, a settimeout() in the javascript also worked, but
                    // this seems more durable. By the time the user can click Play, we'll be done.}
                }
                <audio id="bgPlayer" preload="none" />
            </div>
        );
    }

    public static setup(root) {
        ReactDOM.render(
            <MusicPanelControls />,
            root
        );
    }
}