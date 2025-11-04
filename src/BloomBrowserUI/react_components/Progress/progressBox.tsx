import { css } from "@emotion/react";

import * as React from "react";
import WebSocketManager, {
    IBloomWebSocketProgressEvent,
} from "../../utils/WebSocketManager";
import {
    kBloomGold,
    kErrorColor,
    kLogBackgroundColor,
    kDialogPadding,
} from "../../bloomMaterialUITheme";
import { StringWithOptionalLink } from "../stringWithOptionalLink";

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

    // For external control of message state (e.g., where undesirable mounting/unmounting is possible)
    // set both of these, typically by having a state like
    // const [messages, setMessages] = React.useState<Array<JSX.Element>>([]);
    // and passing both those values on.
    // Typically, you would try leaving these undefined. If ProgressBox is only mounted once
    // for a given sequence of messages, it can manage its own state. This works, for example,
    // when ProgressDialog is the root component of Browser.
    // If you find that messages are mysteriously disappearing (or never seen), investigate the
    // possibility that this component or a parent is being unmounted, and prevent it if possible.
    // For example, when it is part of a BloomDialog (e.g., when ProgressDialog is just one component
    // in a larger layout), unmounting seems to happen every time the BloomDialog is rendered.
    // In these cases, unless you can prevent the unmounting,
    // the only solution is to raise the list-of-messages state above the level where the
    // mounting/unmounting is happening, as ProgressDialog does.
    messages?: Array<JSX.Element>;
    setMessages?: React.Dispatch<React.SetStateAction<JSX.Element[]>>;
}

export type ProgressBoxHandle = {
    clear: () => void;
};

let indexForMessageKey = 0;

// Note that this component does not do localization; we expect the progress messages
// to already be localized when they are sent over the websocket.
// This has to be an uncontrolled component (has its own state) so that it can directly
// wire to the backend, but we need the parent to "control" it in the sense of needing to clear it.
// The ref lets us give a handle to parent.
export const ProgressBox = React.forwardRef<
    ProgressBoxHandle,
    IProgressBoxProps
>((props, ref) => {
    const [localMessages, setLocalMessages] = React.useState<
        Array<JSX.Element>
    >([]);

    const {
        webSocketContext,
        onReadyToReceive,
        onGotErrorMessage,
        preloadedProgressEvents,
        messages: propsMessages,
        setMessages: propsDummy,
        ...divProps
    } = props;

    let messages = localMessages;
    let setMessages = setLocalMessages;
    if (props.messages && props.setMessages) {
        messages = props.messages;
        setMessages = props.setMessages;
    } else if (props.messages || props.setMessages) {
        console.error(
            "messages and setMessages must both be provided for controlled use of ProgressBox",
        );
    }

    // used for scrolling to bottom
    const bottomRef = React.useRef<HTMLDivElement>(null);

    React.useEffect(() => {
        if (props.preloadedProgressEvents) {
            // Don't overwrite existing messages.  See https://issues.bloomlibrary.org/youtrack/issue/BL-11696.
            props.preloadedProgressEvents.forEach((e) => processEvent(e));
        }
    }, [props.preloadedProgressEvents]);

    React.useEffect(() => {
        const listener = (e) => processEvent(e);
        if (props.webSocketContext) {
            WebSocketManager.addListener<IBloomWebSocketProgressEvent>(
                props.webSocketContext,
                listener,
            );
        }
        if (props.onReadyToReceive && props.webSocketContext) {
            WebSocketManager.notifyReady(
                props.webSocketContext,
                props.onReadyToReceive,
            );
        }
        // clean up when we are unmounted or this useEffect runs again (i.e. if the props.webSocketContext were to change)
        return () => {
            if (props.webSocketContext)
                WebSocketManager.removeListener(
                    props.webSocketContext,
                    listener,
                );
        };
    }, [props.webSocketContext]);

    React.useEffect(() => {
        if (bottomRef && bottomRef.current)
            bottomRef!.current!.scrollIntoView({
                behavior: "smooth",
                block: "start",
            });
    }, [messages]);

    // This (along with the forwardRef wrapper above) allows the parent to call clear() on this component.
    React.useImperativeHandle(ref, () => ({
        clear() {
            setMessages([]);
        },
    }));

    function writeLine(message: string, color: string, style?: string) {
        const line = (
            <p
                key={indexForMessageKey++}
                css={css`
                    color: ${color};
                    ${style ? style : ""}
                `}
            >
                <StringWithOptionalLink message={message} />
            </p>
        );
        setMessages((old) => [...old, line]);
    }

    function processEvent(e: IBloomWebSocketProgressEvent) {
        const msg = "" + e.message;

        if (e.id === "message") {
            if (e.progressKind) {
                switch (e.progressKind) {
                    default:
                        writeLine(msg, "black");
                        break;
                    case "Heading":
                        writeLine(msg, "black", "font-weight:bold");
                        break;
                    case "Error":
                    case "Fatal":
                        writeLine(msg, kErrorColor);
                        props.onGotErrorMessage?.();
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
                overflow-y: auto;
                background-color: ${kLogBackgroundColor};
                padding: ${kDialogPadding};
                height: 100%;
                box-sizing: border-box;

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
            {...divProps}
        >
            {messages}
            <div ref={bottomRef} />
        </div>
    );
});

ProgressBox.displayName = "ProgressBox";
