import { css } from "@emotion/react";

import * as React from "react";
import { useCallback, useState } from "react";
import * as ReactDOM from "react-dom";

import {
    BloomDialog,
    DialogBottomButtons,
    DialogTitle,
    DialogMiddle,
} from "../../../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton,
} from "../../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../../react_components/l10nHooks";
import { getAsync, postBoolean, postJsonAsync } from "../../../utils/bloomApi";
import { kAudioCurrent } from "./audioRecording";
import { AdjustTimingsControl, TimedTextSegment } from "./AdjustTimingsControl";
import {
    getUrlPrefixFromWindowHref,
    kHighlightSegmentClass,
} from "bloom-player";
import { Div } from "../../../react_components/l10nComponents";
import SyncIcon from "@mui/icons-material/Sync";
import EditIcon from "@mui/icons-material/Edit";
import AccessTimeIcon from "@mui/icons-material/AccessTime";
import { LocalizableMenuItem } from "../../../react_components/localizableMenuItem";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import { PWithLink } from "../../../react_components/pWithLink";
import { Icon, Menu } from "@mui/material";
import { getAudioRecorder } from "./audioRecording";

const timingsMenuId = "timingsMenuAnchor";

export const AdjustTimingsDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    split: (timingFilePath: string) => Promise<string | undefined>;
    editTimingsFile: (timingsFilePath?: string) => Promise<void>;
    applyTimingsFile: (timingsFilePath?: string) => Promise<string | undefined>;
    closing: (canceling: boolean) => void;
}> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);
    const [segments, setSegments] =
        useState<{ start: number; end: number; text: string }[]>();
    // Should the next render of the AdjustTimingsControl adjust the segments based on the audio?
    // This should only happen on the render immediately after we create the segments based on the text length.
    const [shouldAdjustSegments, setShouldAdjustSegments] = useState(false);
    const [endTimes, setEndTimes] = useState<number[]>([]);
    const [audioFileUrl, setAudioFileUrl] = useState<string>();
    const [fontFamily, setFontFamily] = useState<string>("Andika");
    const [haveAeneasDeps, setHaveAeneasDeps] = useState(false);
    const [missingAeneasTip, setMissingAeneasTip] = useState("");
    // initially, the content of data-audioRecordingEndTimes. After loading times from a file
    // or running Aeneas, this will be the new times. Unlike endTimes, this is not continually
    // updated as the user adjusts the times in the dialog. It is only updated when we load
    // timings from some other source and want to replace the ones currently shown in the dialog.
    const [audioRecordingEndTimes, setAudioRecordingEndTimesReal] = useState<
        string | undefined | null
    >();
    // used to force a re-run of the code that handles audioRecordingTimes
    // when we reset audioRecordingTimes but it might not
    // have changed. For example, if we set the times by loading a timings file (sets
    // audioRecordingEndTimes) and then adjust them by hand (does not change audioRecordingEndTimes),
    // then apply the same timings file again, we need to force a re-render to show the restored times,
    // which are not different from when we last applied the timings file.
    const [generation, setGeneration] = useState(0);
    const setAudioRecordingEndTimes = useCallback(
        (newTimes: string | undefined | null) => {
            setAudioRecordingEndTimesReal(newTimes);
            setGeneration((oldGen) => oldGen + 1);
        },
        [],
    );
    // More menu
    const [moreElForAdvancedMenu, setMoreElForAdvancedMenu] =
        React.useState<null | HTMLElement>(null);
    const moreMenuOpen = Boolean(moreElForAdvancedMenu);
    const handleClick = () => {
        const anchor = document.getElementById(timingsMenuId);
        setMoreElForAdvancedMenu(anchor);
    };
    const closeMoreMenu = () => {
        setMoreElForAdvancedMenu(null);
    };

    // Called when the times are updated from the dialog.
    // An important time this happens is when we render with shouldAdjustSegments true, meaning we
    // initialized the segments based on the text length, and want the control to fine tune
    // them based on the audio. This should happen just once, not on any subsequent render
    // when we pass in segments from some other source, such as Aeneas or a timings file.
    // So when we get this message, we clear the flag. (Clearing the flag woult not require
    // a render, which makes a useRef appealing, but we always change some other state when we
    // change setShouldAdjustSegments, so there's no point.)
    const updateEndTimes = (newTimes: number[]) => {
        // I think these two state changes will result in just one re-render, but just in
        // case we set this flag first, so that the next render will not try to adjust the segments.
        setShouldAdjustSegments(false);
        setEndTimes(newTimes);
    };

    // Configure the local function (`show`) for showing the dialog to be the one derived from useSetupBloomDialog (`showDialog`)
    // which allows js launchers of the dialog to make it visible (by calling showAdjustTimingsDialog)
    show = showDialog;

    const dialogTitle = useL10n(
        "Adjust Timings",
        "EditTab.Toolbox.TalkingBookTool.AdjustTimings.Dialog.Title",
    );
    const [timingsFilePath, setTimingsFilePath] = useState<string>();

    // Tell edit tab to disable everything when the dialog is up.
    // (Without this, the page list is not disabled since the modal
    // div only exists in the book pane. Once the whole edit tab is inside
    // one browser, this would not be necessary.)
    React.useEffect(() => {
        if (propsForBloomDialog.open === undefined) return;
        postBoolean("editView/setModalState", propsForBloomDialog.open);
    }, [propsForBloomDialog.open]);

    React.useEffect(() => {
        const checkForAeneas = async () => {
            const result = await getAsync(
                "audioSegmentation/checkAutoSegmentDependencies",
            );

            setHaveAeneasDeps(result.data !== "FALSE");
            if (result.data === "FALSE") {
                theOneLocalizationManager
                    .asyncGetText(
                        "EditTab.Toolbox.TalkingBook.MissingDependency",
                        "To split recordings into sentences, first install this {0} system.",
                        "The placeholder {0} will be replaced with the dependency that needs to be installed.",
                    )
                    .done((localizedMessage) => {
                        const missingDependencyHoverTip =
                            theOneLocalizationManager.simpleFormat(
                                localizedMessage,
                                ["aeneas"],
                            );
                        setMissingAeneasTip(missingDependencyHoverTip);
                    });
            }
        };
        checkForAeneas();

        // When the dialog starts up we load the times from the data-audiorecordingendtimes attribute
        // if we have one. This really wants to not happen again, since it would discard any changes
        // the user has made.
        setAudioRecordingEndTimes(
            getCurrentTextBox()?.getAttribute("data-audiorecordingendtimes"),
        );
        // This is supposed to execute exactly once, when the dialog is first opened.
        // React insists it must have this dependency, even though I set up a useCallback
        // to make sure we only have one version of this function ever. Grrr!
    }, [setAudioRecordingEndTimes]);

    const setPauseBasedEndTimes = () => {
        const endTimes =
            getAudioRecorder()?.autoSegmentBasedOnTextLength() ?? [];
        // will be passed to the control to tell it to fine tune the segments based on the audio.
        // gets set back to false when the control sends us the adjusted times.
        setShouldAdjustSegments(true);
        const bloomEditable = getCurrentTextBox();

        const segmentElements = Array.from(
            bloomEditable.getElementsByClassName(kHighlightSegmentClass),
        ) as HTMLSpanElement[];
        // if the user OKs without changing anything, these are the times to save.
        // (probably redundant, the dialog will send us new values after adjusting).
        setEndTimes(endTimes);
        setSegments(computeSegments(endTimes, segmentElements));
    };

    React.useEffect(() => {
        if (!propsForBloomDialog.open) return;
        const bloomEditable = getCurrentTextBox();
        async function getTimingsFileData() {
            setTimingsFilePath(await getTimingsFileName());
        }
        getTimingsFileData();
        const ff = (
            bloomEditable.ownerDocument.defaultView || window
        ).getComputedStyle(bloomEditable).fontFamily;

        setFontFamily(ff);
        let endTimes = audioRecordingEndTimes?.split(" ").map(parseFloat);
        let segmentElements = Array.from(
            bloomEditable.getElementsByClassName(kHighlightSegmentClass),
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
            // will be passed to the control to tell it to fine tune the segments based on the audio.
            // gets set back to false when the control sends us the adjusted times.
            setShouldAdjustSegments(true);
            segmentElements = Array.from(
                bloomEditable.getElementsByClassName(kHighlightSegmentClass),
            ) as HTMLSpanElement[];
        }
        // if the user OKs without changing anything, these are the times to save.
        setEndTimes(endTimes);

        setSegments(computeSegments(endTimes, segmentElements));
        const prefix = getUrlPrefixFromWindowHref(
            (bloomEditable.ownerDocument.defaultView || window).location.href,
        );
        setAudioFileUrl(
            `${prefix}/audio/${bloomEditable.getAttribute("id")}.mp3`,
        );
    }, [propsForBloomDialog.open, audioRecordingEndTimes, generation]);

    return (
        <BloomDialog
            {...propsForBloomDialog}
            fullWidth={true}
            maxWidth={false}
            onCancel={(reason) => {
                if (reason !== "backdropClick") {
                    props.closing(true);
                    closeDialog();
                }
            }}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle>
                <AdjustTimingsControl
                    segments={segments!}
                    audioFileUrl={audioFileUrl!}
                    setEndTimes={(endTimes) => updateEndTimes(endTimes)}
                    fontFamily={fontFamily}
                    shouldAdjustSegments={shouldAdjustSegments}
                />
                <div
                    id={timingsMenuId}
                    css={css`
                        display: flex;
                        width: fit-content;
                        margin-top: 20px;
                    `}
                    onClick={handleClick}
                >
                    <Div
                        l10nKey="CollectionTab.ContextMenu.More"
                        css={css`
                            display: flex;
                        `}
                    >
                        More
                    </Div>
                    <span
                        css={css`
                            padding: 0px 15px 0px 6px;
                            position: relative;
                            top: -2px;
                        `}
                    >
                        ▶
                    </span>
                </div>
                <Menu
                    open={moreMenuOpen}
                    onClose={closeMoreMenu}
                    anchorEl={moreElForAdvancedMenu}
                    anchorOrigin={{ vertical: "top", horizontal: "right" }}
                    css={css`
                        max-width: 397px;
                        p,
                        h6 {
                            white-space: wrap !important;
                        }
                        li {
                            align-items: flex-start; // icons at top
                        }
                    `}
                >
                    <LocalizableMenuItem
                        l10nId="EditTab.Toolbox.TalkingBookTool.AdjustTimings.Dialog.Rerun"
                        onClick={() => {
                            closeMoreMenu();
                            setPauseBasedEndTimes();
                        }}
                        icon={<SyncIcon />}
                        english={"Re-run pause-based timing guesses"}
                        subLabelL10nId="EditTab.Toolbox.TalkingBookTool.AdjustTimings.Dialog.Rerun.Extra"
                    />
                    <LocalizableMenuItem
                        l10nId="EditTab.Toolbox.TalkingBookTool.AdjustTimings.Dialog.Aeneas"
                        onClick={async () => {
                            closeMoreMenu();
                            // passing an empty string causes us to actually run Aeneas. Otherwise, the api
                            // just reads the existing file, if there is one.
                            const newTimes = await props.split("");
                            if (newTimes) {
                                setAudioRecordingEndTimes(newTimes);
                            }
                        }}
                        disabled={!haveAeneasDeps}
                        // JohnH wants to design an icon one day.
                        // Possibly the old Split icon, split-enabled.svg?
                        // This was not good enough.
                        // icon={<QueryStatsIcon />}
                        // This takes up the space an icon would normally use to align things.
                        icon={
                            <EditIcon
                                css={css`
                                    visibility: hidden;
                                `}
                            />
                        }
                        english={"Use Aeneas to guess timings"}
                        subLabel={
                            <PWithLink
                                css={css`
                                    margin-top: -3px;
                                    margin-block-end: 0;
                                    // We want the link to work even when the menu item is disabled
                                    a {
                                        pointer-events: auto;
                                    }
                                `}
                                // Enhance: this is just the download tab. We want a page that explains
                                // what Aeneas is and why you might want to use it.
                                href="https://bloomlibrary.org/aeneas"
                                l10nKey="EditTab.Toolbox.TalkingBookTool.AdjustTimings.Dialog.Aeneas.Extra"
                            />
                        }
                        tooltipIfDisabled={missingAeneasTip}
                    />
                    <LocalizableMenuItem
                        l10nId="EditTab.Toolbox.TalkingBookTool.EditTimingsFile"
                        onClick={() => {
                            closeMoreMenu();
                            exportTimingsFile(timingsFilePath!, endTimes);
                            props.editTimingsFile(timingsFilePath);
                        }}
                        icon={<EditIcon />}
                        english={"Edit timings file..."}
                        subLabelL10nId="EditTab.Toolbox.TalkingBookTool.EditTimingsFile.Extra"
                    />
                    <LocalizableMenuItem
                        l10nId="EditTab.Toolbox.TalkingBookTool.ApplyTimingsFile"
                        onClick={async () => {
                            closeMoreMenu();
                            const newTimes =
                                await props.applyTimingsFile(timingsFilePath);
                            if (newTimes) {
                                setAudioRecordingEndTimes(newTimes);
                            }
                        }}
                        icon={<AccessTimeIcon />}
                        english={"Apply timings file..."}
                        //subLabelL10nId="EditTab.Toolbox.TalkingBookTool.ApplyTimingsFile.Extra"
                    />
                </Menu>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    onClick={() => {
                        // Update the data-audiorecordingendtimes attribute in the getCurrentTextBox() div to match
                        // the current state of the adjustments.
                        const bloomEditable = getCurrentTextBox();
                        bloomEditable.setAttribute(
                            "data-audiorecordingendtimes",
                            endTimes.join(" "),
                        );
                        // We have confirmed split times, display it as split.
                        getAudioRecorder()?.markAudioSplit();
                        props.closing(false);
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

export function showAdjustTimingsDialog(
    split: (timingFilePath: string) => Promise<string | undefined>,
    editTimingsFile: (timingsFilePath?: string) => Promise<void>,
    applyTimingsFile: (timingsFilePath?: string) => Promise<string | undefined>,
    closing: (canceling: boolean) => void,
) {
    try {
        ReactDOM.render(
            <AdjustTimingsDialog
                split={split}
                editTimingsFile={editTimingsFile}
                applyTimingsFile={applyTimingsFile}
                closing={closing}
            />,
            // creates (or recreates) a div in the top frame to allow the dialog to be as wide as possible.
            // it can overlap both the book and the toolbox.
            getModalContainer(),
        );
    } catch (error) {
        console.error(error);
    }
    show();
}

function getModalContainer(): HTMLElement {
    let modalDialogContainer = document.getElementById(
        "AdjustTimingsDialogContainer",
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
        "page",
    ) as HTMLIFrameElement;
    const pageBody = page.contentWindow!.document.body;
    const audioCurrentElements = pageBody.getElementsByClassName(kAudioCurrent);
    const currentTextBox = audioCurrentElements.item(0) as HTMLElement;
    return currentTextBox;
}

// Existing code wants to be passed the actual path (not a BloomServer url) to the timings file.
// Rather than try to rework all that, I made an api that allows us to get such a path
// from a path starting at the current book's folder.
async function getTimingsFileName(): Promise<string> {
    const bloomEditable = getCurrentTextBox();
    const fileName = `audio/${bloomEditable.getAttribute("id")}_timings.txt`;
    // id should be a guid, so should not need encoding.
    const result = await postJsonAsync("fileIO/completeRelativePath?", {
        relativePath: fileName,
    });
    return result?.data;
}

// Compute the segment objects we will pass to the AdjustTimingsControl,
// based on parallel arrays of endTimes and the elements that represent the segments.
const computeSegments = (
    endTimes: number[],
    segmentElements: HTMLSpanElement[],
): TimedTextSegment[] => {
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
    // There's no obvious behavior here if the two arrays are not the same length,
    // and if the value we get from the data attribute does not have the right number of
    // items, code elsewhere reverts to the autoSegmentBasedOnTextLength behavior,
    // which should give the right number of segments, at least.
    console.assert(
        endTimes.length === segmentElements.length,
        "Mismatched endTimes and segments",
    );

    let start = 0;
    for (let i = 0; i < segmentElements.length; i++) {
        const end = endTimes[i];
        segmentArray.push({
            start,
            end,
            text: segmentElements[i].textContent || "",
        });
        start = end;
    }
    return segmentArray;
};

// Save the current timings to a file in the book's folder that can be edited either by hand
// or using a tool like Audacity.
const exportTimingsFile = async (
    timingsFileName: string,
    endTimes: number[],
) => {
    const bloomEditable = getCurrentTextBox();
    const segmentElements = Array.from(
        bloomEditable.getElementsByClassName(kHighlightSegmentClass),
    ) as HTMLElement[];
    let start = 0;
    let fileContent = "";
    for (let i = 0; i < endTimes.length; i++) {
        const element = segmentElements[i];
        fileContent += `${start.toFixed(3)}\t${endTimes[i].toFixed(3)}\t${
            element.innerText
        }\r\n`;
        start = endTimes[i];
    }
    await postJsonAsync("fileIO/writeFile", {
        path: timingsFileName,
        content: fileContent,
    });
};
