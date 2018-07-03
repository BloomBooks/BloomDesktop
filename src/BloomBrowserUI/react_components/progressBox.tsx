import * as React from "react";
import * as ReactDOM from "react-dom";
import WebSocketManager from "../utils/WebSocketManager";

interface IProgressBoxProps {
    clientContext: string;
    // If the client is going to start doing something right away that will
    // cause progress messages to happen, it had better wait until this is invoked;
    // otherwise, some of the early ones may be lost. This function will be called
    // once, immediately if the socket is already open, otherwise, as soon as
    // it is in the "OPEN" state where messages can be received (and sent).
    onReadyToReceive?: () => void;
}

interface IProgressState {
    progress: string;
}

// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
export default class ProgressBox extends React.Component<
    IProgressBoxProps,
    IProgressState
> {
    constructor(props: IProgressBoxProps) {
        super(props);
        let self = this;
        this.state = { progress: "" };
        //get progress messages from c#
        WebSocketManager.addListener(props.clientContext, e => {
            if (e.id === "progress") {
                if (e.cssStyleRule) {
                    this.writeLine(
                        `<span style='${e.cssStyleRule}'>${e.message}</span>`
                    );
                } else {
                    this.writeLine(e.message);
                }
                this.tryScrollToBottom();
            }
        });
        if (props.onReadyToReceive) {
            WebSocketManager.notifyReady(
                props.clientContext,
                props.onReadyToReceive
            );
        }
    }

    public write(htmlToAdd: string) {
        this.setState({
            progress: this.state.progress + htmlToAdd
        });
    }
    public writeLine(htmlToAdd: string) {
        this.write(htmlToAdd + "<br/>");
    }

    private tryScrollToBottom() {
        // Must be done AFTER painting once, so we
        // get a real current scroll height.
        let progressDiv = document.getElementById("progress-box");

        // in my testing in FF, this worked the first time
        if (progressDiv) progressDiv.scrollTop = progressDiv.scrollHeight;
        // but there apparently have been times when the div wasn't around
        // yet, so we do this:
        else {
            window.requestAnimationFrame(() => this.tryScrollToBottom());
            // note, I have tested what happens if the element is *never* found (due to some future bug).
            // Tests show that everyhing stays responsive.
        }
    }

    public render() {
        return (
            <div
                id="progress-box"
                dangerouslySetInnerHTML={{ __html: this.state.progress }}
            />
        );
    }
}
