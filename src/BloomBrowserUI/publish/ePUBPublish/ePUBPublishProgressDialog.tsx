import * as React from "react";
import { useState } from "react";
import { ProgressDialog, ProgressState } from "../commonPublish/ProgressDialog";
import { BloomApi } from "../../utils/bloomApi";
import WebSocketManager, {
    useWebSocketListenerForOneEvent
} from "../../utils/WebSocketManager";

export const EPUBPublishProgressDialog = () => {
    const [closePending, setClosePending] = useState(false);
    const [accumulatedMessages, setAccumulatedMessages] = useState("");
    const [progressState, setProgressState] = useState(ProgressState.Working);
    //TODO Localize
    const [heading, setHeading] = useState("Creating ePUB");
    const [errorEncountered, setErrorEncountered] = useState(false);

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
            }
        }
    }, [closePending]);

    // useWebSocketListenerForOneEvent(
    //     "publish-epub",
    //     "publish/android/state",
    //     e => {
    //         switch (e.message) {
    //             case "stopped":
    //                 setClosePending(true);
    //                 break;
    //             case "UsbStarted":
    //                 //TODO Localize
    //                 setHeading("Sending via USB Cable");
    //                 setProgressState(ProgressState.Serving);
    //                 break;
    //             case "ServingOnWifi":
    //                 //TODO Localize
    //                 setHeading("Sharing");
    //                 setProgressState(ProgressState.Serving);
    //                 break;
    //             default:
    //                 throw new Error(
    //                     "Method Chooser does not understand the state: " +
    //                         e.message
    //                 );
    //         }
    //     }
    // );
    useWebSocketListenerForOneEvent("publish-epub", "message", e => {
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
                    setAccumulatedMessages(oldMessages => oldMessages + html);
            }
        }
    });

    React.useEffect(() => {
        // we need to be ready to listen to progress messages from the server,
        // before we kick anything off on the server.
        WebSocketManager.notifyReady("publish-epub", () => {
            // We agonized over the fact that "updatePreview" doesn't have anything to do with displaying progress.
            // It just so happens that 1) this is the first thing we do in this screen and 2) after it, we
            // need to do something to the state of the dialog.
            // But the alternative gets complicated too... the weirdness here is that we need to
            // do something (change the state of the dialog) when the postData's promise is satisfied.
            // (That is, when the preview construction is complete).
            BloomApi.postData("publish/epub/updatePreview", {}, () =>
                setClosePending(true)
            );
        });
    }, []);

    return (
        <ProgressDialog
            heading={heading}
            messages={accumulatedMessages}
            progressState={progressState}
            onUserStopped={() => {}}
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
