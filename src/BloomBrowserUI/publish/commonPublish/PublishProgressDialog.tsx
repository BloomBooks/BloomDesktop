import * as React from "react";
import { useState } from "react";
import {
    ProgressDialogInner,
    ProgressState
} from "./PublishProgressDialogInner";
import { postData } from "../../utils/bloomApi";
import WebSocketManager, {
    IBloomWebSocketProgressEvent,
    useSubscribeToWebSocketForEvent
} from "../../utils/WebSocketManager";

export const PublishProgressDialog: React.FunctionComponent<{
    heading: string; // up to client to localize
    webSocketClientContext: string;
    startApiEndpoint: string | null; // Null means no need to start with any API endpoint.
    onUserStopped?: () => void;
    closePending: boolean;
    setClosePending: (boolean) => void;
    progressState: ProgressState;
    setProgressState: (ProgressState) => void;
}> = props => {
    const [instructionMessage, setInstructionMessage] = useState<
        string | undefined
    >(undefined);
    const [accumulatedMessages, setAccumulatedMessages] = useState("");

    const [errorEncountered, setErrorEncountered] = useState(false);

    //Note, originally this was just a function, closeIfNoError().
    // However that would be called before the errorEncountered had been updated.
    // So now we make it happen by calling setClosePending() and then in the next
    // update we notice that and see about closing.
    React.useEffect(() => {
        if (props.closePending) {
            if (errorEncountered) {
                props.setProgressState(() =>
                    errorEncountered ? ProgressState.Done : ProgressState.Closed
                );
                // Although we may be in state 'Done' and thus not actually closed yet,
                // we're no longer in the state that closePending is meant to handle,
                // where we've finished but don't yet have enough information to know
                // whether to close automatically or wait to let the user see any errors.
                // In case the dialog is used again (e.g., to update a preview with
                // different parameters), we need to turn this off so the useEffect will notice
                // the next time it is turned on.
                props.setClosePending(false);
            } else {
                // set up for next time
                setAccumulatedMessages("");
                setInstructionMessage(undefined);
                setErrorEncountered(false);
                // close it
                props.setProgressState(ProgressState.Closed);
                props.setClosePending(false);
            }
        }
    }, [props.closePending]);

    React.useEffect(() => {
        // we need to be ready to listen to progress messages from the server,
        // before we kick anything off on the server.
        WebSocketManager.notifyReady(props.webSocketClientContext, () => {
            if (!props.startApiEndpoint) {
                // Note: If no startApiEndpoint is fired, we don't setClosePending immediately.
                // It doesn't seem to make much sense to do so currently.
                return;
            }

            // Handle an optional API request that fires immediately upon mounting,
            // and handle changing the state of the dialog when the postData's promise is satisfied.
            postData(
                props.startApiEndpoint,
                {},
                () => props.setClosePending(true),
                r => {
                    // Error handler if server encountered a really bad error and wasn't able to return a message to us.
                    setErrorEncountered(true);
                    setAccumulatedMessages(
                        oldMessages =>
                            oldMessages +
                            `<span class='Error'>Failed to prepare the book for publish. Request '${props.startApiEndpoint}' returned: ${r}.</span>`
                    );
                    props.setProgressState(ProgressState.Done);
                }
            );
        });
    }, []);

    useSubscribeToWebSocketForEvent(
        props.webSocketClientContext,
        "message",
        e => {
            const progressEvent = e as IBloomWebSocketProgressEvent;

            // // the epub maker
            // if(progressState === ProgressState.Closed){
            //     setProgressState(ProgressState.Working);
            // }
            const html = `<span class='${progressEvent.progressKind}'>${e.message}</span><br/>`;
            if (e.id == "message") {
                switch (progressEvent.progressKind) {
                    case "Error":
                    case "Warning":
                        setErrorEncountered(true);
                    // deliberately fall through
                    case "Progress":

                    // eslint-disable-next-line no-fallthrough
                    case "Note":
                        setAccumulatedMessages(
                            oldMessages => oldMessages + html
                        );
                        break;
                    case "Instruction":
                        setInstructionMessage(e.message);
                }
            }
        }
    );

    return (
        <ProgressDialogInner
            heading={props.heading}
            instruction={instructionMessage}
            messages={accumulatedMessages}
            progressState={props.progressState}
            onUserStopped={() => props.onUserStopped && props.onUserStopped()}
            onUserClosed={() => {
                setAccumulatedMessages("");
                setErrorEncountered(false);
                props.setProgressState(ProgressState.Closed);
            }}
            errorEncountered={errorEncountered}
        />
    );
};
