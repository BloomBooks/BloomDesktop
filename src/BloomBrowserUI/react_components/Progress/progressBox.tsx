/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
// import { Button } from "@material-ui/core";
import * as React from "react";
import WebSocketManager, {
    IBloomWebSocketProgressEvent
} from "../../utils/WebSocketManager";
import {
    kBloomGold,
    kErrorColor,
    kLogBackgroundColor,
    kDialogPadding
} from "../../bloomMaterialUITheme";

export interface IProgressBoxProps {
    webSocketContext?: string;
    // If the client is going to start doing something right away that will
    // cause progress messages to happen, it had better wait until this is invoked;
    // otherwise, some of the early ones may be lost. This function will be called
    // once, immediately if the socket is already open, otherwise, as soon as
    // it is in the "OPEN" state where messages can be received (and sent).
    onReadyToReceive?: () => void;
    preloadedProgressEvents?: Array<IBloomWebSocketProgressEvent>;
    onGotErrorMessage?: () => void;
    progressBoxId?: string;
    notifyProgressChange?: (progress: string) => void;
}

interface IProgressState {
    progress: string;
}

// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
export class ProgressBox extends React.Component<IProgressBoxProps> {
    private progressDiv: HTMLElement | null;
    public readonly state: IProgressState = {
        progress: ""
    };

    private internalMessageHtml: string;

    constructor(props: IProgressBoxProps) {
        super(props);
        //alert("constructing progress box for " + this.props.clientContext);
        //get progress messages from c#
        if (props.webSocketContext) {
            WebSocketManager.addListener<IBloomWebSocketProgressEvent>(
                props.webSocketContext,
                e => this.processEvent(e)
            );
        }
        this.internalMessageHtml = "";
    }

    public componentDidMount() {
        if (this.props.onReadyToReceive && this.props.webSocketContext) {
            WebSocketManager.notifyReady(
                this.props.webSocketContext,
                this.props.onReadyToReceive
            );
        }
    }
    public componentDidUpdate(prevProps: Readonly<IProgressBoxProps>) {
        if (
            prevProps.preloadedProgressEvents !==
            this.props.preloadedProgressEvents
        )
            this.props.preloadedProgressEvents?.forEach(e =>
                this.processEvent(e)
            );
    }
    public componentWillUnmount() {
        //WebSocketManager.removeListener(props.clientContext);
    }

    private processEvent(e: IBloomWebSocketProgressEvent) {
        const msg = "" + e.message;
        if (e.id === "message") {
            if (e.message!.indexOf("error") > -1) {
                if (this.props.onGotErrorMessage) {
                    this.props.onGotErrorMessage();
                }
            }
            if (e.progressKind) {
                switch (e.progressKind) {
                    default:
                        this.writeLine(msg, "black");
                        break;
                    case "Error":
                        this.writeLine(msg, kErrorColor);
                        break;
                    case "Warning":
                        this.writeLine(msg, kBloomGold);
                        break;
                }
            } else {
                this.writeLine(msg, "black");
            }
        }
    }
    private writeLine(htmlToAdd: string, color: string, style?: string) {
        this.internalMessageHtml += `<p  style='color:${color}; '${"" +
            style}'>${htmlToAdd}<p>`;
        this.setState({
            progress: this.internalMessageHtml
        });

        // review: I (JH) *think* this was put here as a way of enlisting our client in helping to keep our state
        if (this.props.notifyProgressChange) {
            this.props.notifyProgressChange(this.internalMessageHtml);
        }
        this.tryScrollToBottom();
    }

    // having this public this is a bit awkward, it is un-react-y, but it's needed for the DaisyChecks client
    public writeError(message: string) {
        this.writeLine(message, kErrorColor);
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
            // Tests show that everything stays responsive.
        }
    }
    /* At the moment I (JH) am leaning away from an explicit copy button. Instead
    clients of this control should have "REPORT" button if we want users to be
    telling us about problems, PLUS allow for selection & copy of the contents
    for the rare case where something other than reporting is needed.

    private copyToClipboard() {
        const range = document.createRange();
        range.selectNode(
            document.getElementById(this.props.progressBoxId || "progress")!
        );
        window.getSelection()!.removeAllRanges();
        window.getSelection()!.addRange(range);
        document.execCommand("copy");
        window.getSelection()!.removeAllRanges();
    }*/

    public render() {
        return (
            <div
                css={css`
                    user-select: all;
                    overflow-y: scroll;
                    background-color: ${kLogBackgroundColor};
                    padding: ${kDialogPadding};
                    height: 100%;
                    /* width: calc(
                            100% - @copy-button-width - @copy-button-left-margin -
                                @copy-button-right-margin - @main-margin
                        );
                        margin-right: @main-margin; */
                    p {
                        margin-block-start: 0px;
                        margin-block-end: 8px;
                        font-family: "consolas", "monospace";
                    }
                `}
                id={this.props.progressBoxId || "progress"}
                dangerouslySetInnerHTML={{
                    __html: this.state.progress
                }}
                ref={div => (this.progressDiv = div)}
            />
        );
    }
}

/* <Button
                    //id="copy-button"
                    onClick={() => this.copyToClipboard()}
                    title="Copy to Clipboard"
                >
                    Copy to Clipboard
                </Button> */
