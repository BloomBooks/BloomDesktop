import axios = require("axios");
import * as React from "react";
import * as ReactDOM from "react-dom";

interface ComponentProps { }

interface ComponentState {
    progress: string;
}

export default class ProgressBox extends React.Component<ComponentProps, ComponentState> {
    constructor(props) {
        super(props);
        let self = this;
        this.state = { progress: "" };
        //get progress messages from c#
        this.getWebSocket().addEventListener("message", event => {
            var e = JSON.parse(event.data);
            if (e.id === "progress") {
                self.setState({ progress: self.state.progress + "<br/>" + e.payload });
            }
        });
    }

    readonly kSocketName = "webSocket";

    render() {
        return (
            <div id="progress" dangerouslySetInnerHTML={{ __html: this.state.progress }} />
        );
    }

    private getWebSocket(): WebSocket {
        if (typeof window.top[this.kSocketName] === "undefined") {
            // Enhance: ask the server for the socket so that we aren't assuming that it is the current port + 1
            let websocketPort = parseInt(window.location.port, 10) + 1;
            window.top[this.kSocketName] = new WebSocket("ws://127.0.0.1:" + websocketPort.toString());
        }
        return window.top[this.kSocketName];
    }
}
