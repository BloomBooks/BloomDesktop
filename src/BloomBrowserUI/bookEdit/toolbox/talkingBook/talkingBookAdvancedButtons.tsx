/** @jsx jsx **/
import { jsx, css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../../react_components/bloomButton";
import {
    ImportIcon,
    UseTimingsFileIcon,
    InsertSegmentMarkerIcon
} from "./TalkingBookToolboxIcons";
import { postJson } from "../../../utils/bloomApi";
import {
    Button,
    Collapse,
    Divider,
    TooltipProps,
    Checkbox,
    FormControl,
    FormControlLabel,
    FormLabel,
    RadioGroup,
    Radio,
    Typography
} from "@mui/material";
import { Span } from "../../../react_components/l10nComponents";
//import { Checkbox } from "../../../react_components/checkbox";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import { BloomSwitch } from "../../../react_components/BloomSwitch";
import { RecordingMode } from "./audioRecording";

export const TalkingBookAdvancedButtons: React.FunctionComponent<{
    isXmatter: boolean;
    hasAudio: boolean;
    lastTimingsFilePath?: string;
    //enableRecordingModeControl: boolean;
    recordingMode: RecordingMode;
    hasRecordableDivs: boolean;
    handleImportRecordingClick: () => void;
    split: (timingFilePath: string) => Promise<void>;
    enableRecordingModeControl: boolean;
    setRecordingMode: (recordingMode: RecordingMode) => Promise<void>;
    inShowPlaybackOrderMode: boolean;
    setShowPlaybackOrder: (isOn: boolean) => void;
    insertSegmentMarker: () => void;
}> = props => {
    // return a triangle button. Its children are normally hidden. When you click it, it rotates and shows its children.

    const enableEditTimings =
        props.recordingMode === RecordingMode.TextBox &&
        props.hasAudio &&
        !!props.lastTimingsFilePath;

    const enabledImportRecordingButton =
        props.recordingMode === RecordingMode.TextBox &&
        props.hasRecordableDivs;
    const commonTooltipProps = {
        placement: "bottom" as TooltipProps["placement"] // toolbox is currently its own iframe, so we can't spill out to the left yet
    };

    return (
        <ThemeProvider theme={toolboxTheme}>
            <TriangleCollapse
                initiallyOpen={true} /*for dev. enhance: remember the state*/
            >
                <BloomTooltip
                    showDisabled={!props.enableRecordingModeControl}
                    tipWhenDisabled={
                        (props.isXmatter && {
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.RecordingModeXMatter"
                        }) ||
                        (props.hasRecordableDivs
                            ? `First select a box has something to record.`
                            : `If you want to turn change this mode, first use the "Clear" button to remove your recording.`)
                    }
                    {...commonTooltipProps}
                >
                    <Typography variant="h2">Recording Mode</Typography>
                    <RadioGroup
                        value={props.recordingMode}
                        onChange={(
                            event: React.ChangeEvent<HTMLInputElement>
                        ) =>
                            props.setRecordingMode(
                                RecordingMode[
                                    (event.target as HTMLInputElement).value
                                ]
                            )
                        }
                    >
                        {" "}
                        <BloomTooltip
                            tip={
                                "Record one sentence at a time. You can break up sentences using the `|` character."
                            }
                            {...commonTooltipProps}
                        >
                            <FormControlLabel
                                disabled={!props.enableRecordingModeControl}
                                value={RecordingMode.Sentence}
                                control={<Radio />}
                                label="By Sentence"
                            ></FormControlLabel>
                        </BloomTooltip>
                        <BloomTooltip
                            showDisabled={!props.enableRecordingModeControl}
                            tip={
                                "Record the whole text box in one take, or load in a recording."
                            }
                            {...commonTooltipProps}
                        >
                            <FormControlLabel
                                disabled={!props.enableRecordingModeControl}
                                value={RecordingMode.TextBox}
                                control={<Radio />}
                                label="By Whole Textbox"
                            ></FormControlLabel>
                        </BloomTooltip>
                    </RadioGroup>
                </BloomTooltip>
                <BloomTooltip
                    showDisabled={!enabledImportRecordingButton}
                    tip={"Import an mp3 recording of the whole text box."}
                    tipWhenDisabled={
                        "This is disabled because this box is not in `Record by Text Box` mode."
                    }
                    {...commonTooltipProps}
                >
                    <BloomButton
                        id="import-recording-button"
                        iconBeforeText={React.createElement(ImportIcon)}
                        hasText={true}
                        variant="outlined"
                        size="small"
                        enabled={enabledImportRecordingButton}
                        l10nKey="EditTab.Toolbox.TalkingBookTool.ImportRecording"
                        onClick={() => props.handleImportRecordingClick()}
                    />
                </BloomTooltip>
                <BloomTooltip
                    id="edit-timings-file-button-tooltip"
                    tip={
                        "Open the timing file in a text editor. After you save your changes, click the `Apply Timings File` button."
                    }
                    showDisabled={!enableEditTimings}
                    tipWhenDisabled={`This is only enabled after doing a "Split" of a recording of the whole text box.`}
                    {...commonTooltipProps}
                >
                    <BloomButton
                        id="edit-timings-file-button"
                        hasText={true}
                        variant="outlined"
                        size="small"
                        enabled={enableEditTimings}
                        l10nKey="EditTab.Toolbox.TalkingBookTool.EditTimingsFile"
                        onClick={async () => {
                            const result = await postJson("fileIO/openFile", {
                                path: props.lastTimingsFilePath
                            });
                        }}
                    />
                </BloomTooltip>
                <BloomTooltip
                    tip={
                        "This lets you choose a file containing timings for aligning the audio with the text. The format of the file is tab-separated values, with the first column being the start time in seconds, and the second column being the end time in seconds. The third column is often a label, but it will be ignored."
                    }
                    showDisabled={!enableEditTimings}
                    tipWhenDisabled={`This is only enabled after doing a "Split" of a recording of the whole text box.`}
                    {...commonTooltipProps}
                >
                    <BloomButton
                        id="apply-timings-file-button"
                        iconBeforeText={React.createElement(UseTimingsFileIcon)}
                        hasText={true}
                        variant="outlined"
                        size="small"
                        enabled={
                            props.hasRecordableDivs &&
                            props.recordingMode &&
                            props.hasAudio
                        }
                        l10nKey="EditTab.Toolbox.TalkingBookTool.ApplyTimingsFile"
                        onClick={async () => {
                            const result = await postJson("fileIO/chooseFile", {
                                title: "Choose Timing File",
                                fileTypes: [
                                    {
                                        name: "Tab-separated Timing File",
                                        extensions: ["txt", "tsv"]
                                    }
                                ],
                                defaultPath: props.lastTimingsFilePath
                            });
                            if (!result || !result.data) {
                                return;
                            }
                            props.split(result.data);
                        }}
                    />
                </BloomTooltip>
                <BloomTooltip
                    tip={`Insert a "|" character into the text at the current cursor position. This character tells Bloom to introduce a new segment for the purpose of highlighting the text during audio playback. You can also just type the "|" character using your keyboard.`}
                    showDisabled={!props.hasRecordableDivs}
                    tipWhenDisabled={`First select a box has something to record.`}
                    {...commonTooltipProps}
                >
                    <BloomButton
                        id="insert-segment-marker-button"
                        iconBeforeText={React.createElement(
                            InsertSegmentMarkerIcon
                        )}
                        hasText={true}
                        variant="outlined"
                        size="small"
                        enabled={props.hasRecordableDivs}
                        l10nKey="EditTab.Toolbox.TalkingBookTool.InsertSegmentMarker"
                        onClick={props.insertSegmentMarker}
                    />
                </BloomTooltip>
                <Divider />
                <BloomTooltip
                    tip={
                        "Control the order in which various boxes on the page are played back."
                    }
                    {...commonTooltipProps}
                >
                    <BloomSwitch
                        css={css`
                            z-index: 1002; // has to be above the disableOverlay because this is the one control we don't want to disable in show playback order mode
                        `}
                        size="small"
                        // disabled={!props.hasRecordableDivs}
                        checked={props.inShowPlaybackOrderMode}
                        onChange={() =>
                            props.setShowPlaybackOrder(
                                !props.inShowPlaybackOrderMode
                            )
                        }
                        l10nKey="EditTab.Toolbox.TalkingBookTool.ShowPlaybackOrder"
                    />
                </BloomTooltip>
            </TriangleCollapse>
        </ThemeProvider>
    );
};

// todo: move this to its own component
const TriangleCollapse: React.FC<{
    initiallyOpen: boolean;
    children: React.ReactNode;
}> = props => {
    const [open, setOpen] = React.useState(props.initiallyOpen);

    const handleClick = () => {
        setOpen(!open);
    };

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                gap: 5px;
            `}
        >
            <Button
                onClick={handleClick}
                css={css`
                    justify-content: start;
                    text-transform: none;
                    padding-left: 0;
                `}
            >
                <svg
                    width="10"
                    height="10"
                    css={css`
                        transition: transform 0.2s ease-in-out;
                        transform: ${open ? "rotate(135deg)" : "rotate(90deg)"};
                    `}
                >
                    <path d="M 0 10 L 5 0 L 10 10 Z" fill="white" />
                </svg>
                <Span
                    css={css`
                        margin-left: 5px;
                    `}
                    l10nKey="Common.Advanced"
                ></Span>
            </Button>
            <Collapse
                in={open}
                css={css`
                    .MuiCollapse-wrapperInner {
                        display: flex;
                        flex-direction: column;
                        gap: 5px;
                    }
                `}
            >
                {props.children}
            </Collapse>
        </div>
    );
};
