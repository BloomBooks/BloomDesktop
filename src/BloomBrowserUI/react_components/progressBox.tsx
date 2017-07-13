import * as React from "react";
import * as ReactDOM from "react-dom";

interface ComponentProps {
    webSocket: WebSocket;
}

interface ComponentState {
    progress: string;
}

// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
export default class ProgressBox extends React.Component<ComponentProps, ComponentState> {
    constructor(props) {
        super(props);
        let self = this;
        this.state = { progress: "" };
        //get progress messages from c#
        props.webSocket.addEventListener("message", event => {
            var e = JSON.parse(event.data);
            if (e.id === "progress") {
                self.setState({ progress: self.state.progress + "<br/>" + e.payload });
            }
        });
    }

    //TODO: make box messages scroll to bottom whenever a new message arrives
    // (or alternatively, when a new message arrives and the scroll was previously at the bottom).
    render() {
        return (
            <div id="progress" dangerouslySetInnerHTML={{ __html: this.state.progress }} />
        );
    }
}
