import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps } from "../../../react_components/l10n";

interface IMusicState {
    activeRadioValue: string;
    backgroundAudioVolume: number; // 0-1.0
}

export default class MusicPanelControls extends React.Component<{}, IMusicState> {
    public render() {
        return (
            <div data-panelId="musicTool">
                <div className="musicBody">
                    <Div className="musicHelp" l10nKey="EditTab.Toolbox.Music.Overview">You can set up background music to play with this page when the book is viewed in the Bloom Reader app.</Div>
                </div>
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