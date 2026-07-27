import { css, ThemeProvider } from "@emotion/react";
import { FunctionComponent, useEffect, useRef, useState } from "react";
import {
    toolboxMenuPopupTheme,
    toolboxTheme,
} from "../../../bloomMaterialUITheme";
import { Span } from "../../../react_components/l10nComponents";
import BloomButton from "../../../react_components/bloomButton";
import { Link } from "../../../react_components/link";
import { IAudioRecorder } from "./IAudioRecorder";
import { Status, TalkingBookUiState } from "./TalkingBookUiState";
import {
    kBloomBuff,
    kBloomPanelBackground,
    kBloomYellow,
} from "../../../utils/colorUtils";
import { RecordingMode } from "./recordingMode";
import { TalkingBookAdvancedSection } from "./talkingBookAdvancedSection";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { Menu } from "@mui/material";
import { LocalizableMenuItem } from "../../../react_components/localizableMenuItem";
import { useMountEffect } from "../../../utils/useMountEffect";
import { useWatchApiData } from "../../../utils/bloomApi";

const RecordingMeterAndText: FunctionComponent<{
    inputDevice: { iconSrc: string; title: string } | undefined;
    shouldDisplay: boolean;
    audioDevices: string[];
    audioRecorder: IAudioRecorder;
    uiLanguage: string;
}> = (props) => {
    // this useRef and useMountEffect deal with setting the engine's
    // level canvas to be the canvas element provided in this component.
    // Since the logic for drawing to the canvas exists in the engine,
    // it needs access to the canvas, so on mount we pass the canvas element
    // to the engine, and on unmount we remove it from the engine.
    const meterCanvasRef = useRef<HTMLCanvasElement>(null);

    useMountEffect(() => {
        props.audioRecorder.setLevelCanvas(meterCanvasRef.current);

        return () => props.audioRecorder.setLevelCanvas(null);
    });

    // anchors the MUI menu to the audio device image, so that it appears
    // beneath the image when opened.
    const [menuAnchor, setMenuAnchor] = useState<HTMLElement>();

    return (
        <>
            <div
                css={css`
                    color: ${kBloomBuff};
                `}
            >
                <span>{`${new Intl.NumberFormat(props.uiLanguage).format(1)}) `}</span>
                <Span l10nKey="EditTab.Toolbox.TalkingBookTool.CheckSettingsLabel">
                    Check that you are recording into the correct device and
                    that these levels are showing blue:
                </Span>
            </div>
            <div
                css={css`
                    display: flex;
                    align-items: flex-end;
                    margin-top: 5px;
                    margin-bottom: 12px;
                `}
            >
                <BloomTooltip
                    tip={props.inputDevice ? props.inputDevice.title : ""}
                    placement="bottom-end"
                    slotProps={{
                        tooltip: {
                            sx: { width: "auto", maxWidth: "165px" },
                        },
                    }}
                >
                    <BloomButton
                        l10nKey=""
                        variant="text"
                        hasText={false}
                        enabledImageFile={
                            props.inputDevice
                                ? props.inputDevice.iconSrc
                                : "/bloom/bookEdit/toolbox/talkingBook/microphone.svg"
                        }
                        css={css`
                            min-width: 0;
                            width: 30px;
                            height: 30px;
                            img {
                                width: 30px;
                                height: 30px;
                            }
                            &:hover {
                                background-color: transparent;
                            }
                        `}
                        onClickCapture={(event) => {
                            setMenuAnchor(event.currentTarget);
                            props.audioRecorder.changeInputDevice();
                        }} // BloomButton doesn't allow an event to be passed into the onClick property, so onClickCapture is used here to allow for the anchoring logic to work
                        disableRipple
                        enabled
                    ></BloomButton>
                </BloomTooltip>
                <canvas ref={meterCanvasRef} width={80} height={15}></canvas>
            </div>
            <ThemeProvider theme={toolboxMenuPopupTheme}>
                <Menu
                    anchorEl={menuAnchor}
                    open={props.shouldDisplay}
                    css={css`
                        margin-top: 1px;
                        margin-left: -2px;
                        margin-right: -13px;
                    `}
                    slotProps={{
                        paper: {
                            sx: {
                                width: "180px",
                                overflowX: "hidden",
                            },
                        },
                    }}
                    onClose={() => props.audioRecorder.closeDeviceSelectMenu()}
                >
                    {props.audioDevices.map((item, index) => (
                        <LocalizableMenuItem
                            key={index}
                            onClick={() =>
                                props.audioRecorder.setInputDevice(item)
                            }
                            // convert "Microphone (xxxx)" --> xxxx, where the final ')' is often missing (cut off somewhere upstream)
                            english={item.replace(
                                /Microphone \(([^\)]*)\)?/,
                                "$1",
                            )}
                            l10nId={null}
                            hasLeadingIconSpace={false}
                            labelCss={css`
                                font-size: 12px;
                                white-space: nowrap;
                            `}
                            css={css`
                                min-height: 20px;
                                padding-left: 5px;
                                padding-top: 3px;
                                padding-bottom: 3px;
                                margin-top: 0;
                            `}
                        />
                    ))}
                </Menu>
            </ThemeProvider>
        </>
    );
};

const TalkingBookButton: FunctionComponent<{
    status: Status;
    enabledImgFile: string;
    expectedImgFile?: string;
    activeImgFile?: string;
    size?: number;
    stepNum?: number;
    uiLanguage?: string;
    hideStepNum?: boolean;
    l10nKey: string;
    l10nText: string;
    onMouseDown?: () => void;
    onMouseUp?: () => void;
    onClickCapture?: (event) => void; // BloomButton's onClick doesn't allow for passing in an event, so this is used by the play button to handle the ctrl + click functionality
    onClick?: () => void;
}> = (props) => {
    const {
        status,
        enabledImgFile,
        expectedImgFile,
        activeImgFile,
        size,
        stepNum,
        uiLanguage,
        hideStepNum,
        l10nKey,
        l10nText,
        ...possibleEventHandlers // since the buttons have different kinds of clicking/mouse event handlers, just use whatever is given
    } = props;
    let imgFile = props.enabledImgFile;
    switch (props.status) {
        case Status.Active:
            imgFile = props.activeImgFile ?? props.enabledImgFile;
            break;
        case Status.Expected:
            imgFile = props.expectedImgFile ?? props.enabledImgFile;
            break;
        default:
            imgFile = props.enabledImgFile;
            break;
    }
    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                margin-top: 10px;
                margin-bottom: 5px;
            `}
        >
            <BloomButton
                l10nKey=""
                variant="text"
                enabled={props.status !== Status.Disabled}
                hasText={false}
                enabledImageFile={imgFile}
                disabledImageFile={imgFile}
                disableRipple
                css={css`
                    width: ${props.size ?? 40}px;
                    height: ${props.size ?? 40}px;
                    pointer-events: none; // this makes it so that you can't interact with the button when you hover over/click on the padding surrounding the button image
                    opacity: ${props.status !== Status.Disabled ? 1 : 0.4};
                    img {
                        height: ${props.size ?? 40}px;
                        width: ${props.size ?? 40}px;
                        margin-left: -20px;
                        pointer-events: auto; // make the button image the thing you can interact with
                    }
                    &:hover {
                        background-color: transparent;
                    }
                `}
                {...possibleEventHandlers}
            ></BloomButton>
            <div
                css={css`
                    color: ${props.status === Status.Expected
                        ? kBloomYellow
                        : kBloomBuff};
                `}
            >
                {!props.hideStepNum && props.stepNum && props.uiLanguage && (
                    <span>{`${new Intl.NumberFormat(props.uiLanguage).format(props.stepNum)}) `}</span>
                )}
                <Span l10nKey={props.l10nKey}>{props.l10nText}</Span>
            </div>
        </div>
    );
};

export const TalkingBookToolControls: FunctionComponent<{
    audioRecorder: IAudioRecorder;
}> = (props) => {
    // this grabs the current locale that the entire app is using, so that the step
    // numbers can be properly translated using Intl.NumberFormat
    const uiLanguage = useWatchApiData(
        "currentUiLanguage",
        "en",
        "app",
        "uiLanguageChanged",
    );

    // set the state for the entire talking book tool to be the ui state
    // interface from TalkingBookUiState.ts. Note that the engine
    // implements the interface.
    const [uiState, setUiState] = useState<TalkingBookUiState>(() =>
        props.audioRecorder.getTalkingBookUiState(),
    );

    // this useEffect is needed to pass our state setter function to the engine,
    // so that it can update the ui state and cause a rerender whenever relevant
    // data/state changes, such as the status of the buttons.
    useEffect(() => {
        return props.audioRecorder.registerStateListener((newState) => {
            setUiState(newState);
        });
    }, [props.audioRecorder]);

    return (
        <ThemeProvider theme={toolboxTheme}>
            {uiState.inShowPlaybackOrderMode && (
                <div
                    css={css`
                        z-index: 1001;
                        opacity: 0.7;
                        position: fixed;
                        top: inherit;
                        background-color: ${kBloomPanelBackground};
                        height: 100%;
                        width: calc(100% - 15px);
                    `}
                ></div>
            )}
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                    height: calc(100% - 2 * 15px);
                    padding: 15px;
                `}
            >
                <RecordingMeterAndText
                    inputDevice={uiState.inputDevice}
                    shouldDisplay={uiState.shouldShowDeviceMenu}
                    audioDevices={uiState.audioDevices}
                    audioRecorder={props.audioRecorder}
                    uiLanguage={uiLanguage}
                />
                <div
                    css={css`
                        color: ${kBloomBuff};
                    `}
                >
                    <span>{`${new Intl.NumberFormat(uiLanguage).format(2)}) `}</span>
                    <Span l10nKey="EditTab.Toolbox.TalkingBookTool.LookAtSentenceLabel">
                        Look at the highlighted text
                    </Span>
                </div>
                {/* record/speak button */}
                <TalkingBookButton
                    status={uiState.buttons.record}
                    enabledImgFile="/bloom/bookEdit/toolbox/talkingBook/record_enabled.svg"
                    expectedImgFile="/bloom/bookEdit/toolbox/talkingBook/record_expected.svg"
                    activeImgFile="/bloom/bookEdit/toolbox/talkingBook/record_active.svg"
                    stepNum={3}
                    uiLanguage={uiLanguage}
                    l10nKey="EditTab.Toolbox.TalkingBookTool.SpeakLabel"
                    l10nText="Speak"
                    onMouseDown={() => {
                        props.audioRecorder.startRecordCurrentAsync();
                    }}
                    onMouseUp={() => {
                        props.audioRecorder.endRecordCurrentAsync();
                    }}
                />
                {/* play/check button */}
                <TalkingBookButton
                    status={uiState.buttons.play}
                    enabledImgFile="/bloom/images/play_enabled.svg"
                    expectedImgFile="/bloom/bookEdit/toolbox/talkingBook/play_expected.svg"
                    activeImgFile="/bloom/bookEdit/toolbox/talkingBook/pause_yellow.svg"
                    size={45}
                    stepNum={4}
                    uiLanguage={uiLanguage}
                    hideStepNum={uiState.buttons.play === Status.Active}
                    l10nKey={
                        uiState.buttons.play === Status.Active
                            ? "Common.Pause"
                            : "EditTab.Toolbox.TalkingBookTool.CheckLabel"
                    }
                    l10nText={
                        uiState.buttons.play === Status.Active
                            ? "Pause"
                            : "Check"
                    }
                    onClickCapture={(event) => {
                        if (event.ctrlKey) {
                            props.audioRecorder.playESpeakPreview();
                        } else {
                            props.audioRecorder.togglePlayCurrentAsync();
                        }
                    }}
                />
                {/* adjust timings button, displayed only when recording the whole textbox */}
                {uiState.recordingMode === RecordingMode.TextBox && (
                    <TalkingBookButton
                        status={uiState.buttons.split}
                        enabledImgFile="/bloom/bookEdit/toolbox/talkingBook/adjustTimings.svg"
                        size={45}
                        stepNum={5}
                        uiLanguage={uiLanguage}
                        l10nKey="EditTab.Toolbox.TalkingBookTool.AdjustTimings"
                        l10nText="Adjust Timings..."
                        onClick={() => {
                            props.audioRecorder.showAdjustTimingsDialog();
                        }}
                    />
                )}
                {/* next button */}
                <TalkingBookButton
                    status={uiState.buttons.next}
                    enabledImgFile="/bloom/bookEdit/toolbox/talkingBook/next_enabled.svg"
                    expectedImgFile="/bloom/bookEdit/toolbox/talkingBook/next_expected.svg"
                    stepNum={
                        uiState.recordingMode === RecordingMode.TextBox ? 6 : 5
                    }
                    uiLanguage={uiLanguage}
                    l10nKey="EditTab.Toolbox.TalkingBookTool.NextLabel"
                    l10nText="Next"
                    onClick={() => {
                        props.audioRecorder.moveToNextAudioElement();
                    }}
                />
                {/* back/prev button */}
                <TalkingBookButton
                    status={uiState.buttons.prev}
                    enabledImgFile="/bloom/bookEdit/toolbox/talkingBook/prev_enabled.svg"
                    size={20}
                    l10nKey="EditTab.Toolbox.TalkingBookTool.Back"
                    l10nText="Back"
                    onClick={() => {
                        props.audioRecorder.moveToPrevAudioElementAsync();
                    }}
                />
                {/* clear button */}
                <TalkingBookButton
                    status={uiState.buttons.clear}
                    enabledImgFile="/bloom/bookEdit/toolbox/talkingBook/clear_enabled.svg"
                    size={20}
                    l10nKey="EditTab.Toolbox.TalkingBookTool.Clear"
                    l10nText="Clear"
                    onClick={() => {
                        props.audioRecorder.clearRecordingAsync();
                    }}
                />
                {/* listen to whole page button */}
                <TalkingBookButton
                    status={uiState.buttons.listen}
                    enabledImgFile="/bloom/bookEdit/toolbox/talkingBook/listen_enabled.svg"
                    activeImgFile="/bloom/bookEdit/toolbox/talkingBook/listen_active.svg"
                    l10nKey="EditTab.Toolbox.TalkingBookTool.Listen"
                    l10nText="Listen to the whole page"
                    onClick={() => {
                        props.audioRecorder.listenAsync();
                    }}
                />
                <TalkingBookAdvancedSection
                    recordingMode={uiState.recordingMode}
                    haveACurrentTextboxModeRecording={
                        uiState.haveACurrentTextboxModeRecording
                    }
                    setRecordingMode={(recordingMode) =>
                        props.audioRecorder.setRecordingMode(recordingMode)
                    }
                    hasAudio={uiState.hasAudio}
                    hasRecordableDivs={uiState.hasRecordableDivs}
                    handleImportRecordingClick={() =>
                        props.audioRecorder.handleImportRecordingClick()
                    }
                    insertSegmentMarker={() =>
                        props.audioRecorder.insertSegmentMarker()
                    }
                    inShowPlaybackOrderMode={uiState.inShowPlaybackOrderMode}
                    setShowPlaybackOrder={(isOn) =>
                        props.audioRecorder.setShowPlaybackOrder(isOn)
                    }
                    showingImageDescriptions={uiState.showingImageDescriptions}
                    setShowingImageDescriptions={(isOn) =>
                        props.audioRecorder.setShowingImageDescriptions(isOn)
                    }
                />
                <div
                    css={css`
                        margin-top: auto;
                        z-index: 1002;
                    `}
                >
                    <Link
                        l10nKey="Common.Help"
                        href="/bloom/api/help?topic=Tasks/Edit_tasks/Record_Audio/Talking_Book_Tool_overview.htm"
                    >
                        Help
                    </Link>
                </div>
            </div>
        </ThemeProvider>
    );
};
