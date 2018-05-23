import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps, Label } from "../../../react_components/l10n";
import { RadioGroup, Radio } from "../../../react_components/radio";
import axios from "axios";
import { ToolBox, ITool } from "../toolbox";
import Slider from "rc-slider";
import AudioRecording from "../talkingBook/audioRecording";
import { getPageFrameExports } from "../../js/bloomFrames";
import "./signLanguage.less";

// The recording process can be in one of these states:
// waiting...the initial state, returned to when stopped; top label shows "Start Recording"; stop button and second label hidden
// countdown{3,2,1}...record button has been pressed, top label shows countdown, not recording, stop button shows,
//  "press any key to cancel" shows in bottom label
// recording...top label shows "Recording", stop button shows, recording is happening, bottom label shows "press any key to stop"
// Transitions:
// Start recording button: state waiting -> countdown{3}, * -> waiting (save video if any)
// Stop button or any key: * -> waiting
interface IComponentState {
    recording: boolean;
    countdown: number;
    enabled: boolean;
    stateClass: string; // one of waiting, countdown3, countdown2, countdown1, recording
    haveRecording: boolean;
    originalExists: boolean;
}

// incomplete typescript definitions for MediaRecorder and related types.
// Can't find complete ones, so rather than just do without type checking altogether,
// I've made declarations as accurately as I can figure out for the methods we actually use.
interface BlobEvent {
    data: Blob;
}

interface MediaRecorder {
    new(source: MediaStream, options: any);
    start(): void;
    stop(): void;
    ondataavailable: (ev: BlobEvent) => void;
    onstop: () => void;
}

declare var MediaRecorder: {
    prototype: MediaRecorder;
    new(s: MediaStream, options: any): MediaRecorder;
};

// This react class implements the UI for the sign language (video) toolbox.
// Note: this file is included in toolboxBundle.js because webpack.config says to include all
// tsx files in bookEdit/toolbox.
// The toolbox is included in the list of tools because of the one line of immediately-executed code
// which passes an instance of SignLanguageTool to ToolBox.registerTool().
export class SignLanguageToolControls extends React.Component<{}, IComponentState> {
    constructor() {
        super({});
        this.state = { recording: false, countdown: 0, enabled: false, stateClass: "waiting", haveRecording: false, originalExists: false };
    }

    private videoStream: MediaStream;
    private chunks: Blob[];
    private mediaRecorder: MediaRecorder;
    private timerId: number;

    public render() {
        return (
            <div className={"signLanguageFrame " + this.state.stateClass + (this.state.enabled ? "" : " disabled")}>
                <Label l10nKey="Toolbox.SignLanguage.WhatCameraSees">Here is what your camera sees:</Label>
                <div id="videoMonitorWrapper"><video id="videoMonitor" autoPlay></video></div>
                <div className="button-label-wrapper">
                    <div id="videoPlayAndLabelWrapper">
                        <div className="videoButtonWrapper">
                            <button id="videoToggleRecording" className={"video-button ui-button"
                                + (this.state.stateClass !== "waiting" ? " started" : "")
                                + (this.state.recording ? " recordingNow" : "")
                                + (this.state.enabled ? " enabled" : " disabled")}
                                onClick={() => this.toggleRecording()} />
                            <Label className="startRecording waiting" l10nKey="Toolbox.SignLanguage.StartRecording">Start Recording</Label>
                            <span className="countdown3 countdownNumber">3</span>
                            <span className="countdown2 countdownNumber">2</span>
                            <span className="countdown1 countdownNumber">1</span>
                            <Label className="recording recordingLabel" l10nKey="Toolbox.SignLanguage.Recording">Recording</Label>
                        </div>
                    </div>
                </div>
                <div id="editOutsideWrapper" className={"videoButtonWrapper" + (this.state.haveRecording ? "" : " disabled ")}>
                    <button id="editOutsideButton" onClick={() => this.editOutside()} />
                    <Label className="commandLabel" l10nKey="Toolbox.SignLanguage.EditOutside">Edit outside of Bloom</Label>
                </div>
                <div id="restoreOriginalWrapper" className={"videoButtonWrapper" + (this.state.originalExists ? "" : " disabled ")}>
                    <button id="restoreOriginalButton" onClick={() => this.restoreOriginal()} />
                    <Label className="commandLabel" l10nKey="Toolbox.SignLanguage.RestoreOriginal">Restore Original</Label>
                </div>
                <div>
                    <button id="videoStopRecording" className={"video-button ui-button notWaiting"}
                        onClick={() => this.toggleRecording()} />
                </div>
                <Label l10nKey="Toolbox.SignLanguage.PressCancel" className="counting stopLabel">Press any key to cancel</Label>
                <Label l10nKey="Toolbox.SignLanguage.PressStop" className="recording stopLabel">Press any key to stop</Label>
            </div>
        );
    }

    private editOutside() {
        axios.post("/bloom/api/toolbox/editVideo");
    }

    private restoreOriginal() {
        axios.post("/bloom/api/toolbox/restoreOriginal");
    }

    public turnOnVideo() {
        const constraints = { video: true };
        navigator.mediaDevices.getUserMedia(constraints).then(stream => this.startMonitoring(stream))
            .catch(reason => this.errorCallback(reason));
    }

    public turnOffVideo() {
        if (this.videoStream) {
            const oldStream = this.videoStream;
            this.videoStream = null; // prevent recursive calls
            oldStream.getVideoTracks().forEach(t => t.stop());
            oldStream.getAudioTracks().forEach(t => t.stop());
        }
    }

    // callback from getUserMedia when it fails.
    private errorCallback(reason) {
        // something wrong! Developers note: Bloom and Firefox cannot both use it, so be careful about
        // "open in browser".
        alert("Could not access video camera...is something else using it? Details: " + reason);
    }

    // callback from getUserMedia when it succeeds; gives us a stream we can monitor and record from.
    private startMonitoring(stream: MediaStream) {
        this.videoStream = stream;
        const videoMonitor = document.getElementById("videoMonitor") as HTMLVideoElement;
        videoMonitor.srcObject = stream;
    }

    // Not just a normal function definition, because then when we pass it to addEventListener,
    // 'this' is not what we expect. Nor can we just pass a locally-defined function, because
    // we have to be able to remove it later.
    private onKeyPress = () => {
        this.toggleRecording();
    }

    // Called when the record or stop button is clicked, or if a key is pressed while not in the waiting state
    // ...depending on the current state it either starts or ends the recording. It works as the action function
    // for things that only stop the recording because those controls are not enabled in the waiting state.
    private toggleRecording() {
        if (!this.videoStream) {
            return;
        }
        var oldState = this.state.stateClass;
        var wasRecording = this.state.recording;
        if (wasRecording) {
            document.removeEventListener("keydown", this.onKeyPress);
            this.setState({ recording: false, stateClass: "waiting" });
            // triggers all the interesting behavior defined in onstop below.
            this.mediaRecorder.stop();
            return;
        }
        if (oldState === "waiting") {
            document.addEventListener("keydown", this.onKeyPress);
            this.setState({ stateClass: "countdown3" });
            this.timerId = window.setTimeout(() => {
                this.setState({ stateClass: "countdown2" });
                this.timerId = window.setTimeout(() => {
                    this.setState({ stateClass: "countdown1" });
                    this.timerId = window.setTimeout(() => {
                        this.setState({ stateClass: "recording", recording: true });
                        this.startRecording();
                    }, 1000);
                }, 1000);
            }, 1000);
            return;
        }
        // we're in one of the countdown states. Back to waiting.
        document.removeEventListener("keydown", this.onKeyPress);
        window.clearTimeout(this.timerId);
        this.setState({ stateClass: "waiting" });
    }
    private startRecording() {
        // OK, we want to start recording.
        this.chunks = [];
        var options = {
            // I found a couple of examples online with these rates for video/mp4 and the results
            // look reasonable. It's possible we could get useful recordings with lower rates.
            audioBitsPerSecond: 128000,
            videoBitsPerSecond: 2500000,
            mimeType: "video/mp4"
        };
        this.mediaRecorder = new MediaRecorder(this.videoStream, options);
        this.mediaRecorder.ondataavailable = e => {
            // called periodically during recording and once more with the rest of the data
            // when recording stops. So all the chunks which make up the recording come here.
            this.chunks.push(e.data);
        };
        this.mediaRecorder.onstop = () => {
            // raised when the user clicks stop and we call this.mediaRecorder.stop() above.
            var blob = new Blob(this.chunks, { type: "video/webm" });
            this.chunks = []; // enable garbage collection?
            axios.post("/bloom/api/toolbox/recordedVideo", blob, {
                headers: {
                    "Content-Type": "video/mp4"
                }
            });
            // Don't know why this is necessary, but for some reason, the stream we have is no
            // longer useful after calling mediaRecorder.stop(). The monitor freezes and
            // nothing happens when I click record. So dispose of it and start a new one.
            this.turnOffVideo();
            this.turnOnVideo();
        };

        // All set, get the actual recording going.
        this.mediaRecorder.start();
    }

    public static setup(root): SignLanguageToolControls {
        return ReactDOM.render(
            <SignLanguageToolControls />,
            root
        );
    }
}

export class SignLanguageTool implements ITool {
    private reactControls: SignLanguageToolControls;
    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "signLanguageBody");
        this.reactControls = SignLanguageToolControls.setup(root);
        return root as HTMLDivElement;
    }
    public isAlwaysEnabled(): boolean {
        return false;
    }
    public isExperimental(): boolean {
        return true;
    }
    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    public showTool() {
        // nothing to do here (if this class eventually extends our React Adaptor, this can be removed.)
    }
    public hideTool() {
        // nothing to do here (if this class eventually extends our React Adaptor, this can be removed.)
    }

    public detachFromPage() {
        // Decided NOT to remove bloom-selected here. It's harmless (only the edit stylesheet
        // does anything with it) and leaving it allows us to keep the same one selected
        // when we come back to the page. This is especially important when refreshing the
        // page after selecting or recording a video.
        const containers = ToolBox.getPage().getElementsByClassName("bloom-videoContainer");
        for (var i = 0; i < containers.length; i++) {
            containers[i].removeEventListener("click", this.containerClickListener);
        }

        this.reactControls.turnOffVideo();
    }

    public id(): string {
        return "signLanguage";
    }

    // This function is saved in a variable so we can remove the same listener we added.
    private containerClickListener: EventListener = (event: MouseEvent) => {
        // The reason for the listener: to select the current element
        const currentContainers = ToolBox.getPage().getElementsByClassName("bloom-videoContainer");
        for (var i = 0; i < currentContainers.length; i++) {
            currentContainers[i].classList.remove("bloom-selected");
        }
        var container = (event.currentTarget as HTMLElement);
        container.classList.add("bloom-selected");
        this.updateStateForSelected(container);
        // And now in most locations we want to prevent the default behavior where click starts playback.
        // This may need adjustment for zoom.
        // The idea here is that it should be possible to select a video by clicking it
        // without starting it playing. On the other hand, the playback controls should work.
        // So we prevent the default (playback-related) behavior if the click is in the area
        // that the actual controls occupy.
        // This is fairly rough (the central circle is approximated with a square, and we don't
        // try to account for the fact that it disappears once first clicked).
        // It's also fairly fragile...future versions of Gecko may not have the exact same
        // controls in the same places. But it's the best I've been able to figure out.
        const buttonRadius = 28;
        var clientRect = container.getBoundingClientRect();
        var x = event.clientX - clientRect.left;
        var y = event.clientY - clientRect.top;
        if (y < container.offsetHeight - 40 // above the control bar across the bottom
            && (y < container.offsetHeight / 2 - buttonRadius // above the play button
                || y > container.offsetHeight / 2 + buttonRadius // below the play button
                || x < container.offsetWidth / 2 - buttonRadius // left of play button
                || x > container.offsetWidth / 2 + buttonRadius)) {// right of play button
            event.preventDefault();
        }
    }
    // required for ITool interface
    public hasRestoredSettings: boolean;
    /* tslint:disable:no-empty */ // We need these to implement the interface, but don't need them to do anything.
    public configureElements(container: HTMLElement) { }
    public finishToolLocalization(pane: HTMLElement) { }
    public updateMarkup() { }
    /* tslint:enable:no-empty */

    public newPageReady() {
        const page = ToolBox.getPage();
        const containers = page.getElementsByClassName("bloom-videoContainer");
        if (containers.length === 0) {
            if (this.reactControls.state.enabled) {
                this.reactControls.turnOffVideo();
                this.reactControls.setState({ enabled: false });
            }
        } else {
            // We want one video container to be selected, so pick the first.
            // If one is already marked selected, presumably from a previous use of this page,
            // we'll leave that one active.
            const selectedVideos = page.getElementsByClassName("bloom-videoContainer bloom-selected");
            if (selectedVideos.length === 0) {
                containers[0].classList.add("bloom-selected");
                this.updateStateForSelected(containers[0]);
            } else {
                this.updateStateForSelected(selectedVideos[0]);
            }
            for (var i = 0; i < containers.length; i++) {
                const container = containers[i];
                // UpdateMarkup is called fairlyfrequently. Not sure what effect having
                // the same listener attached multiple times might have, so play safe by
                // removing it before adding.
                container.removeEventListener("click", this.containerClickListener);
                container.addEventListener("click", this.containerClickListener);
            }
            // we turn it off when we leave a page, so even if we already have enabled:true,
            // we need to turn it on for this page now we know there is something to record.
            this.reactControls.turnOnVideo();
            if (!this.reactControls.state.enabled) {
                this.reactControls.setState({ enabled: true });
            }
        }
    }

    private updateStateForSelected(container: Element) {
        var videos = container.getElementsByTagName("video");
        if (videos.length === 0) {
            this.reactControls.setState({ haveRecording: false, originalExists: false });
            return;
        }
        var sources = videos[0].getElementsByTagName("source");

        if (sources.length === 0) {
            this.reactControls.setState({ haveRecording: false, originalExists: false });
            return;
        }

        var src = sources[0].getAttribute("src");
        if (!src) {
            this.reactControls.setState({ haveRecording: false, originalExists: false });
            return;
        }
        axios.get("/bloom/api/toolbox/fileExists?filename=" + src).then(result => {
            this.reactControls.setState({ haveRecording: (result.data) });
        });
        axios.get("/bloom/api/toolbox/fileExists?filename=" + src.replace(".mp4", ".orig")).then(result => {
            this.reactControls.setState({ originalExists: (result.data) });
        });
    }
}
