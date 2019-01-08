/* 
bloom-player-preview wraps bloom-player-core and adds just enough controls to preview the 
book inside of the Bloom:Publish:Android screen.
*/
import * as React from "react";
import BloomPlayerCore from "./bloom-player-core";

// This component is designed to wrap a BloomPlayer with some controls
// for things like pausing audio and motion, hiding and showing
// image descriptions. The current version is pretty crude, just enough
// for testing the BloomPlayer narration functions.

interface IProps {
    url: string; // of the bloom book (folder)
    showContextPages?: boolean;
}
interface IState {
    paused: boolean;
}
export default class BloomPlayerControls extends React.Component<
    IProps & React.HTMLProps<HTMLDivElement>,
    IState
> {
    public readonly state: IState = {
        paused: false
    };

    public render() {
        return (
            <div
                {...this.props} // Allow all standard div props
            >
                <button onClick={() => this.setState({ paused: false })}>
                    Play
                </button>
                <button onClick={() => this.setState({ paused: true })}>
                    Pause
                </button>
                <BloomPlayerCore
                    url={this.props.url}
                    showContextPages={this.props.showContextPages}
                    paused={this.state.paused}
                />
            </div>
        );
    }
}
