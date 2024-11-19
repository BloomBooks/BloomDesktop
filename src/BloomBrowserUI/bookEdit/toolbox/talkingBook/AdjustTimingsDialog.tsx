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
import { getUrlPrefixFromWindowHref } from "../dragActivity/narration";
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
    const [url, setUrl] = useState<string>();
    const [fontFamily, setFont] = useState<string>("Andika");

    // Configure the local function (`show`) for showing the dialog to be the one derived from useSetupBloomDialog (`showDialog`)
    // which allows js launchers of the dialog to make it visible (by calling showCopyrightAndLicenseInfoOrDialog)
    show = showDialog;

    const dialogTitle = useL10n(
        "Adjust Timings",
        "EditTab.Toolbox.TalkingBookTool.AdjustTimings.Dialog.Title"
    ); // TODO

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
        console.log(
            `AudjustTimingsControl bloomEditable: ${bloomEditable.outerHTML}`
        );
        let ff = (
            bloomEditable.ownerDocument.defaultView || window
        ).getComputedStyle(bloomEditable).fontFamily;
        ff = ff.replace(/^['"]/, "").replace(/['"]$/, "");
        setFont(ff);
        let endTimes = bloomEditable
            .getAttribute("data-audiorecordingendtimes")
            ?.split(" ")
            .map(parseFloat);
        console.log(`AudjustTimingsControl endTimes: ${endTimes}`);
        let segmentElements = Array.from(
            bloomEditable.getElementsByClassName("bloom-highlightSegment")
        ) as HTMLSpanElement[];
        if (
            // No segments typically occurs with new texts that have never been split into segments
            !segmentElements ||
            segmentElements.length === 0 ||
            // No endTimes can occur if the segments were created by putting the documet into sentence mode,
            // or by splitting a previous recording that has now been discarded.
            !endTimes ||
            endTimes.length === 0
        ) {
            // In any of these case, we want to make sure the splits are up to date and generate
            // a first-attempt at endTimes based on the length of the text.
            // The AdjustTimingsControl will improve on this after loading the audio.
            getAudioRecorder()?.autoSegmentBasedOnTextLength();
            setSegmentsCreated(true);
            segmentElements = Array.from(
                bloomEditable.getElementsByClassName("bloom-highlightSegment")
            ) as HTMLSpanElement[];
            endTimes = bloomEditable
                .getAttribute("data-audiorecordingendtimes")
                ?.split(" ")
                .map(parseFloat);
        }

        // Review: do we need to better handle pathological cases like not having a duration, not having any segments,
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
        console.log(
            `AudjustTimingsControl segmentArray: ${JSON.stringify(
                segmentArray,
                null,
                2
            )}`
        );
        setSegments(segmentArray);
        console.log(
            `AudjustTimingsControl url: ${bloomEditable.getAttribute("id")}`
        );
        const prefix = getUrlPrefixFromWindowHref(
            (bloomEditable.ownerDocument.defaultView || window).location.href
        );
        setUrl(`${prefix}/audio/${bloomEditable.getAttribute("id")}.mp3`);
    }, [propsForBloomDialog.open]);

    return (
        <BloomDialog
            {...propsForBloomDialog}
            // css={css`
            //     padding-left: 18px;
            // `}
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
                    url={url!}
                    setEndTimes={endTimes => setEndTimes(endTimes)}
                    fontFamily={fontFamily}
                    shouldAdjustSegments={segmentsCreated}
                />
                {/* <div id={"json"}>
                    {JSON.stringify(segments, null, 2)}
                </div>
                <div>{endTimes.join(" ")}</div> */}
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    onClick={() => {
                        // read json from #json and then update the data-audiorecordingendtimes attribute in the getCurrentTextBox() div
                        const bloomEditable = getCurrentTextBox();
                        bloomEditable.setAttribute(
                            "data-audiorecordingendtimes",
                            endTimes.join(" ")
                        );

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
