/** @jsx jsx **/
import { jsx, css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../../react_components/bloomButton";
import {
    ImportIcon,
    UseTimingsFileIcon,
    InsertSegmentMarkerIcon,
    EditTimingsFileIcon
} from "./TalkingBookToolboxIcons";
import { Divider, TooltipProps, RadioGroup, Typography } from "@mui/material";
import { MuiRadio } from "../../../react_components/muiRadio";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import { BloomSwitch } from "../../../react_components/BloomSwitch";
import { RecordingMode } from "./audioRecording";
import { TriangleCollapse } from "../../../react_components/TriangleCollapse";

export const TalkingBookAdvancedSection: React.FunctionComponent<{
    isXmatter: boolean;
    hasAudio: boolean;
    lastTimingsFilePath?: string;
    //enableRecordingModeControl: boolean;
    recordingMode: RecordingMode;
    hasRecordableDivs: boolean;
    handleImportRecordingClick: () => void;
    split: (timingFilePath: string) => Promise<void>;
    editTimingsFile: () => void;
    applyTimingsFile: () => void;
    setRecordingMode: (recordingMode: RecordingMode) => Promise<void>;
    inShowPlaybackOrderMode: boolean;
    setShowPlaybackOrder: (isOn: boolean) => void;
    insertSegmentMarker: () => void;

    showingImageDescriptions: boolean;
    setShowingImageDescriptions: (isOn: boolean) => void;
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
        placement: "bottom-start" as TooltipProps["placement"] // toolbox is currently its own iframe, so we can't spill out to the left yet
    };

    return (
        <ThemeProvider theme={toolboxTheme}>
            <TriangleCollapse
                initiallyOpen={false}
                css={css`
                    padding-left: 10px;
                `}
            >
                <BloomTooltip
                    tip={{
                        l10nKey:
                            "EditTab.Toolbox.TalkingBookTool.InsertSegmentMarkerTip"
                    }}
                    showDisabled={!props.hasRecordableDivs}
                    tipWhenDisabled={{
                        l10nKey:
                            "EditTab.Toolbox.TalkingBookTool.NeedCursorInRecordableThingDisabledTip"
                    }}
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
                    showDisabled={props.isXmatter || !props.hasAudio}
                    tipWhenDisabled={
                        (props.isXmatter && {
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.RecordingModeXMatter"
                        }) ||
                        (!props.hasRecordableDivs && {
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.NeedCursorInRecordableThingDisabledTip"
                        }) ||
                        ""
                        // (props.hasAudio
                        //     ? {
                        //           l10nKey: `EditTab.Toolbox.TalkingBookTool.RecordingModeDisabledBecauseHasAudioTip`
                        //       }
                        //     : {
                        //           l10nKey:
                        //               "EditTab.Toolbox.TalkingBookTool.NeedCursorInRecordableThingDisabledTip"
                        //       })
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
                        <BloomTooltip
                            tip={{
                                l10nKey:
                                    "EditTab.Toolbox.TalkingBookTool.RecordingModeSentenceTip"
                            }}
                            showDisabled={
                                props.isXmatter ||
                                !props.hasRecordableDivs ||
                                // we don't allow you to go from textbox to sentence mode if you have audio
                                props.hasAudio
                            }
                            tipWhenDisabled={{
                                l10nKey: `EditTab.Toolbox.TalkingBookTool.RecordingModeDisabledBecauseHasAudioTip`
                            }}
                            {...commonTooltipProps}
                        >
                            <MuiRadio
                                disabled={
                                    props.isXmatter ||
                                    !props.hasRecordableDivs ||
                                    // we don't allow you to go from textbox to sentence mode if you have audio
                                    props.hasAudio
                                }
                                value={RecordingMode.Sentence}
                                label="By Sentence"
                                l10nKey="EditTab.Toolbox.TalkingBookTool.RecordingModeSentence"
                            />
                        </BloomTooltip>
                        <BloomTooltip
                            showDisabled={
                                props.isXmatter || !props.hasRecordableDivs
                            }
                            tip={{
                                l10nKey:
                                    "EditTab.Toolbox.TalkingBookTool.RecordingModeTextBoxTip"
                            }}
                            {...commonTooltipProps}
                        >
                            <MuiRadio
                                disabled={
                                    props.isXmatter || !props.hasRecordableDivs
                                }
                                value={RecordingMode.TextBox}
                                label="By Whole Textbox"
                                l10nKey="EditTab.Toolbox.TalkingBookTool.RecordingModeTextBox"
                            />
                        </BloomTooltip>
                    </RadioGroup>
                </BloomTooltip>
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        gap: 10px;
                        padding-left: 10px;
                    `}
                >
                    <BloomTooltip
                        showDisabled={!enabledImportRecordingButton}
                        tip={{
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.ImportRecordingTip"
                        }}
                        tipWhenDisabled={{
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.ImportRecordingDisabledTip"
                        }}
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
                        tip={{
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.EditTimingsFileTip"
                        }}
                        showDisabled={!enableEditTimings}
                        tipWhenDisabled={{
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.EditTimingsFileDisabledTip"
                        }}
                        {...commonTooltipProps}
                    >
                        <BloomButton
                            id="edit-timings-file-button"
                            iconBeforeText={React.createElement(
                                EditTimingsFileIcon
                            )}
                            hasText={true}
                            variant="outlined"
                            size="small"
                            enabled={enableEditTimings}
                            l10nKey="EditTab.Toolbox.TalkingBookTool.EditTimingsFile"
                            onClick={props.editTimingsFile}
                        />
                    </BloomTooltip>
                    <BloomTooltip
                        tip={{
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.SplitRecordingTip"
                        }}
                        showDisabled={
                            props.recordingMode !== RecordingMode.TextBox ||
                            !props.hasAudio
                        }
                        {...commonTooltipProps}
                    >
                        <BloomButton
                            id="apply-timings-file-button"
                            iconBeforeText={React.createElement(
                                UseTimingsFileIcon
                            )}
                            hasText={true}
                            variant="outlined"
                            size="small"
                            enabled={
                                props.recordingMode === RecordingMode.TextBox &&
                                props.hasAudio
                            }
                            l10nKey="EditTab.Toolbox.TalkingBookTool.ApplyTimingsFile"
                            onClick={props.applyTimingsFile}
                        />
                    </BloomTooltip>
                </div>
                <Divider />
                <BloomTooltip
                    css={css`
                        z-index: 1002; // has to be above the disableOverlay because this is the one control we don't want to disable in show playback order mode
                    `}
                    tip={{
                        l10nKey:
                            "EditTab.Toolbox.TalkingBookTool.ShowPlaybackOrderTip"
                    }}
                    {...commonTooltipProps}
                >
                    <BloomSwitch
                        size="small"
                        // disabled={!props.hasRecordableDivs}
                        checked={props.inShowPlaybackOrderMode}
                        onChange={() =>
                            props.setShowPlaybackOrder(
                                !props.inShowPlaybackOrderMode
                            )
                        }
                        l10nKey="EditTab.Toolbox.TalkingBookTool.ShowPlaybackOrder"
                        highlightWhenTrue={true}
                    />
                </BloomTooltip>
                <BloomSwitch
                    size="small"
                    checked={props.showingImageDescriptions}
                    onChange={() =>
                        props.setShowingImageDescriptions(
                            !props.showingImageDescriptions
                        )
                    }
                    highlightWhenTrue={true}
                    l10nKey="EditTab.Toolbox.TalkingBookTool.ShowImageDescriptions"
                />
            </TriangleCollapse>
        </ThemeProvider>
    );
};
