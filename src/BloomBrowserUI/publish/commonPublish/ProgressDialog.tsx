import * as React from "react";
import { useLayoutEffect } from "react";
import {
    Button,
    CircularProgress,
    Dialog,
    DialogActions,
    DialogContent,
    DialogTitle,
    Typography
} from "@material-ui/core";
import { BloomApi } from "../../utils/bloomApi";
import { useTheme } from "@material-ui/styles";
import "./ProgressDialog.less";
import BloomButton from "../../react_components/bloomButton";

export enum ProgressState {
    Closed,
    Working, // doing something that will lead to a "Done"
    Done,
    Serving // doing something indefinitely, which user can stop
}

export const ProgressDialog: React.FunctionComponent<{
    heading?: string;
    instruction?: string;
    messages: string;
    progressState: ProgressState;
    errorEncountered?: boolean; // do something visual to indicate there was a problem
    onUserClosed: () => void;
    onUserStopped: () => void;
    onUserCanceled: () => void;
}> = props => {
    const messagesDivRef = React.useRef<HTMLDivElement>(null);
    const messageEndRef = React.useRef<HTMLDivElement>(null);
    const theme = useTheme();
    const onCopy = () => {
        // document.execCommand("copy") does not work in Bloom's geckofx.
        BloomApi.postDataWithConfig(
            "publish/android/textToClipboard",
            messagesDivRef.current!.innerText,
            { headers: { "Content-Type": "text/plain" } }
        );
    };

    const somethingStillGoing =
        props.progressState == ProgressState.Working ||
        props.progressState == ProgressState.Serving;

    // every time there are new messages, scroll to the bottom by scrolling into view
    // an empty element that is always at the end.
    useLayoutEffect(() => {
        window.setTimeout(() => {
            if (messageEndRef.current) {
                messageEndRef.current!.scrollIntoView();
            }
        }, 100);
    }, [props.messages]); // do this every time the message text changes

    return (
        <Dialog
            className="progress-dialog"
            open={props.progressState !== ProgressState.Closed}
            onBackdropClick={() => {
                // allow just clicking out of the dialog to close, unless we're still working,
                // in which case you have to go and click on "CANCEL" or "Stop Sharing"
                if (!somethingStillGoing) {
                    props.onUserClosed();
                }
            }}
        >
            <DialogTitle
                className={props.errorEncountered ? "title-bar-error-mode" : ""}
            >
                {props.heading || "Progress"}
            </DialogTitle>
            {somethingStillGoing && (
                <CircularProgress size={20} className={"circle-progress"} />
            )}
            <DialogContent style={{ width: "500px", height: "300px" }}>
                <Typography className="instruction">
                    {props.instruction || ""}
                </Typography>

                <Typography className="progress-messages-typography">
                    <span
                        className="progress-messages"
                        ref={messagesDivRef}
                        dangerouslySetInnerHTML={{
                            __html: props.messages
                        }}
                    />
                    <span ref={messageEndRef} />
                </Typography>
            </DialogContent>
            <DialogActions>
                {/* This && "" is needed because there's something about DialogActions that choaks if given a `false` in its children */}
                {(somethingStillGoing && "") || (
                    <Button
                        onClick={() => onCopy()}
                        color="primary"
                        style={{ marginRight: "auto" }}
                    >
                        Copy to Clipboard
                    </Button>
                )}

                {(() => {
                    switch (props.progressState) {
                        case ProgressState.Serving:
                            return (
                                <BloomButton
                                    enabled={true}
                                    l10nKey="PublishTab.Common.StopPublishing"
                                    hasText={true}
                                    onClick={props.onUserStopped}
                                >
                                    Stop Publishing
                                </BloomButton>
                            );

                        case ProgressState.Working:
                            return null;
                        //  eventually we'll want this, but at the moment, we only use this state
                        //             for making previews, and in that state Bloom doesn't have a way of
                        //             cancelling.
                        //         <Button
                        //             onClick={props.onUserCanceled}
                        //             color="primary"
                        //         >
                        //             Cancel
                        //         </Button>
                        case ProgressState.Done:
                            return (
                                <Button
                                    variant="contained"
                                    onClick={props.onUserClosed}
                                    color="primary"
                                >
                                    Close
                                </Button>
                            );
                        case ProgressState.Closed:
                            return null;
                    }
                })()}
            </DialogActions>
        </Dialog>
    );
};
