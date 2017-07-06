import * as React from "react";
import * as ReactDOM from "react-dom";
import ProgressBox from "../../react_components/progressBox";
import BloomButton from "../../react_components/bloomButton";
import HelpLink from "../../react_components/helpLink";
import { H1, H2, LocalizableElement, IUILanguageAwareProps } from "../../react_components/l10n";

interface ComponentState {
    stateId: string;
}

// This is a screen of controls that gives the user instructions and controls
// for pushing a book to a connected Android device running Bloom Reader.
class AndroidPublishUI extends React.Component<IUILanguageAwareProps, ComponentState> {
    constructor(props) {
        super(props);
        this.state = { stateId: "ReadyToConnect" };

        // enhance: For some reason setting the callback to "this.handleUpdate" calls handleUpdate()
        // with "this" set to the button, not this overal control.
        // I don't quite have my head around this problem yet, but this oddity fixes it.
        // See https://medium.com/@rjun07a/binding-callbacks-in-react-components-9133c0b396c6
        this.handleUpdateState = this.handleUpdateState.bind(this);
    }

    handleUpdateState(s: string): void {
        this.setState({ stateId: s });
        //console.log("this.state is " + JSON.stringify(this.state));
    }

    render() {
        let self = this;
        return (
            <div>
                <HelpLink l10nkey="publish.android.learnAboutDigitalPublishingOptions"
                    l10ncomment="" helpId="learnAboutDigitalPublishingOptions">
                    Learn about your digital publishing options.
                </HelpLink>
                <H1 l10nkey="publish.android.stepInstall">
                    Step 1: Install Bloom Reader app on the Android device</H1>
                <HelpLink l10nkey="publish.android.howToGetBloomReaderOnDevice"
                    helpId="howToGetBloomReaderOnDevice.html">
                    How to get the Bloom Reader app on your device.
                </HelpLink>
                <br />
                <HelpLink l10nkey="publish.android.learnAboutBloomReaderApp"
                    helpId="learnAboutBloomReaderApp.html">
                    Learn more about the Bloom Reader app.
                </HelpLink>
                <H1 l10nkey="publish.android.stepLaunch">
                    Step 2: Launch the Bloom Reader app on the device
                </H1>
                <H1 l10nkey="publish.android.stepConnect">
                    Step 3: Connect this computer to the device
                </H1>

                <BloomButton l10nkey="publish.android.connectUsb"
                    l10ncomment="Button that tells Bloom to connect to an device using a USB cable"
                    enabled={this.state.stateId === "ReadyToConnect"}
                    clickEndpoint="publish/android/connectUsb"
                    onUpdateState={self.handleUpdateState}>
                    Connect with USB cable
                </BloomButton>
                <br />
                <BloomButton l10nkey="publish.android.connectWifi"
                    l10ncomment="Button that tells Bloom to connect to an device using a USB cable"
                    enabled={this.state.stateId === "ReadyToConnect"}
                    clickEndpoint="publish/android/connectWifi"
                    onUpdateState={this.handleUpdateState}>
                    Connect with Wifi
                </BloomButton>

                <H1 l10nkey="publish.android.stepSend">
                    Step 4: Send this book to the device.
                </H1>
                <BloomButton l10nkey="publish.android.sendBook"
                    l10ncomment="Button that tells Bloom to connect to an device using a USB cable"
                    enabled={this.state.stateId === "ReadyToSend"}
                    clickEndpoint="publish/android/connectWifi"
                    onUpdateState={this.handleUpdateState}>
                    Send Book
                    </BloomButton>

                <h3>Progress</h3>
                state: {this.state.stateId}

                <ProgressBox />

            </div>
        );
    }
}

ReactDOM.render(
    <AndroidPublishUI />,
    document.getElementById("AndroidPublishUI")
);
