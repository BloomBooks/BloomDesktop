import * as React from "react";
import * as ReactDOM from "react-dom";
import WebSocketManager from "../utils/WebSocketManager";


interface ComponentState {
    progress: string;
}
interface ComponentProps {
    lifetimeLabel: string;
}
// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
export default class ProgressBox extends React.Component<ComponentProps, ComponentState> {
    constructor(props: ComponentProps) {
        super(props);
        let self = this;
        this.state = { progress: "" };
        //get progress messages from c#
        WebSocketManager.addListener(props.lifetimeLabel, event => {
            var e = JSON.parse(event.data);
            if (e.id === "progress") {
                self.setState({ progress: self.state.progress + "<br/>" + e.payload });
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
