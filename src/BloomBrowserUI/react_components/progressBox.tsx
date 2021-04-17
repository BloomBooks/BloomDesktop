/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import { Button } from "@material-ui/core";
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
export class ProgressBox extends React.Component<
    IProgressBoxProps,
    IProgressState
> {
    private progressDiv: HTMLElement | null;
    public readonly state: IProgressState = {
        progress: this.props.testProgressHtml || ""
    };

    constructor(props: IProgressBoxProps) {
        super(props);
        //alert("constructing progress box for " + this.props.clientContext);
        //get progress messages from c#
        WebSocketManager.addListener(props.clientContext, e => {
            console.log(this.state);
            if (e.id === "message") {
                if (e.message!.indexOf("error") > -1) {
                    if (this.props.onGotErrorMessage) {
                        this.props.onGotErrorMessage();
                    }
                }
                if (e.cssStyleRule) {
                    this.writeLine(
                        `<span style='${e.cssStyleRule}'>${e.message}</span>`
                    );
                } else if (e.kind) {
                    switch (e.kind) {
                        default:
                            this.writeLine(e.message || "");
                            break;
                        case "Error":
                            this.writeLine(
                                `<span style='color:red'>${e.message}</span>`
                            );
                            break;
                        case "Warning":
                            this.writeLine(
                                `<span style='color:#da7903'>${e.message}</span>`
                            );
                            break;
                    }
                } else {
                    this.writeLine(e.message || "");
                }
                this.tryScrollToBottom();
            }
        });
    }

    public componentDidMount() {
        //alert("mounting progress box for " + this.props.clientContext);
        if (this.props.onReadyToReceive) {
            WebSocketManager.notifyReady(
                this.props.clientContext,
                this.props.onReadyToReceive
            );
        }
    }
    public componentDidUnmount() {
        //WebSocketManager.removeListener(props.clientContext);
    }

    public write(htmlToAdd: string) {
        const newProgress = this.state.progress + htmlToAdd;
        console.log(newProgress);
        this.setState({
            progress: newProgress
        });
        if (this.props.notifyProgressChange) {
            this.props.notifyProgressChange(newProgress);
        }
    }
    public writeLine(htmlToAdd: string) {
        this.write(htmlToAdd + "<br/>");
    }

    private tryScrollToBottom() {
        // Must be done AFTER painting once, so we
        // get a real current scroll height.
        const progressDiv = this.progressDiv;
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
    private copyToClipboard() {
        // BloomApi.postJson("common/clipboardText", {
        //     text: progress.current
        // });

        // const copyText: HTMLInputElement = document.getElementById(
        //     this.props.progressBoxId || "progress"
        // ) as HTMLInputElement;

        const range = document.createRange();
        range.selectNode(
            document.getElementById(this.props.progressBoxId || "progress")!
        );
        window.getSelection()!.removeAllRanges();
        window.getSelection()!.addRange(range);
        document.execCommand("copy");
        window.getSelection()!.removeAllRanges();
    }

    public render() {
        return (
            <div>
                <div
                    className="progress-box"
                    id={this.props.progressBoxId || "progress"}
                    dangerouslySetInnerHTML={{
                        __html: this.state.progress
                    }}
                    ref={div => (this.progressDiv = div)}
                    css={css`
                        user-select: all;
                    `}
                />
                <Button
                    //id="copy-button"
                    onClick={() => this.copyToClipboard()}
                    title="Copy to Clipboard"
                >
                    Copy to Clipboard
                </Button>
            </div>
        );
    }
}
