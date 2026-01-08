import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../../react_components/bloomButton";
import { ImportIcon, InsertSegmentMarkerIcon } from "./TalkingBookToolboxIcons";
import { Divider, TooltipProps, RadioGroup, Typography } from "@mui/material";
import { MuiRadio } from "../../../react_components/muiRadio";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import { BloomSwitch } from "../../../react_components/BloomSwitch";
import { RecordingMode } from "./recordingMode";
import { TriangleCollapse } from "../../../react_components/TriangleCollapse";
import { LocalizedString } from "../../../react_components/l10nComponents";
import { useL10n } from "../../../react_components/l10nHooks";
import { RequiresSubscriptionAdjacentIconWrapper } from "../../../react_components/requiresSubscription";
import { useGetFeatureStatus } from "../../../react_components/featureStatus";

export const TalkingBookAdvancedSection: React.FunctionComponent<{
    hasAudio: boolean;
    //enableRecordingModeControl: boolean;
    recordingMode: RecordingMode;
    // this will differ from recordingMode if we changed to textbox mode but
    // haven't yet recorded anything or imported audio. It is used to let someone go back to sentence mode before committing to textbox mode.
    haveACurrentTextboxModeRecording: boolean;
    hasRecordableDivs: boolean;
    handleImportRecordingClick: () => void;
    setRecordingMode: (recordingMode: RecordingMode) => Promise<void>;
    inShowPlaybackOrderMode: boolean;
    setShowPlaybackOrder: (isOn: boolean) => void;
    insertSegmentMarker: () => void;

    showingImageDescriptions: boolean;
    setShowingImageDescriptions: (isOn: boolean) => void;
}> = (props) => {
    // return a triangle button. Its children are normally hidden. When you click it, it rotates and shows its children.

    const wholeTextBoxAudioFeatureStatus = useGetFeatureStatus(
        "WholeTextBoxAudio",
        false,
    );
    const enabledImportRecordingButton =
        props.recordingMode === RecordingMode.TextBox &&
        !!wholeTextBoxAudioFeatureStatus?.enabled &&
        props.hasRecordableDivs;
    // The toolbox is currently its own iframe, so we can't spill out to the left yet.
    // Unfortunately, we can't spill out the bottom without bad side effects either (BL-12366), so we go topside.
    const commonTooltipProps = {
        placement: "top-start" as TooltipProps["placement"],
    };

    // Originally, this string was just the first part without the final sentence. When we added the final sentence,
    // we decided among the various unpleasant options that we would just add a new l10n string.
    //Some day we may have to revisit if a translator says this is unfeasible for a particular language.
    const insertSegmentMarkerTooltipText =
        useL10n(
            `Click this to insert a "|" character into the text at the current cursor position. This character tells Bloom to introduce a new segment for the purpose of highlighting the text during audio playback. You can also just type the "|" character using your keyboard.`,
            "EditTab.Toolbox.TalkingBookTool.InsertSegmentMarkerTip",
        ) +
        " " +
        useL10n(
            `Bloom will hide these when you publish.`,
            "EditTab.Toolbox.TalkingBookTool.InsertSegmentMarkerTipAddition",
        );

    return (
        <ThemeProvider theme={toolboxTheme}>
            <TriangleCollapse
                initiallyOpen={false}
                css={css`
                    padding-left: 10px;
                `}
            >
                <BloomTooltip
                    tip={insertSegmentMarkerTooltipText}
                    showDisabled={!props.hasRecordableDivs}
                    tipWhenDisabled={{
                        l10nKey:
                            "EditTab.Toolbox.TalkingBookTool.NeedCursorInRecordableThingDisabledTip",
                    }}
                    {...commonTooltipProps}
                >
                    <BloomButton
                        id="insert-segment-marker-button"
                        iconBeforeText={React.createElement(
                            InsertSegmentMarkerIcon,
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
                    showDisabled={!props.hasAudio}
                    tipWhenDisabled={
                        (!props.hasRecordableDivs && {
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.NeedCursorInRecordableThingDisabledTip",
                        }) ||
                        ""
                    }
                    {...commonTooltipProps}
                >
                    <Typography variant="h2">
                        <LocalizedString
                            l10nKey={
                                "EditTab.Toolbox.TalkingBookTool.RecordingMode"
                            }
                        >
                            Recording Mode
                        </LocalizedString>
                    </Typography>
                    <RadioGroup
                        value={props.recordingMode}
                        onChange={(
                            event: React.ChangeEvent<HTMLInputElement>,
                        ) =>
                            props.setRecordingMode(
                                RecordingMode[
                                    (event.target as HTMLInputElement).value
                                ],
                            )
                        }
                    >
                        <BloomTooltip
                            tip={{
                                l10nKey:
                                    "EditTab.Toolbox.TalkingBookTool.RecordingModeSentenceTip",
                            }}
                            showDisabled={
                                !props.hasRecordableDivs ||
                                // we don't allow you to go from textbox to sentence mode if you have audio
                                props.hasAudio
                            }
                            tipWhenDisabled={{
                                l10nKey: `EditTab.Toolbox.TalkingBookTool.RecordingModeDisabledBecauseHasAudioTip`,
                            }}
                            {...commonTooltipProps}
                        >
                            <MuiRadio
                                disabled={
                                    !props.hasRecordableDivs ||
                                    // we don't allow you to go from textbox to sentence mode if you have audio
                                    //props.hasAudio
                                    props.haveACurrentTextboxModeRecording
                                }
                                value={RecordingMode.Sentence}
                                label="By Sentence"
                                l10nKey="EditTab.Toolbox.TalkingBookTool.RecordingModeSentence"
                            />
                        </BloomTooltip>
                        <BloomTooltip
                            showDisabled={!props.hasRecordableDivs}
                            tip={{
                                l10nKey:
                                    "EditTab.Toolbox.TalkingBookTool.RecordingModeTextBoxTip",
                            }}
                            {...commonTooltipProps}
                        >
                            <RequiresSubscriptionAdjacentIconWrapper featureName="WholeTextBoxAudio">
                                <MuiRadio
                                    disabled={!props.hasRecordableDivs}
                                    value={RecordingMode.TextBox}
                                    label="By Whole Text Box"
                                    l10nKey="EditTab.Toolbox.TalkingBookTool.RecordingModeTextBox"
                                />
                            </RequiresSubscriptionAdjacentIconWrapper>
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
                                "EditTab.Toolbox.TalkingBookTool.ImportRecordingTip",
                        }}
                        tipWhenDisabled={{
                            l10nKey:
                                "EditTab.Toolbox.TalkingBookTool.ImportRecordingDisabledTip",
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
                </div>
                <Divider />
                <BloomTooltip
                    css={css`
                        z-index: 1002; // has to be above the disableOverlay because this is the one control we don't want to disable in show playback order mode
                    `}
                    tip={{
                        l10nKey:
                            "EditTab.Toolbox.TalkingBookTool.ShowPlaybackOrderTip",
                    }}
                    {...commonTooltipProps}
                >
                    <BloomSwitch
                        size="small"
                        // disabled={!props.hasRecordableDivs}
                        checked={props.inShowPlaybackOrderMode}
                        onChange={() =>
                            props.setShowPlaybackOrder(
                                !props.inShowPlaybackOrderMode,
                            )
                        }
                        l10nKey="EditTab.Toolbox.TalkingBookTool.ShowPlaybackOrder"
                        highlightWhenChecked={true}
                    />
                </BloomTooltip>
                <BloomSwitch
                    size="small"
                    checked={props.showingImageDescriptions}
                    onChange={() =>
                        props.setShowingImageDescriptions(
                            !props.showingImageDescriptions,
                        )
                    }
                    highlightWhenChecked={true}
                    l10nKey="EditTab.Toolbox.TalkingBookTool.ShowImageDescriptions"
                />
            </TriangleCollapse>
        </ThemeProvider>
    );
};
