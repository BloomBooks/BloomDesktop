import * as React from "react";
import * as ReactDOM from "react-dom";
import WebSocketManager from "../utils/WebSocketManager";

interface IProgressBoxProps {
    lifetimeLabel: string;
}

interface IProgressState {
    progress: string;
}

// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
export default class ProgressBox extends React.Component<IProgressBoxProps, IProgressState> {
    constructor(props: IProgressBoxProps) {
        super(props);
        let self = this;
        this.state = { progress: "" };
        //get progress messages from c#
        WebSocketManager.addListener(props.lifetimeLabel, event => {
            var e = JSON.parse(event.data);
            if (e.id === "progress") {
                if (e.style) {
                    self.setState({
                        progress:
                            self.state.progress +
                            "<span style='" +
                            e.style +
                            "'>" +
                            e.payload +
                            "</span><br/>"
                    });
                } else {
                    self.setState({
                        progress: self.state.progress + e.payload + "<br/>"
                    });
                }
                this.tryScrollToBottom();
            }
        });
    }

    tryScrollToBottom() {
        // Must be done AFTER painting once, so we
        // get a real current scroll height.
        let progressDiv = document.getElementById("progress-box");

        // in my testing in FF, this worked the first time
        if (progressDiv)
            progressDiv.scrollTop = progressDiv.scrollHeight;
        // but there apparently have been times when the div wasn't around
        // yet, so we do this:
        else {
            window.requestAnimationFrame(() => this.tryScrollToBottom());
            // note, I have tested what happens if the element is *never* found (due to some future bug).
            // Tests show that everyhing stays responsive.
        }
    }

    render() {
        return (
            <div id="progress-box" dangerouslySetInnerHTML={{ __html: this.state.progress }} />
        );
    }
}
