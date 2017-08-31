import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import ProgressBox from "../../react_components/progressBox";
import BloomButton from "../../react_components/bloomButton";
import Option from "../../react_components/option";
import HelpLink from "../../react_components/helpLink";
import HtmlHelpLink from "../../react_components/htmlHelpLink";
import { H1, H2, LocalizableElement, IUILanguageAwareProps, P } from "../../react_components/l10n";
import WebSocketManager from "../../utils/WebSocketManager";

interface IComponentState {
    method: string;
    stateId: string;
}

// This is a screen of controls that gives the user instructions and controls
// for pushing a book to a connected Android device running Bloom Reader.
class AndroidPublishUI extends React.Component<IUILanguageAwareProps, IComponentState> {
    isLinux: boolean;
    constructor(props) {
        super(props);

        this.isLinux = this.getIsLinuxFromUrl();
        this.state = { stateId: "stopped", method: "wifi" };

        // enhance: For some reason setting the callback to "this.handleUpdate" calls handleUpdate()
        // with "this" set to the button, not this overall control.
        // I don't quite have my head around this problem yet, but this oddity fixes it.
        // See https://medium.com/@rjun07a/binding-callbacks-in-react-components-9133c0b396c6
        this.handleUpdateState = this.handleUpdateState.bind(this);

        WebSocketManager.addListener(event => {
            var e = JSON.parse(event.data);
            if (e.id === "publish/android/state") {
                this.handleUpdateState(e.payload);
            }
        });
        WebSocketManager.setCloseId("closeAndroidUISocket");

        axios.get("/bloom/api/publish/android/method").then(result => {
            this.setState({ method: result.data });
        });
    }

    public componentDidMount() {
        window.addEventListener("beforeunload", this.componentCleanup);
    }

    // Apparently, we have to rely on the window event when closing or refreshing the page.
    // componentWillUnmount will not get called in those cases.
    public componentWillUnmount() {
        this.componentCleanup();
        window.removeEventListener("beforeunload", this.componentCleanup);
    }

    componentCleanup() {
        axios.post("/bloom/api/publish/android/cleanup");
    }

    handleUpdateState(s: string): void {
        this.setState({ stateId: s });
        //console.log("this.state is " + JSON.stringify(this.state));
    }

    getIsLinuxFromUrl(): boolean {
        let searchString = window.location.search;
        let i = searchString.indexOf("isLinux=");
        if (i >= 0) {
            return searchString.substr(i + "isLinux=".length, 4) === "true";
        }
    }

    render() {
        let self = this;

        return (
            <div>
                <H1 l10nKey="Publish.Android.Method"
                    l10nComment="There are several methods for pushing a book to android. This is the heading above the chooser.">
                    Method
                </H1>
                <select className={`method-option ${this.state.method}-method-option`}
                    disabled={this.state.stateId !== "stopped"} value={this.state.method} onChange={
                        (event) => {
                            self.setState({ method: event.target.value });
                            axios.post("/bloom/api/publish/android/method", event.target.value,
                                { headers: { "Content-Type": "text/plain" } });
                        }}>
                    <Option l10nKey="Publish.Android.ChooseWifi"
                        className="method-option wifi-method-option"
                        value="wifi">
                        Serve on WiFi Network
                    </Option>
                    <Option l10nKey="Publish.Android.ChooseUSB"
                        className="method-option usb-method-option" value="usb">
                        Send over USB Cable
                    </Option>
                    <Option l10nKey="Publish.Android.ChooseFile"
                        className="method-option file-method-option" value="file">
                        Save Bloom Reader File
                    </Option>
                </select>

                <p />
                <H1 l10nKey="Publish.Android.Control"
                    l10nComment="This is the heading above various buttons that control the publishing of the book to Android.">
                    Control
                </H1>

                {this.state.method === "wifi" &&
                    <div>
                        <BloomButton l10nKey="Publish.Android.Wifi.Start"
                            l10nComment="Button that tells Bloom to begin offering this book on the wifi network."
                            enabled={this.state.stateId === "stopped"}
                            clickEndpoint="publish/android/wifi/start">
                            Start Serving
                        </BloomButton>
                        <BloomButton l10nKey="Publish.Android.Wifi.Stop"
                            l10nComment="Button that tells Bloom to stop offering this book on the wifi network."
                            enabled={this.state.stateId === "ServingOnWifi"}
                            clickEndpoint="publish/android/wifi/stop">
                            Stop Serving
                        </BloomButton>
                    </div>
                }

                {this.state.method === "usb" &&
                    <div>
                        <BloomButton l10nKey="Publish.Android.Usb.Start"
                            l10nComment="Button that tells Bloom to send the book to a device via USB cable."
                            enabled={this.state.stateId === "stopped"}
                            clickEndpoint="publish/android/usb/start"
                            hidden={this.isLinux}>
                            Connect with USB cable
                        </BloomButton>

                        <BloomButton l10nKey="Publish.Android.Usb.Stop"
                            enabled={this.state.stateId === "UsbStarted"}
                            clickEndpoint="publish/android/usb/stop">
                            Stop Trying
                        </BloomButton>
                    </div>
                }
                {this.state.method === "file" &&
                    <div>
                        <BloomButton l10nKey="Publish.Android.Save"
                            l10nComment="Button that tells Bloom to save the book as a .bloomD file."
                            clickEndpoint="publish/android/file/save"
                            enabled={true}>
                            Save...
                        </BloomButton>
                    </div>
                }

                <div id="progress-section" style={{ visibility: this.state.method === "file" ? "hidden" : "visible" }}>
                    <div id="progress-row">
                        <h1>Progress</h1>
                        <HtmlHelpLink l10nKey="Publish.Android.Troubleshooting"
                            fileid="Publish-Android-Troubleshooting">
                            Troubleshooting Tips
                        </HtmlHelpLink>
                    </div>
                    <ProgressBox />
                </div>
            </div>
        );
    }
}

ReactDOM.render(
    <AndroidPublishUI />,
    document.getElementById("AndroidPublishUI")
);
