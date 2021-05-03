/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

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
    onGotErrorMessage?: () => void;

    preloadedProgressEvents?: Array<IBloomWebSocketProgressEvent>;
}

let indexForMessageKey = 0;

// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
export const ProgressBox: React.FunctionComponent<IProgressBoxProps> = props => {
    const [messages, setMessages] = React.useState<Array<JSX.Element>>([]);

    // used for scrolling to bottom
    const bottomRef = React.useRef<HTMLDivElement>(null);

    React.useEffect(() => {
        if (props.preloadedProgressEvents) {
            setMessages([]);
            props.preloadedProgressEvents.forEach(e => processEvent(e));
        }
    }, [props.preloadedProgressEvents]);

    React.useEffect(() => {
        const listener = e => processEvent(e);
        if (props.webSocketContext) {
            WebSocketManager.addListener<IBloomWebSocketProgressEvent>(
                props.webSocketContext,
                listener
            );
        }
        if (props.onReadyToReceive && props.webSocketContext) {
            WebSocketManager.notifyReady(
                props.webSocketContext,
                props.onReadyToReceive
            );
        }
        // clean up when we are unmounted or this useEffect runs again (i.e. if the props.webSocketContext were to change)
        return () => {
            if (props.webSocketContext)
                WebSocketManager.removeListener(
                    props.webSocketContext,
                    listener
                );
        };
    }, [props.webSocketContext]);

    React.useEffect(() => {
        if (bottomRef && bottomRef.current)
            bottomRef!.current!.scrollIntoView({
                behavior: "smooth",
                block: "start"
            });
    }, [messages]);

    function writeLine(message: string, color: string, style?: string) {
        const line = (
            <p
                key={indexForMessageKey++}
                css={css`
                    color: ${color};
                `}
            >
                {message}
            </p>
        );
        setMessages(old => [...old, line]);
    }

    function processEvent(e: IBloomWebSocketProgressEvent) {
        const msg = "" + e.message;

        if (e.id === "message") {
            if (e.message!.indexOf("error") > -1) {
                if (props.onGotErrorMessage) {
                    props.onGotErrorMessage();
                }
            }
            if (e.progressKind) {
                switch (e.progressKind) {
                    default:
                        writeLine(msg, "black");
                        break;
                    case "Error":
                        writeLine(msg, kErrorColor);
                        break;
                    case "Warning":
                        writeLine(msg, kBloomGold);
                        break;
                }
            } else {
                writeLine(msg, "black");
            }
        }
    }

    return (
        <div
            css={css`
                overflow-y: scroll;
                background-color: ${kLogBackgroundColor};
                padding: ${kDialogPadding};
                height: 100%;

                &,
                * {
                    user-select: all;
                }
                p {
                    margin-block-start: 0px;
                    margin-block-end: 8px;
                    font-family: "consolas", "monospace";
                }
            `} // accept styling that the parent might have put on the <ProgressBox> element. See https://emotion.sh/docs/css-prop
            {...props}
        >
            {messages}
            <div ref={bottomRef} />
        </div>
    );
};
