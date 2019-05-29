import * as React from "react";
import { useState, useContext } from "react";
import { ProgressDialog, ProgressState } from "../commonPublish/ProgressDialog";
import { BloomApi } from "../../utils/bloomApi";
import WebSocketManager, {
    useWebSocketListenerForOneEvent
} from "../../utils/WebSocketManager";

let globalError = false;

export const ReaderPublishProgressDialog = () => {
    const [accumulatedMessages, setAccumulatedMessages] = useState("");
    const [progressState, setProgressState] = useState(ProgressState.Working);
    //TODO Localize
    const [heading, setHeading] = useState("Creating Digital Book");
    const [errorEncountered, setErrorEncountered] = useState(false);

    const closeIfNoError = () => {
        //review: why is errorEncountered always false here?
        // we think there hasn't been a react cycle to actually
        // let the setErrorEncountered change the value of errorEncountered yet.
        // I've hacked around it with globalError.
        if (errorEncountered || globalError) {
            //TODO: replace with a function that then accesses errorEncountered
            //setProgressState(ProgressState.Done);
            setProgressState(() =>
                errorEncountered ? ProgressState.Done : ProgressState.Closed
            );
        } else {
            // set up for next time
            setAccumulatedMessages("");
            setErrorEncountered(false);
            globalError = false;
            // close it
            setProgressState(ProgressState.Closed);
        }
    };
    useWebSocketListenerForOneEvent(
        "publish-android",
        "publish/android/state",
        e => {
            switch (e.message) {
                case "stopped":
                    closeIfNoError();
                    break;
                case "UsbStarted":
                    //TODO Localize
                    setHeading("Sending via USB Cable");
                    setProgressState(ProgressState.Serving);
                    break;
                case "ServingOnWifi":
                    //TODO Localize
                    setHeading("Sharing");
                    setProgressState(ProgressState.Serving);
                    break;
                default:
                    throw new Error(
                        "Method Chooser does not understand the state: " +
                            e.message
                    );
            }
        }
    );
    useWebSocketListenerForOneEvent("publish-android", "message", e => {
        const html = `<span class='${e.kind}'>${e.message}</span><br/>`;
        if (e.id == "message") {
            switch (e.kind) {
                case "Error":
                case "Warning":
                    setErrorEncountered(true);
                    globalError = true;
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
        WebSocketManager.notifyReady("publish-android", () => {
            // We agonized over the fact that "updatePreview" doesn't have anything to do with displaying progress.
            // It just so happens that 1) this is the first thing we do in this screen and 2) after it, we
            // need to do something to the state of the dialog.
            // But the alternative gets complicated too... the weirdness here is that we need to
            // do something (change the state of the dialog) when the postData's promise is satisfied.
            // (That is, when the preview construction is complete).
            BloomApi.postData("publish/android/updatePreview", {}, () =>
                closeIfNoError()
            );
        });
    }, []);

    return (
        <ProgressDialog
            heading={heading}
            progressMessages={accumulatedMessages}
            progressState={progressState}
            onUserStopped={() => {
                BloomApi.postData("publish/android/usb/stop", {});
                BloomApi.postData("publish/android/wifi/stop", {});
            }}
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
