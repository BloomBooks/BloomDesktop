/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useState, useContext } from "react";
import PlayIcon from "@mui/icons-material/PlayCircleFilledWhite";
import PauseIcon from "@mui/icons-material/PauseCircleFilled";
import SkipPreviousIcon from "@mui/icons-material/SkipPrevious";
import SaveIcon from "@mui/icons-material/Save";
import RecordIcon from "@mui/icons-material/RadioButtonChecked";
import { useDebounce } from "use-debounce";
import {
    PublishPanel,
    HelpGroup,
    SettingsPanel
} from "../commonPublish/PublishScreenBaseComponents";
import ReactDOM = require("react-dom");
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import {
    darkTheme,
    kBloomBlue,
    kBloomBlue50Transparent,
    lightTheme
} from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { useSubscribeToWebSocketForStringMessage } from "../../utils/WebSocketManager";
import {
    getBoolean,
    post,
    useApiBoolean,
    useApiState,
    useApiString,
    useWatchBooleanEvent
} from "../../utils/bloomApi";
import HelpLink from "../../react_components/helpLink";
import { Link } from "../../react_components/link";
import { RequiresBloomEnterpriseDialog } from "../../react_components/requiresBloomEnterprise";
import { PublishProgressDialog } from "../commonPublish/PublishProgressDialog";
import { useL10n } from "../../react_components/l10nHooks";
import { ProgressState } from "../commonPublish/PublishProgressDialogInner";
import BloomButton from "../../react_components/bloomButton";
import {
    Button,
    CircularProgress,
    Step,
    StepContent,
    StepLabel,
    Typography
} from "@mui/material";
import { kBloomRed } from "../../utils/colorUtils";
import { SimplePreview } from "./simplePreview";
import { AudioVideoOptionsGroup } from "./AudioVideoOptionsGroup";
import { Div, Span } from "../../react_components/l10nComponents";
import { ErrorBox, NoteBox } from "../../react_components/boxes";
import { useEffect } from "react";
import { isLinux } from "../../utils/isLinux";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import { EmbeddedProgressDialog } from "../../react_components/Progress/ProgressDialog";
import { BloomStepper } from "../../react_components/BloomStepper";

export const PublishAudioVideo = () => {
    // When the user changes some features, included languages, etc., we
    // need to rebuild the book and re-run all of our Bloom API queries.
    // This requires a hard-reset of the whole screen, which we do by
    // incrementing a `key` prop on the core of this screen.
    const [keyForReset, setKeyForReset] = useState(0);

    if (isLinux()) {
        return (
            <div
                css={css`
                    padding: 20px;
                `}
            >
                <ErrorBox>
                    <div>
                        <Div
                            css={css`
                                font-style: italic;
                                font-weight: bold;
                            `}
                            l10nKey="PublishTab.RecordVideo.NotOnLinux"
                        >
                            Not available on Linux
                        </Div>
                        <Div l10nKey="PublishTab.RecordVideo.ApologiesForNoLinux">
                            This feature is available only on the Windows
                            version of Bloom. We apologize for the
                            inconvenience.
                        </Div>
                    </div>
                </ErrorBox>
            </div>
        );
    }
    return (
        <PublishAudioVideoInternalInternal
            key={keyForReset}
            onReset={() => {
                setKeyForReset(keyForReset + 1);
            }}
        />
    );
};

const landscapeWidth = 400;

/// What BloomPlayer reportBookProperties sends.
interface IBookProps {
    landscape: boolean;
    canRotate: boolean;
    hasActivities: boolean;
    hasAnimation: boolean;
}

interface IAudioVideoSettings {
    format: string;
    pageTurnDelay: number;
    motion: boolean;
    pageRange: number[]; // two numbers! Or possibly zero, meaning the whole book
}

const PublishAudioVideoInternalInternal: React.FunctionComponent<{
    onReset: () => void;
}> = props => {
    const inStorybookMode = useContext(StorybookContext);
    const heading = useL10n(
        "Creating Digital Book",
        "PublishTab.Android.Creating"
    );
    const configAndPreview = useL10n(
        "Configure &amp; Preview",
        "PublishTab.RecordVideo.ConfigureAndPreview"
    );
    const makeRecording = useL10n(
        "Make Recording",
        "PublishTab.RecordVideo.MakeRecording"
    );
    const checkRecording = useL10n(
        "Check Recording",
        "PublishTab.RecordVideo.CheckRecording"
    );
    const save = useL10n("Save", "Common.Save");
    const [closePending, setClosePending] = useState(false);
    const [avSettings, setAvSettings] = useApiState<IAudioVideoSettings>(
        "publish/av/settings",
        {
            format: "facebook",
            pageTurnDelay: 3,
            motion: false,
            pageRange: []
        }
    );

    const recording = useWatchBooleanEvent(false, "recordVideo", "recording");

    const isLicenseOK = useWatchBooleanEvent(
        true,
        "recordVideo",
        "publish/licenseOK"
    );

    const [debouncedPageTurnDelay] = useDebounce(
        avSettings.pageTurnDelay,
        1000
    );

    const [progressState, setProgressState] = useState(ProgressState.Working);
    const [activeStep, setActiveStep] = useState(0);
    const [isScalingActive] = useApiBoolean(
        "publish/av/isScalingActive",
        false
    );
    const gotRecording = useWatchBooleanEvent(false, "recordVideo", "ready");

    const [hasActivities] = useApiBoolean("publish/av/hasActivities", false);
    React.useEffect(() => {
        if (activeStep < 2 && gotRecording) {
            setActiveStep(2);
        }
        if (activeStep >= 2 && !gotRecording) {
            setActiveStep(1);
        }
    }, [gotRecording]);

    // bookUrl is expected to be a normal, well-formed URL.
    // (that is, one that you can directly copy/paste into your browser and it would work fine)
    const [bookUrl, setBookUrl] = useState(
        inStorybookMode
            ? window.location.protocol +
                  "//" +
                  window.location.host +
                  "/templates/Sample Shells/The Moon and the Cap" // Enhance: provide an actual bloomd in the source tree
            : // otherwise, wait for the websocket to deliver a url when the c# has finished creating the bloomd.
              //BloomPlayer recognizes "working" as a special value; it will show some spinner or some such.
              "working"
    );

    const [defaultLandscape] = useApiBoolean(
        "publish/bloompub/defaultLandscape",
        false
    );

    const [useOriginalPageSize, setUseOriginalPageSize] = useApiBoolean(
        "publish/av/shouldUseOriginalPageSize",
        false
    );

    const [playing, setPlaying] = useState(false);
    useSubscribeToWebSocketForStringMessage(
        "publish-bloompub",
        "bloomPubPreview",
        url => {
            setBookUrl(url);
        }
    );
    const pathToOutputBrowser = inStorybookMode ? "./" : "../../";

    const sendMessageToPlayer = (msg: any) => {
        const preview = document.getElementById(
            "simple-preview"
        ) as HTMLIFrameElement;
        msg.messageType = "control";
        preview.contentWindow?.postMessage(JSON.stringify(msg), "*");
    };
    const play = () => {
        sendMessageToPlayer({ play: true });
        setPlaying(true);
    };
    const pause = () => {
        sendMessageToPlayer({ pause: true });
        setPlaying(false);
    };
    const reset = () => {
        sendMessageToPlayer({ pause: true });
        sendMessageToPlayer({ reset: true });
        setPlaying(false);
    };
    const activitiesSkipped = useL10n(
        "Activities will be skipped",
        "PublishTab.RecordVideo.ActivitiesSkipped"
    );
    const isPauseButtonDisabled = !playing;
    useEffect(() => {
        const listener = data => {
            // something sends us an empty message, which we haven't figured out, but know we can ignore
            if (!data || !data.data || data.data.length === 0) {
                return;
            }

            try {
                const msg = JSON.parse(data.data);
                if (msg.messageType == "playbackComplete") {
                    pause();
                }
            } catch (ex) {
                // just ignore it
            }
        };
        window.addEventListener("message", listener);
        return () => {
            window.removeEventListener("message", listener);
        };
    }, []);
    // The param is added because, if anything has changed that forces us to re-render
    // the iframe with a different URL, we need to have the latest settings, in case new
    // ones have been transmitted from the player to the backend. Otherwise, the player
    // would be reset to the value we previously retrieved. With a param that includes
    // any variable element that contributes to the preview URL, we will re-run the query
    // any time it changes. (The actual content of the param is ignored in the backend.)
    const videoSettings = useApiString(
        "publish/av/videoSettings?regen=" + avSettings.pageTurnDelay,
        ""
    );
    let videoSettingsParam = "";
    if (videoSettings) {
        // videoSettings is sent as a url-encoded JSON string, which is exactly what we
        // want. But infuriatingly, axios decides to decode it into an object. If that
        // happens, we have to convert it back to URL-encoded stringified JSON.
        // Possible future issue: should we apply encodeURLComponent to the stringified JSON?
        // I know axios converts the string to an object, don't think it decodes it. No real way to test
        // at present, because the JSON data is a language code, a number, and property names,
        // so nothing is changed by URL encoding it. If we get some data in there that might
        // really need URL-encoding, should test whether we need to do some encoding here.
        if (typeof videoSettings === "string") {
            videoSettingsParam = "&videoSettings=" + videoSettings;
        } else {
            videoSettingsParam =
                "&videoSettings=" + JSON.stringify(videoSettings);
        }
    }
    let pageRangeSetting = "";
    if (avSettings.pageRange.length == 2) {
        pageRangeSetting = `&start-page=${
            avSettings.pageRange[0]
        }&autoplay-count=${avSettings.pageRange[1] -
            avSettings.pageRange[0] +
            1}`;
    }
    const recordingVideo = avSettings.format != "mp3";
    const circleHeight = "0.88rem";
    const blurbClasses = `
    max-width: ${landscapeWidth}px;
    margin-bottom:5px;
    color: grey;`;
    const mainPanel = (
        <PublishPanel>
            <BloomStepper
                activeStep={activeStep}
                areStepsAlwaysEnabled={true}
                orientation="vertical"
            >
                <Step expanded={true}>
                    <StepLabel>{configAndPreview}</StepLabel>
                    <StepContent>
                        <StyledEngineProvider injectFirst>
                            <ThemeProvider theme={darkTheme}>
                                <Div
                                    css={css`
                                        ${blurbClasses}
                                    `}
                                    l10nKey="PublishTab.RecordVideo.Instructions"
                                >
                                    If your book has multiple languages or other
                                    options, you will see a row of red buttons.
                                    Use these to set up the book for recording.
                                </Div>
                                <SimplePreview
                                    // using this key ensures that the preview is regenerated when motion changes,
                                    // which would not otherwise happen because it's not part of the props
                                    key={avSettings.motion.toString()}
                                    landscape={
                                        defaultLandscape || avSettings.motion
                                    }
                                    landscapeWidth={landscapeWidth}
                                    url={
                                        pathToOutputBrowser +
                                        "bloom-player/dist/bloomplayer.htm?centerVertically=true&videoPreviewMode=true&autoplay=yes&paused=true&defaultDuration=" +
                                        debouncedPageTurnDelay +
                                        "&url=" +
                                        encodeURIComponent(bookUrl) + // Need to apply encoding to the bookUrl again as data to use it as a parameter of another URL
                                        `&independent=false&host=bloomdesktop&useOriginalPageSize=${useOriginalPageSize}&skipActivities=true&hideNavButtons=true` +
                                        videoSettingsParam +
                                        pageRangeSetting
                                    }
                                />
                                <div
                                    css={css`
                                        display: flex;
                                        width: ${landscapeWidth}px;
                                        justify-content: center;
                                    `}
                                >
                                    <Button
                                        onClick={reset}
                                        disabled={!isLicenseOK}
                                    >
                                        <SkipPreviousIcon
                                            // unfortunately this icon doesn't come in a variant with a built-in circle.
                                            // To make it match the other two we have to shrink it, make it white,
                                            // and carefully position an independent circle behind it.
                                            css={css`
                                                color: white;
                                                font-size: 1.5rem;
                                                z-index: 1;
                                            `}
                                        />
                                        <div
                                            css={css`
                                                border: ${circleHeight} solid
                                                    ${kBloomBlue};
                                                border-radius: ${circleHeight};
                                                position: absolute;
                                                top: 0.5rem;
                                                left: 1.1rem;
                                            `}
                                        ></div>
                                    </Button>
                                    {playing ? (
                                        <CircularProgress
                                            css={css`
                                                margin-top: 8px;
                                                margin-left: 19px;
                                                margin-right: 19px;
                                            `}
                                            size="1.6rem"
                                        ></CircularProgress>
                                    ) : (
                                        <Button
                                            onClick={play}
                                            disabled={!isLicenseOK}
                                        >
                                            <PlayIcon
                                                css={css`
                                                    color: ${kBloomBlue};
                                                    font-size: 2rem !important;
                                                `}
                                            />
                                        </Button>
                                    )}
                                    <Button
                                        onClick={pause}
                                        disabled={
                                            isPauseButtonDisabled ||
                                            !isLicenseOK
                                        }
                                    >
                                        <PauseIcon
                                            css={css`
                                                color: ${isPauseButtonDisabled
                                                    ? kBloomBlue50Transparent
                                                    : kBloomBlue};
                                                font-size: 2rem !important;
                                            `}
                                        />
                                    </Button>
                                </div>
                            </ThemeProvider>
                        </StyledEngineProvider>
                    </StepContent>
                </Step>
                <Step expanded={true} disabled={false}>
                    <StepLabel onClick={() => setActiveStep(1)}>
                        {makeRecording}
                    </StepLabel>
                    <StepContent
                        css={css`
                            .MuiButtonBase-root {
                                background-color: ${kBloomRed} !important;
                            }
                        `}
                    >
                        <Div
                            css={css`
                                ${blurbClasses}
                            `}
                            l10nKey="PublishTab.RecordVideo.WillOpenRecordingWindow"
                        >
                            This will open a window and play the selected pages.
                            Bloom will record it to match the “Format” option in
                            the upper right of this screen. Don't disturb this
                            window while the recording is in progress!
                        </Div>
                        {isScalingActive && recordingVideo && (
                            <ErrorBox>
                                <div>
                                    <Div
                                        css={css`
                                            font-style: italic;
                                            font-weight: bold;
                                        `}
                                        l10nKey="PublishTab.RecordVideo.DisableScaling"
                                    >
                                        Disable Display Scaling
                                    </Div>
                                    <p
                                        css={css`
                                            margin: 0;
                                        `}
                                    >
                                        <Span l10nKey="PublishTab.RecordVideo.ChangeScale100">
                                            Please change your display scaling
                                            to 100% while making videos.
                                        </Span>{" "}
                                        <Link
                                            css={css`
                                                text-decoration: underline !important;
                                                text-decoration-color: white !important;
                                            `}
                                            l10nKey="PublishTab.RecordVideo.DisplaySettings"
                                            onClick={() =>
                                                post(
                                                    "publish/av/displaySettings"
                                                )
                                            }
                                        >
                                            Open Display Settings
                                        </Link>
                                    </p>
                                </div>
                            </ErrorBox>
                        )}
                        <BloomButton
                            enabled={
                                !recording && isLicenseOK && !isScalingActive
                            }
                            l10nKey="PublishTab.RecordVideo.Record"
                            l10nComment="'Record' as in 'Record a video recording'"
                            clickApiEndpoint="publish/av/recordVideo"
                            iconBeforeText={
                                <RecordIcon
                                    css={css`
                                        color: white;
                                    `}
                                />
                            }
                        >
                            Record
                        </BloomButton>
                    </StepContent>
                </Step>
                <Step expanded={true} disabled={false}>
                    <StepLabel>{checkRecording}</StepLabel>
                    <StepContent>
                        <Div
                            css={css`
                                ${blurbClasses}
                            `}
                            l10nKey="PublishTab.RecordVideo.WillOpenProgram"
                        >
                            This will open the program on your computer that is
                            associated with this file type.
                        </Div>
                        <BloomButton
                            enabled={gotRecording && isLicenseOK}
                            l10nKey="PublishTab.RecordVideo.Play"
                            clickApiEndpoint="publish/av/playVideo"
                            iconBeforeText={
                                <PlayIcon
                                    css={css`
                                        color: white;
                                    `}
                                />
                            }
                        >
                            Play Recording
                        </BloomButton>
                    </StepContent>
                </Step>
                <Step
                    expanded={true}
                    disabled={false}
                    onClick={() => setActiveStep(3)}
                >
                    <StepLabel>{save}</StepLabel>
                    <StepContent>
                        <BloomButton
                            enabled={gotRecording && isLicenseOK}
                            l10nKey="PublishTab.Save"
                            clickApiEndpoint="publish/av/saveVideo"
                            iconBeforeText={
                                <SaveIcon
                                    css={css`
                                        color: white;
                                    `}
                                />
                            }
                        >
                            Save...
                        </BloomButton>
                    </StepContent>
                </Step>
            </BloomStepper>
        </PublishPanel>
    );

    const optionsPanel = (
        <SettingsPanel>
            <AudioVideoOptionsGroup
                pageTurnDelay={avSettings.pageTurnDelay}
                onSetPageTurnDelay={(n: number) =>
                    setAvSettings({ ...avSettings, pageTurnDelay: n })
                }
                format={avSettings.format}
                onFormatChanged={(f: string) => {
                    setAvSettings({ ...avSettings, format: f });
                    getBoolean(
                        "publish/av/shouldUseOriginalPageSize",
                        value => {
                            setUseOriginalPageSize(value);
                        }
                    );
                }}
                pageRange={avSettings.pageRange}
                onSetPageRange={(range: number[]) =>
                    setAvSettings({ ...avSettings, pageRange: range })
                }
                motion={avSettings.motion}
                onMotionChange={m => {
                    setAvSettings({
                        ...avSettings,
                        motion: m!
                    });
                    // Will restart due to regenerating, we want the controls to show not playing.
                    pause();
                }}
            ></AudioVideoOptionsGroup>

            {/* push everything to the bottom */}
            <div
                css={css`
                    margin-top: auto;
                `}
            />
            {hasActivities && <NoteBox>{activitiesSkipped}</NoteBox>}
            <HelpGroup>
                <HelpLink
                    l10nKey="PublishTab.RecordVideo.OverviewHelpLink"
                    helpId="Tasks/Publish_tasks/Create_audio_or_video_of_book.htm"
                >
                    Publishing Audio or Video Books
                </HelpLink>
                <HelpLink
                    l10nKey="PublishTab.TasksOverview"
                    helpId="Tasks/Publish_tasks/Publish_tasks_overview.htm"
                >
                    Publish tab tasks overview
                </HelpLink>
            </HelpGroup>
        </SettingsPanel>
    );

    return (
        <Typography
            component={"div"}
            css={css`
                height: 100%;
            `}
        >
            <RequiresBloomEnterpriseDialog />
            <PublishScreenTemplate
                bannerTitleEnglish="Publish as Audio or Video"
                bannerTitleL10nId="PublishTab.RecordVideo.BannerTitle"
                bannerDescriptionMarkdown="Create video files that you can upload to sites like Facebook and YouTube. You can also make videos to share with people who use inexpensive “feature phones” and even audio-only files for listening."
                bannerDescriptionL10nId="PublishTab.RecordVideo.BannerDescription"
                optionsPanelContents={optionsPanel}
            >
                {mainPanel}
            </PublishScreenTemplate>
            {/* In storybook, there's no bloom backend to run the progress dialog */}
            {inStorybookMode || (
                <PublishProgressDialog
                    heading={heading}
                    startApiEndpoint="publish/av/updatePreview"
                    webSocketClientContext="publish-bloompub"
                    progressState={progressState}
                    setProgressState={setProgressState}
                    closePending={closePending}
                    setClosePending={setClosePending}
                    onUserStopped={() => {
                        setClosePending(true);
                    }}
                />
            )}
            <EmbeddedProgressDialog id="avPublish" />
        </Typography>
    );
};

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
// WE could now switch to doing this with ReactControl. But it's easier if all the publish HTML
// panels work the same way.
if (document.getElementById("PublishAudioVideo")) {
    ReactDOM.render(
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <PublishAudioVideo />
            </ThemeProvider>
        </StyledEngineProvider>,
        document.getElementById("PublishAudioVideo")
    );
}
