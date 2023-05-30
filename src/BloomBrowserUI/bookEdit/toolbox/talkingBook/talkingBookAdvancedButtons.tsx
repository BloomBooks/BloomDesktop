/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../../react_components/bloomButton";
import {
    ImportIcon,
    UseTimingsFileIcon,
    InsertSegmentMarkerIcon
} from "./TalkingBookToolboxIcons";
import { postJson } from "../../../utils/bloomApi";
import { Box, Button, Collapse, styled } from "@mui/material";
import { Span } from "../../../react_components/l10nComponents";
import { CheckBox } from "@mui/icons-material";
import { Checkbox } from "../../../react_components/checkbox";

export const TalkingBookAdvancedButtons: React.FunctionComponent<{
    hasAudio: boolean;
    lastTimingsFilePath?: string;
    wholeTextBoxMode: boolean;
    hasRecordableDivs: boolean;
    handleImportRecordingClick: () => void;
    split: (timingFilePath: string) => Promise<void>;
    toggleRecordingModeAsync: () => Promise<void>;
}> = props => {
    // return a triangle button. Its children are normally hidden. When you click it, it rotates and shows its children.

    return (
        <TriangleCollapse
            initiallyOpen={true} /*for dev. enhance: remember the state*/
        >
            <Checkbox
                id="audio-recordingModeControl"
                disabled={!(props.wholeTextBoxMode && props.hasRecordableDivs)}
                l10nKey="EditTab.Toolbox.TalkingBookTool.RecordByTextBox"
                onClick={() => props.handleImportRecordingClick()}
            />
            <BloomButton
                id="import-recording-button"
                iconBeforeText={React.createElement(ImportIcon)}
                hasText={true}
                variant="outlined"
                size="small"
                enabled={props.wholeTextBoxMode && props.hasRecordableDivs}
                l10nKey="EditTab.Toolbox.TalkingBookTool.ImportRecording"
                onClick={() => props.handleImportRecordingClick()}
            />
            <BloomButton
                id="edit-timings-file-button"
                hasText={true}
                variant="outlined"
                size="small"
                enabled={
                    props.wholeTextBoxMode &&
                    props.hasAudio &&
                    !!props.lastTimingsFilePath
                }
                l10nKey="EditTab.Toolbox.TalkingBookTool.EditTimingsFile"
                onClick={async () => {
                    const result = await postJson("fileIO/openFile", {
                        path: props.lastTimingsFilePath
                    });
                }}
            />
            <BloomButton
                id="apply-timings-file-button"
                iconBeforeText={React.createElement(UseTimingsFileIcon)}
                hasText={true}
                variant="outlined"
                size="small"
                enabled={props.wholeTextBoxMode && props.hasAudio}
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
            <BloomButton
                id="insert-segment-marker-button"
                iconBeforeText={React.createElement(InsertSegmentMarkerIcon)}
                hasText={true}
                variant="outlined"
                size="small"
                enabled={props.wholeTextBoxMode && props.hasRecordableDivs}
                l10nKey="EditTab.Toolbox.TalkingBookTool.InsertSegmentMarker"
                onClick={() => {}} // TODO
            />
        </TriangleCollapse>
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
        <Box>
            <Button
                onClick={handleClick}
                css={css`
                    justifycontent: "flex-start";
                    text-transform: none;
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
            <Collapse in={open}>{props.children}</Collapse>
        </Box>
    );
};
