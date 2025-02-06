/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";
import * as ReactDOM from "react-dom";

import {
    BloomDialog,
    DialogBottomButtons,
    DialogTitle,
    DialogMiddle
} from "../../../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton
} from "../../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../../react_components/l10nHooks";
import { postBoolean } from "../../../utils/bloomApi";
import { kAudioCurrent } from "./audioRecording";
import { AdjustTimingsControl, TimedTextSegment } from "./AdjustTimingsControl";
import {
    getUrlPrefixFromWindowHref,
    kHighlightSegmentClass
} from "../../shared/narration";
import { IAudioRecorder } from "./IAudioRecorder";
import { getToolboxBundleExports } from "../../editViewFrame";

export function getAudioRecorder(): IAudioRecorder | undefined {
    const exports = getToolboxBundleExports();
    const result = exports ? exports.getTheOneAudioRecorder() : undefined;
    return result;
}

export const AdjustTimingsDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    const [segments, setSegments] = useState<
        { start: number; end: number; text: string }[]
    >();
    const [segmentsCreated, setSegmentsCreated] = useState<boolean>(false);
    const [endTimes, setEndTimes] = useState<number[]>([]);
    const [audioFileUrl, setAudioFileUrl] = useState<string>();
    const [fontFamily, setFontFamily] = useState<string>("Andika");

    // Configure the local function (`show`) for showing the dialog to be the one derived from useSetupBloomDialog (`showDialog`)
    // which allows js launchers of the dialog to make it visible (by calling showAdjustTimingsDialog)
    show = showDialog;

    const dialogTitle = useL10n(
        "Adjust Timings",
        "EditTab.Toolbox.TalkingBookTool.AdjustTimings.Dialog.Title"
    );

    // Tell edit tab to disable everything when the dialog is up.
    // (Without this, the page list is not disabled since the modal
    // div only exists in the book pane. Once the whole edit tab is inside
    // one browser, this would not be necessary.)
    React.useEffect(() => {
        if (propsForBloomDialog.open === undefined) return;
        postBoolean("editView/setModalState", propsForBloomDialog.open);
    }, [propsForBloomDialog.open]);

    React.useEffect(() => {
        if (!propsForBloomDialog.open) return;
        const bloomEditable = getCurrentTextBox();
        let ff = (
            bloomEditable.ownerDocument.defaultView || window
        ).getComputedStyle(bloomEditable).fontFamily;
        ff = ff.replace(/^['"]/, "").replace(/['"]$/, "");
        setFontFamily(ff);
        let endTimes = bloomEditable
            .getAttribute("data-audiorecordingendtimes")
            ?.split(" ")
            .map(parseFloat);
        let segmentElements = Array.from(
            bloomEditable.getElementsByClassName(kHighlightSegmentClass)
        ) as HTMLSpanElement[];
        if (
            // No segments typically occurs with new texts that have never been split into segments
            !segmentElements ||
            segmentElements.length === 0 ||
            // No endTimes can occur if the segments were created by putting the documet into sentence mode,
            // or by splitting a previous recording that has now been discarded.
            !endTimes ||
            endTimes.length === 0 ||
            // Not quite sure this is the right thing to do here. Perhaps, for example, the user
            // just added to the text but has not re-recorded. In that case, the previous endTimes might
            // be useful for as many segments as it has. But somehow, the dialog needs to have matching
            // numbers of segments and endTimes. If they don't match, we probably need to start over
            // aligning things. So I think this is a reasonable thing to do.
            endTimes.length !== segmentElements.length
        ) {
            // In any of these case, we want to make sure the splits are up to date and generate
            // a first-attempt at endTimes based on the length of the text.
            // The AdjustTimingsControl will improve on this after loading the audio.
            endTimes = getAudioRecorder()?.autoSegmentBasedOnTextLength() ?? [];
            setSegmentsCreated(true);
            segmentElements = Array.from(
                bloomEditable.getElementsByClassName(kHighlightSegmentClass)
            ) as HTMLSpanElement[];
            // if the user OKs without changing anything, these are the times to save.
            setEndTimes(endTimes);
        }

        // Review: pathological cases are still possible. I saw a book somehow get into a state where
        // data-audiorecordingendtimes was present but had no value, and endtimes was [NaN]. Hopefully
        // just a result of some temporary bad state of the code.
        // There might be no text at all in the box, and therefore zero segments even after calling
        // autoSegmentBasedOnTextLength. But such boxes can't normally become the active text box.
        // There might be a pathological case whre the text was deleted after recording.
        // The dialog is also not much use if there's only one segment, but what should we do instead?
        // I'm thinking it might be best to let JohnH think about these kinds of issues along with any
        // other problems that come up in testing.
        const segmentArray: TimedTextSegment[] = [];

        let start = 0;
        for (let i = 0; i < segmentElements.length; i++) {
            const end = endTimes ? endTimes[i] : start + 1;
            segmentArray.push({
                start,
                end,
                text: segmentElements[i].textContent || ""
            });
            start = end;
        }
        setSegments(segmentArray);
        const prefix = getUrlPrefixFromWindowHref(
            (bloomEditable.ownerDocument.defaultView || window).location.href
        );
        setAudioFileUrl(
            `${prefix}/audio/${bloomEditable.getAttribute("id")}.mp3`
        );
    }, [propsForBloomDialog.open]);

    return (
        <BloomDialog
            {...propsForBloomDialog}
            fullWidth={true}
            maxWidth={false}
            onCancel={reason => {
                if (reason !== "backdropClick") {
                    closeDialog();
                }
            }}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle>
                <AdjustTimingsControl
                    segments={segments!}
                    audioFileUrl={audioFileUrl!}
                    setEndTimes={endTimes => setEndTimes(endTimes)}
                    fontFamily={fontFamily}
                    shouldAdjustSegments={segmentsCreated}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    onClick={() => {
                        // Update the data-audiorecordingendtimes attribute in the getCurrentTextBox() div to match
                        // the current state of the adjustments.
                        const bloomEditable = getCurrentTextBox();
                        bloomEditable.setAttribute(
                            "data-audiorecordingendtimes",
                            endTimes.join(" ")
                        );
                        // We have confirmed split times, display it as split.
                        getAudioRecorder()?.markAudioSplit();

                        closeDialog();
                    }}
                    default
                />
                <DialogCancelButton default={false} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

let show: () => void = () => {
    window.alert("AdjustTimingsDialog is not set up yet.");
};

export function showAdjustTimingsDialog() {
    try {
        ReactDOM.render(<AdjustTimingsDialog />, getModalContainer());
    } catch (error) {
        console.error(error);
    }
    show();
}

function getModalContainer(): HTMLElement {
    let modalDialogContainer = document.getElementById(
        "AdjustTimingsDialogContainer"
    );
    if (modalDialogContainer) {
        modalDialogContainer.remove();
    }
    modalDialogContainer = document.createElement("div");
    modalDialogContainer.id = "AdjustTimingsDialogContainer";
    document.body.appendChild(modalDialogContainer);
    return modalDialogContainer;
}

function getCurrentTextBox(): HTMLElement {
    const page = parent.window.document.getElementById(
        "page"
    ) as HTMLIFrameElement;
    const pageBody = page.contentWindow!.document.body;
    const audioCurrentElements = pageBody.getElementsByClassName(kAudioCurrent);
    const currentTextBox = audioCurrentElements.item(0) as HTMLElement;
    return currentTextBox;
}
