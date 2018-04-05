import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps, Label } from "../../../react_components/l10n";
import { RadioGroup, Radio } from "../../../react_components/radio";
import axios from "axios";
import { ToolBox, ITool } from "../toolbox";
import Slider from "rc-slider";
import AudioRecording from "../talkingBook/audioRecording";

interface IVideoState {
    recording: boolean;
    countdown: number;
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

// This react class implements the UI for the video toolbox.
// Note: this file is included in toolboxBundle.js because webpack.config says to include all
// tsx files in bookEdit/toolbox.
// The toolbox is included in the list of tools because of the one line of immediately-executed code
// which passes an instance of VideoTool to ToolBox.registerTool().
export class VideoToolControls extends React.Component<{}, IVideoState> {
    constructor() {
        super({});
        this.state = { recording: false, countdown: 0 };
    }

    videoStream: MediaStream;
    chunks: Blob[];
    mediaRecorder: MediaRecorder;

    public render() {
        return (
            <div className="videoBody">
                <div className={"button-label-wrapper" + (this.state.recording ? "" : " disabled")} id="videoOuterWrapper">
                    <div id="videoPlayAndLabelWrapper">
                        <div className="videoButtonWrapper">
                            <button id="videoToggleRecording" className={"video-button ui-button enabled"
                                + (this.state.recording ? " recording" : "")}
                                onClick={() => this.toggleRecording()} />
                        </div>
                    </div>
                </div>
                <div id="videoMonitorWrapper"><video id="videoMonitor" autoPlay></video></div>
            </div>
        );
    }

    public turnOnVideo() {
        const constraints = { video: true, audio: true };
        // ((navigator.mediaDevices) as any).getUserMedia(constraints, this.startMonitoring, this.errorCallback);
        // works in FF 59 and Chrome. Not in Bloom.
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
    errorCallback(reason) {
        // something wrong! Developers note: Bloom and Firefox cannot both use it, so be careful about
        // "open in browser".
        alert("Could not access video camera...is something else using it? Details: " + reason);
    }

    // callback from getUserMedia when it succeeds; gives us a stream we can monitor and record from.
    startMonitoring(stream: MediaStream) {
        this.videoStream = stream;
        const videoMonitor = document.getElementById("videoMonitor") as HTMLVideoElement;
        videoMonitor.srcObject = stream;
    }

    // Called when the record button is clicked...depending on the current state it either starts
    // or ends the recording.
    toggleRecording() {
        if (!this.videoStream) {
            return;
        }
        var wasRecording = this.state.recording; // technically we should get this from prevState in the following function
        this.setState(prevState => { return { recording: !prevState.recording }; });
        if (wasRecording) {
            // triggers all the interesting behavior defined in onstop below.
            this.mediaRecorder.stop();
            return;
        }
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

    public static setup(root): VideoToolControls {
        return ReactDOM.render(
            <VideoToolControls />,
            root
        );
    }
}

export class VideoTool implements ITool {
    reactControls: VideoToolControls;
    makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "videoBody");
        this.reactControls = VideoToolControls.setup(root);
        return root as HTMLDivElement;
    }
    isAlwaysEnabled(): boolean {
        return false;
    }
    beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }
    showTool() {
        this.reactControls.turnOnVideo();
    }
    hideTool() {
        this.reactControls.turnOffVideo();
    }

    id(): string {
        return "video";
    }
    // required for ITool interface
    hasRestoredSettings: boolean;
    /* tslint:disable:no-empty */ // We need these to implement the interface, but don't need them to do anything.
    configureElements(container: HTMLElement) { }
    finishToolLocalization(pane: HTMLElement) { }
    updateMarkup() { }
    /* tslint:enable:no-empty */
}
