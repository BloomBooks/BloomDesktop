import * as React from "react";
import WebSocketManager from "../utils/WebSocketManager";
import "./progressBox.less";

export interface IProgressBoxProps {
    clientContext: string;
    // If the client is going to start doing something right away that will
    // cause progress messages to happen, it had better wait until this is invoked;
    // otherwise, some of the early ones may be lost. This function will be called
    // once, immediately if the socket is already open, otherwise, as soon as
    // it is in the "OPEN" state where messages can be received (and sent).
    onReadyToReceive?: () => void;
    testProgressHtml?: string;
    onGotErrorMessage?: () => void;
    progressBoxId?: string;
    notifyProgressChange?: (progress: string) => void;
}

interface IProgressState {
    progress: string;
}

// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
export const ProgressBox: React.FunctionComponent<IProgressBoxProps &
    IProgressState> = props => {
    //progressDiv: HTMLElement | null;
    // public readonly state: IProgressState = {
    //     progress: this.props.testProgressHtml || ""
    // };
    const [progressHtml, setProgressHtml] = React.useState<string>("");
    const progressDiv = React.useRef<HTMLDivElement>(null);

    function write(htmlToAdd: string) {
        const newProgress = this.state.progress + htmlToAdd;
        this.setState({
            progress: newProgress
        });
        if (this.props.notifyProgressChange) {
            this.props.notifyProgressChange(newProgress);
        }
    }
    function writeLine(htmlToAdd: string) {
        this.write(htmlToAdd + "<br/>");
    }

    function tryScrollToBottom() {
        // Must be done AFTER painting once, so we
        // get a real current scroll height.
        //const progressDiv = this.progressDiv;

        // in my testing in FF, this worked the first time
        if (progressDiv.current)
            progressDiv.current.scrollTop = progressDiv.current?.scrollHeight;
        // but there apparently have been times when the div wasn't around
        // yet, so we do this:
        else {
            window.requestAnimationFrame(() => this.tryScrollToBottom());
            // note, I have tested what happens if the element is *never* found (due to some future bug).
            // Tests show that everything stays responsive.
        }
    }

    WebSocketManager.addListener(props.clientContext, e => {
        if (e.id === "message") {
            if (e.message!.indexOf("error") > -1) {
                if (props.onGotErrorMessage) {
                    props.onGotErrorMessage();
                }
            }
            if (e.cssStyleRule) {
                writeLine(
                    `<span style='${e.cssStyleRule}'>${e.message}</span>`
                );
            } else if (e.kind) {
                switch (e.kind) {
                    default:
                        writeLine(e.message || "");
                        break;
                    case "Error":
                        writeLine(
                            `<span style='color:red'>${e.message}</span>`
                        );
                        break;
                    case "Warning":
                        writeLine(
                            `<span style='color:#da7903'>${e.message}</span>`
                        );
                        break;
                }
            } else {
                writeLine(e.message || "");
            }
            tryScrollToBottom();
        }
    });

    // Tell the back end that we are ready to start receiving progress notices
    React.useEffect(() => {
        if (props.onReadyToReceive) {
            WebSocketManager.notifyReady(
                props.clientContext,
                props.onReadyToReceive
            );
        }
    }, [props.clientContext]); // just call this once, after first render

    return (
        <div
            className="progress-box"
            id={props.progressBoxId || ""}
            dangerouslySetInnerHTML={{ __html: progressHtml }}
            ref={progressDiv}
        />
    );
};
