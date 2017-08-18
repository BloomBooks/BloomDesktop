import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import ProgressBox from "../../react_components/progressBox";
import BloomButton from "../../react_components/bloomButton";
import HelpLink from "../../react_components/helpLink";
import { H1, H2, LocalizableElement, IUILanguageAwareProps, P } from "../../react_components/l10n";

interface IComponentState {
    method: string;
    stateId: string;
}

// This is a screen of controls that gives the user instructions and controls
// for pushing a book to a connected Android device running Bloom Reader.
class AndroidPublishUI extends React.Component<IUILanguageAwareProps, IComponentState> {
    webSocket: WebSocket;
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

        this.webSocket = this.getWebSocket();
        this.webSocket.addEventListener("message", event => {
            var e = JSON.parse(event.data);
            if (e.id === "publish/android/state") {
                this.handleUpdateState(e.payload);
            }
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
                        (event) => self.setState({ method: event.target.value })}>;
                    <option className="method-option wifi-method-option" value="wifi">Serve on WiFi Network</option>
                    <option className="method-option usb-method-option" value="usb">Send over USB Cable</option>
                    <option className="method-option file-method-option" value="file">Save Bloom Reader File</option>
                </select>

                <p />
                <H1 l10nKey="Publish.Android.Control"
                    l10nComment="This is the heading above various buttons that control the publishing of the book to Android.">
                    Control
                </H1>

                {this.state.method === "wifi" &&
                    <div>
                        <BloomButton l10nKey="Publish.Android.Wifi.Serving"
                            enabled={this.state.stateId === "stopped"}
                            clickEndpoint="publish/android/wifi/start">
                            Start Serving
                        </BloomButton>
                        <BloomButton l10nKey="Publish.Android.Wifi.Stopped"
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
                            clickEndpoint="publish/android/usb/save"
                            enabled={true}>
                            Save...
                        </BloomButton>
                        &lt;--- Not implemented yet
                    </div>
                }
                <div id="progress-row">
                    <h1>Progress</h1>
                    <HelpLink l10nKey="Publish.Android.Troubleshooting"
                        helpId="publish-android-troubleshooting.html">
                        Troubleshooting Tips
                    </HelpLink>
                </div>
                <ProgressBox />
            </div>
        );
    }

    // Enhance: We want to extract this higher up. See http://issues.bloomlibrary.org/youtrack/issue/BL-4804
    private getWebSocket(): WebSocket {
        let kSocketName = "webSocket";
        if (typeof window.top[kSocketName] === "undefined") {
            // Enhance: ask the server for the socket so that we aren't assuming that it is the current port + 1
            let websocketPort = parseInt(window.location.port, 10) + 1;
            window.top[kSocketName] = new WebSocket("ws://127.0.0.1:" + websocketPort.toString());
        }
        return window.top[kSocketName];
    }
}

ReactDOM.render(
    <AndroidPublishUI />,
    document.getElementById("AndroidPublishUI")
);
