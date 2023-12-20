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
    DialogCloseButton,
    DialogOkButton
} from "../../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../../react_components/l10nHooks";
import { postBoolean } from "../../../utils/bloomApi";
import { kAudioCurrent } from "./audioRecording";

export const AdjustTimingsDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    const [currentTextBox, setCurrentTextBox] = useState<HTMLElement>();
    const [segments, setSegments] = useState<
        { start: number; end: number; text: string }[]
    >();

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

        interface Segment {
            start: number;
            end: number;
            text: string;
        }

        const endTimes = bloomEditable
            .getAttribute("data-audiorecordingendtimes")
            ?.split(" ")
            .map(parseFloat);
        const segmentElements = Array.from(
            bloomEditable.getElementsByClassName("bloom-highlightSegment")
        ) as HTMLSpanElement[];
        const segmentArray: Segment[] = [];

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
    }, [propsForBloomDialog.open]);

    return (
        <BloomDialog
            {...propsForBloomDialog}
            css={css`
                padding-left: 18px;
                .MuiDialog-paperWidthSm {
                    max-width: 720px;
                }
            `}
            onCancel={closeDialog}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle>
                <div id={"json"}>
                    {/* {segments?.map(segment => JSON.stringify(segment, null, 2))} */}
                    {JSON.stringify(segments, null, 2)}
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    onClick={() => {
                        // read json from #json and then update the data-audiorecordingendtimes attribute in the getCurrentTextBox() div
                        const bloomEditable = getCurrentTextBox();
                        const json = document.getElementById(
                            "json"
                        ) as HTMLElement;
                        const segments = JSON.parse(json.textContent || "");
                        const endTimes = segments.map(
                            (segment: any) => segment.end
                        );
                        bloomEditable.setAttribute(
                            "data-audiorecordingendtimes",
                            endTimes.join(" ").replace("2.72", "1.12")
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
