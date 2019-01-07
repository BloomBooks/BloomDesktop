import * as React from "react";
import BloomPlayerCore from "./bloom-player-core";

// This component is designed to wrap a BloomPlayer with some controls
// for things like pausing audio and motion, hiding and showing
// image descriptions. The current version is pretty crude, just enough
// for testing the BloomPlayer narration functions.

interface IBloomControlsProps {
    url: string; // of the bloom book (folder)
    showContext?: string; // currently may be "no" or "yes"
}
interface IState {
    paused: boolean;
}
export default class BloomPlayerControls extends React.Component<
    IBloomControlsProps,
    IState
> {
    public readonly state: IState = {
        paused: false
    };

    public render() {
        return (
            <div>
                <button onClick={() => this.setState({ paused: false })}>
                    Play
                </button>
                <button onClick={() => this.setState({ paused: true })}>
                    Pause
                </button>
                <BloomPlayerCore
                    url={this.props.url}
                    showContext={this.props.showContext}
                    paused={this.state.paused}
                />
            </div>
        );
    }
}
