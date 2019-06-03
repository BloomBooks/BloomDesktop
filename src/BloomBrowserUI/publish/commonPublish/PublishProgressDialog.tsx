import * as React from "react";
import { useState } from "react";
import { ProgressDialog, ProgressState } from "./ProgressDialog";
import { BloomApi } from "../../utils/bloomApi";
import WebSocketManager, {
    useWebSocketListenerForOneEvent
} from "../../utils/WebSocketManager";

export const PublishProgressDialog: React.FunctionComponent<{
    heading: string; // up to client to localize
    webSocketClientContext: string;
    startApiEndpoint: string;
    onUserStopped?: () => void;
    wireUpStateListeners?: (
        setClosePending: (boolean) => void,
        setProgressState: (ProgressState) => void
    ) => void;
}> = props => {
    const [closePending, setClosePending] = useState(false);
    const [accumulatedMessages, setAccumulatedMessages] = useState("");
    const [progressState, setProgressState] = useState(ProgressState.Working);
    const [errorEncountered, setErrorEncountered] = useState(false);

    // Allow our parents to control our state
    if (props.wireUpStateListeners) {
        // Note: the things we're calling here have their own hooks, so we don't need to (and we're not allowed to)
        // put this inside a useEffect().
        props.wireUpStateListeners(setClosePending, setProgressState);
    }

    //Note, originally this was just a function, closeIfNoError().
    // However that would be called before the errorEncountered had been updated.
    // So now we make it happen by calling setClosePending() and then in the next
    // update we notice that and see about closing.
    React.useEffect(() => {
        if (closePending) {
            if (errorEncountered) {
                setProgressState(() =>
                    errorEncountered ? ProgressState.Done : ProgressState.Closed
                );
            } else {
                // set up for next time
                setAccumulatedMessages("");
                setErrorEncountered(false);
                // close it
                setProgressState(ProgressState.Closed);
                setClosePending(false);
            }
        }
    }, [closePending]);

    React.useEffect(() => {
        // we need to be ready to listen to progress messages from the server,
        // before we kick anything off on the server.
        WebSocketManager.notifyReady(props.webSocketClientContext, () => {
            // We agonized over the fact that "updatePreview" doesn't have anything to do with displaying progress.
            // It just so happens that 1) this is the first thing we do in this screen and 2) after it, we
            // need to do something to the state of the dialog.
            // But the alternative gets complicated too... the weirdness here is that we need to
            // do something (change the state of the dialog) when the postData's promise is satisfied.
            // (That is, when the preview construction is complete).
            BloomApi.postData(props.startApiEndpoint, {}, () =>
                setClosePending(true)
            );
        });
    }, []);

    useWebSocketListenerForOneEvent(
        props.webSocketClientContext,
        "message",
        e => {
            // // the epub maker
            // if(progressState === ProgressState.Closed){
            //     setProgressState(ProgressState.Working);
            // }
            const html = `<span class='${e.kind}'>${e.message}</span><br/>`;
            if (e.id == "message") {
                switch (e.kind) {
                    case "Error":
                    case "Warning":
                        setErrorEncountered(true);
                    // deliberately fall through
                    case "Progress":
                    case "Instruction":
                    case "Note":
                        setAccumulatedMessages(
                            oldMessages => oldMessages + html
                        );
                }
            }
        }
    );

    return (
        <ProgressDialog
            heading={props.heading}
            messages={accumulatedMessages}
            progressState={progressState}
            onUserStopped={() => props.onUserStopped && props.onUserStopped()}
            onUserCanceled={() => {}}
            onUserClosed={() => {
                setAccumulatedMessages("");
                setErrorEncountered(false);
                setProgressState(ProgressState.Closed);
            }}
            errorEncountered={errorEncountered}
        />
    );
};
