import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import ProgressBox from "../../react_components/progressBox";
import BloomButton from "../../react_components/bloomButton";
import ContentEditable from "../../react_components/ContentEditable";
import Option from "../../react_components/option";
import Link from "../../react_components/link";
import HelpLink from "../../react_components/helpLink";
import HtmlHelpLink from "../../react_components/htmlHelpLink";
import { H1, Div, IUILanguageAwareProps } from "../../react_components/l10n";
import WebSocketManager from "../../utils/WebSocketManager";

const kWebSocketLifetime = "publish-android";

interface IComponentState {
    method: string;
    stateId: string;
    backColor: string;
    colorsVisible: boolean;
}
// This is a screen of controls that gives the user instructions and controls
// for pushing a book to a connected Android device running Bloom Reader.
class AndroidPublishUI extends React.Component<IUILanguageAwareProps, IComponentState> {
    isLinux: boolean;
    constructor(props) {
        super(props);

        this.isLinux = this.getIsLinuxFromUrl();
        this.state = { stateId: "stopped", method: "wifi", backColor: "#FFFFFF", colorsVisible: false };

        // enhance: For some reason setting the callback to "this.handleUpdate" calls handleUpdate()
        // with "this" set to the button, not this overall control.
        // I don't quite have my head around this problem yet, but this oddity fixes it.
        // See https://medium.com/@rjun07a/binding-callbacks-in-react-components-9133c0b396c6
        this.handleUpdateState = this.handleUpdateState.bind(this);

        WebSocketManager.addListener(kWebSocketLifetime, event => {
            var e = JSON.parse(event.data);
            if (e.id === "publish/android/state") {
                this.handleUpdateState(e.payload);
            }
        });

        axios.get("/bloom/api/publish/android/method").then(result => {
            this.setState({ method: result.data });
        });

        axios.get("/bloom/api/publish/android/backColor").then(result =>
            this.setState({ backColor: result.data })
        );
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
        axios.post("/bloom/api/publish/android/cleanup").then(result => {
            WebSocketManager.closeSocket(kWebSocketLifetime);
        });
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

    onCopy(e) {
        e.preventDefault();

        // Yes, this is a hack. I simply could not get the client to populate the clipboard.
        // I tried using react-copy-to-clipboard, but kept getting runtime errors as if the component was not found.
        // I tried using document.execCommand("copy"), but though it worked in FF and Chrome, it did not work in Bloom.
        axios.post("/bloom/api/publish/android/textToClipboard",
            document.getElementById("progress-box").innerText, { headers: { "Content-Type": "text/plain" } });
    }

    reportColorChange(newColor: string) {
        axios.post("/bloom/api/publish/android/backColor", newColor,
            { headers: { "Content-Type": "text/plain" } }).then(
            //wait until it's set because once the state changes, a
            // new image gets requested and we want that to happen
            // only after the server has registered this change.
            () => this.setState({ backColor: newColor }));
    }

    render() {
        let colors: string[] = ["#E48C84", "#B0DEE4", "#98D0B9", "#C2A6BF", "#FFFFA4", "#FEBF00", "#7BDCB5",
            "#B2CC7D", "#F8B576", "#D29FEF", "#ABB8C3", "#C1EF93", "#FFD4D4", "#FFAAD4"];


        return (
            <div>
                <H1 className="media-heading" l10nKey="PublishTab.Android.Media"
                    l10nComment="A heading in the Publish to Android screen.">
                    Media
                </H1>

                <div className="media-row">
                    <div className="media-subheading">
                        <Div l10nKey="PublishTab.Android.ThumbnailColor">
                            Thumbnail Color
                        </Div>
                    </div>
                    <div className="tc-outer-wrapper" tabIndex={0} onClick={
                        (event) => {
                            this.setState({ colorsVisible: !this.state.colorsVisible });
                            axios.get("/bloom/api/publish/android/backColor").then(result =>
                                this.setState({ backColor: result.data })
                            );
                        }}
                        onBlur={() => { this.setState({ colorsVisible: false }); }}>
                        <div className="tc-image-wrapper">
                            <img className="tc-image"
                                // the api ignores the color parameter, but it
                                // causes this to re-request the img whenever the backcolor changes
                                src={"/bloom/api/publish/android/thumbnail?color=" + this.state.backColor}></img>
                        </div>
                        <div className="tc-menu-arrow">
                            <div className="tc-pulldown-wrapper" style={{ visibility: (this.state.colorsVisible ? "visible" : "hidden") }}>
                                {colors.map((color, i) =>
                                    <div className="tc-color-option" key={i} style={{ backgroundColor: color }} data-color={color} onClick={
                                        (event) => {
                                            const newColor = event.currentTarget.getAttribute("data-color");
                                            this.reportColorChange(newColor);
                                        }}>
                                    </div>)}
                                <div className="tc-hex-wrapper" onClick={(event) => event.stopPropagation()}>
                                    <div className="tc-hex-leadin">#</div>
                                    <div className="tc-hex-value">
                                        <ContentEditable content={this.state.backColor.substring(1)} onChange={(newContent => {
                                            this.reportColorChange("#" + newContent);
                                        })} onEnterKeyPressed={() => this.setState({ colorsVisible: false })} />
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <H1 l10nKey="PublishTab.Android.Method"
                    l10nComment="There are several methods for pushing a book to android. This is the heading above the chooser.">
                    Method Choices
                </H1>
                {/* This wrapper, with some really tricky CSS, serves to make a select where the options have both text
                and images, and the down arrow is a dark black on a plain white background with a black line left of it.
                1. The outer div provides the outer border and, by means of a background image, both the down arrow
                and the vertical line to the left of it.
                2. The normal down arrow in the select is hidden by moz-appearance: none.
                3. The outer div's background is allowed to show through the select by background-color: transparent.
                4. A class for each option brings in the appropriate background image. One of these classes is
                applied to the select itself by the react logic so the right image shows up there too.

                We can't just put a background image on the select to get the down arrow, because we're already
                using its background image to get the image that goes with the selected option.
                I can't find a way to get a border between the select and the button without making the select
                smaller, and then the down-arrow is not part of it and doesn't trigger the select action.
                Modern browsers do not allow code to trigger the pulling down of the select list...it's
                somehow considered a security risk, so we can't just put a click action on the button.

                Note: when the select has focus, the dotted focus outline is drawn around the text rather
                than the whole select, putting a distracting line between the text and picture. For some reason
                Firefox is drawing this inside the padding that is used to put the text at the top instead of
                centered in the select. I've tried things that should turn the focus outline off, like
                :focus {outline: none !important} and -moz-focusring {color: transparent; text-shadow: 0 0 0 #000;}
                but they didn't make any difference. I've tried various things, including messing with the text
                baseline and applying properties to the span that FF creates inside each option, to move the
                text to the top without padding, but with no success. We decided to live with the dotted line
                rather than spending more time trying to remove it (or re-implementing the control without
                using a select, probably the most promising way to get exactly what we want.)

                This approach does not work in FF 56 (no images in the options) so we will probably eventually
                have to re-implement without using select.
                 */}
                <div className="method-select-wrapper">
                    <select className={`method-shared method-root ${this.state.method}-method-option`}
                        disabled={this.state.stateId !== "stopped"} value={this.state.method} onChange={
                            (event) => {
                                this.setState({ method: event.target.value });
                                axios.post("/bloom/api/publish/android/method", event.target.value,
                                    { headers: { "Content-Type": "text/plain" } });
                            }}>
                        <Option l10nKey="PublishTab.Android.ChooseWifi"
                            className="method-shared wifi-method-option"
                            value="wifi">
                            Serve on WiFi Network
                    </Option>
                        <Option l10nKey="PublishTab.Android.ChooseUSB"
                            className="method-shared usb-method-option" value="usb">
                            Send over USB Cable
                    </Option>
                        <Option l10nKey="PublishTab.Android.ChooseFile"
                            className="method-shared file-method-option" value="file">
                            Save Bloom Reader File
                    </Option>
                    </select>
                </div>

                <p />
                <H1 l10nKey="PublishTab.Android.Control"
                    l10nComment="This is the heading above various buttons that control the publishing of the book to Android.">
                    Control
                </H1>

                {this.state.method === "wifi" &&
                    <div>
                        <BloomButton l10nKey="PublishTab.Android.Wifi.Start"
                            l10nComment="Button that tells Bloom to begin offering this book on the wifi network."
                            enabled={this.state.stateId === "stopped"}
                            clickEndpoint="publish/android/wifi/start"
                            hasText={true}>
                            Start Serving
                        </BloomButton>
                        <BloomButton l10nKey="PublishTab.Android.Wifi.Stop"
                            l10nComment="Button that tells Bloom to stop offering this book on the wifi network."
                            enabled={this.state.stateId === "ServingOnWifi"}
                            clickEndpoint="publish/android/wifi/stop"
                            hasText={true}>
                            Stop Serving
                        </BloomButton>
                    </div>
                }

                {this.state.method === "usb" &&
                    <div>
                        <BloomButton l10nKey="PublishTab.Android.Usb.Start"
                            l10nComment="Button that tells Bloom to send the book to a device via USB cable."
                            enabled={this.state.stateId === "stopped"}
                            clickEndpoint="publish/android/usb/start"
                            hidden={this.isLinux}
                            hasText={true}>
                            Connect with USB cable
                        </BloomButton>

                        <BloomButton l10nKey="PublishTab.Android.Usb.Stop"
                            enabled={this.state.stateId === "UsbStarted"}
                            clickEndpoint="publish/android/usb/stop"
                            hasText={true}>
                            Stop Trying
                        </BloomButton>
                    </div>
                }
                {this.state.method === "file" &&
                    <div>
                        <BloomButton l10nKey="PublishTab.Android.Save"
                            l10nComment="Button that tells Bloom to save the book as a .bloomD file."
                            clickEndpoint="publish/android/file/save"
                            enabled={true}
                            hasText={true}>
                            Save...
                        </BloomButton>
                    </div>
                }

                <div id="progress-section" style={{ visibility: "visible" }}>
                    <div id="progress-row">
                        <h1>Progress</h1>
                        <Link id="getBloomReaderLink"
                            href="https://play.google.com/store/search?q=%2B%22sil%20international%22%20%2B%22bloom%20reader%22&amp;c=apps"
                            l10nKey="PublishTab.Android.GetBloomReader"
                            l10nComment="Link to find Bloom Reader on Google Play Store">
                            Get Bloom Reader App
                        </Link>
                        <HtmlHelpLink l10nKey="PublishTab.Android.Troubleshooting"
                            fileid="Publish-Android-Troubleshooting">
                            Troubleshooting Tips
                        </HtmlHelpLink>
                    </div>
                    <ProgressBox lifetimeLabel={kWebSocketLifetime} />
                    <Link id="copyProgressToClipboard"
                        href=""
                        l10nKey="PublishTab.Android.CopyToClipboard"
                        onClick={this.onCopy}>
                        Copy to Clipboard
                    </Link>
                </div>
            </div>
        );
    }
}

ReactDOM.render(
    <AndroidPublishUI />,
    document.getElementById("AndroidPublishUI")
);
