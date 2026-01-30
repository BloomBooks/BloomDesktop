import * as React from "react";
import { useState } from "react";
import {
    ProgressDialogInner,
    ProgressState,
} from "./PublishProgressDialogInner";
import { postData } from "../../utils/bloomApi";
import WebSocketManager, {
    IBloomWebSocketProgressEvent,
    useSubscribeToWebSocketForEvent,
} from "../../utils/WebSocketManager";

export const PublishProgressDialog: React.FunctionComponent<{
    heading: string; // up to client to localize
    webSocketClientContext: string;
    // The API call to start whatever task we are displaying the progress of
    // When this component is ready to receive progress messages, it will send A POST request to this endpoint
    // It should just be the relative API path, not the full URL. e.g. just use "publish/[...]/[...]"
    apiForStartingTask: string;
    // Callback executed when {apiForStartingTask} completes successfully.
    // For example, if you want it to automatically close upon completion of the start API, you can make that happen here.
    onTaskComplete: (() => void) | undefined;
    onUserStopped?: () => void;
    closePending: boolean;
    setClosePending: React.Dispatch<React.SetStateAction<boolean>>; // the type of the setter from React.useState<boolean>
    progressState: ProgressState;
    setProgressState: React.Dispatch<React.SetStateAction<ProgressState>>; // the type of the setter from React.useState<ProgressState>
    generation?: number; // bump this to force restarting.
}> = (props) => {
    const [instructionMessage, setInstructionMessage] = useState<
        string | undefined
    >(undefined);
    const [accumulatedMessages, setAccumulatedMessages] = useState("");

    const [errorEncountered, setErrorEncountered] = useState(false);
    const [interestingMessageEncountered, setInterestingMessageEncountered] =
        useState(false);

    const {
        setProgressState: setProgressStateProp,
        setClosePending: setClosePendingProp,
    } = props;
    const closeAndResetDialog = React.useCallback(() => {
        // close it
        setProgressStateProp(ProgressState.Closed);

        // set up for next time
        setClosePendingProp(false);
        setAccumulatedMessages("");
        setInstructionMessage(undefined);
        setErrorEncountered(false);
        setInterestingMessageEncountered(false);
    }, [setProgressStateProp, setClosePendingProp]);

    //Note, originally this was just a function, closeIfNoError().
    // However that would be called before the errorEncountered had been updated.
    // So now we make it happen by calling setClosePending() and then in the next
    // update we notice that and see about closing.
    React.useEffect(() => {
        if (props.closePending) {
            if (errorEncountered || interestingMessageEncountered) {
                setProgressStateProp(() =>
                    errorEncountered || interestingMessageEncountered
                        ? ProgressState.Done
                        : ProgressState.Closed,
                );
                // Although we may be in state 'Done' and thus not actually closed yet,
                // we're no longer in the state that closePending is meant to handle,
                // where we've finished but don't yet have enough information to know
                // whether to close automatically or wait to let the user see any errors.
                // In case the dialog is used again (e.g., to update a preview with
                // different parameters), we need to turn this off so the useEffect will notice
                // the next time it is turned on.
                setClosePendingProp(false);
            } else {
                closeAndResetDialog();
            }
        }
    }, [
        props.closePending,
        setProgressStateProp,
        setClosePendingProp,
        closeAndResetDialog,
        interestingMessageEncountered,
    ]);

    React.useEffect(() => {
        props.setProgressState(ProgressState.Working);
        // we need to be ready to listen to progress messages from the server,
        // before we kick anything off on the server.
        WebSocketManager.notifyReady(props.webSocketClientContext, () => {
            // Handle an optional API request that fires immediately upon mounting,
            // and handle changing the state of the dialog when the postData's promise is satisfied.
            postData(
                props.apiForStartingTask,
                {},
                props.onTaskComplete,
                (r) => {
                    // Error handler if server encountered a really bad error and wasn't able to return a message to us.
                    setErrorEncountered(true);
                    setAccumulatedMessages(
                        (oldMessages) =>
                            oldMessages +
                            `<span class='Error'>Failed to prepare the book for publish. Request '${props.apiForStartingTask}' returned: ${r}.</span>`,
                    );
                    props.setProgressState(ProgressState.Done);
                },
            );
        });
    }, [props.apiForStartingTask, props.generation]); // Every time the start API endpoint changes, we basically restart the component

    useSubscribeToWebSocketForEvent(
        props.webSocketClientContext,
        "message",
        (e) => {
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
                        setInterestingMessageEncountered(true);
                    // deliberately fall through
                    case "Progress":

                    // eslint-disable-next-line no-fallthrough
                    case "Note":
                        if (progressEvent.progressKind === "Note") {
                            setInterestingMessageEncountered(true);
                        }
                        setAccumulatedMessages(
                            (oldMessages) => oldMessages + html,
                        );
                        break;
                    case "Instruction":
                        setInstructionMessage(e.message);
                }
            }
        },
    );

    return (
        <ProgressDialogInner
            heading={props.heading}
            instruction={instructionMessage}
            messages={accumulatedMessages}
            progressState={props.progressState}
            onUserStopped={() => props.onUserStopped && props.onUserStopped()}
            onUserClosed={closeAndResetDialog}
            errorEncountered={errorEncountered}
        />
    );
};
