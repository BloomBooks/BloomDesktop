import axios = require("axios");
import * as React from "react";
import * as ReactDOM from "react-dom";
import ProgressBox from "./progressBox";
import BloomButton from "./bloomButton";

interface ComponentProps { }

interface ComponentState {
    stateId: string;
}

class AndroidPublishUI extends React.Component<ComponentProps, ComponentState> {
    constructor(props) {
        super(props);
        this.state = { stateId: "ReadyToConnect" };
        let self = this;
    }
    render() {
        return (
            <div>
                {/* TODO make a simple way for these kind of links to do the right thing.
                    (Which is what? I assume find the the document and open in user's browser? */}
                <a href="learnAboutDigitalPublishingOptions.html">
                    Learn about your digital publishing options.
                </a>
                <h1>Step 1: Install Bloom Reader app on the Android device</h1>
                <a href="howToGetBloomReaderOnDevice.html">
                    How to get the Bloom Reader app on your device.
                </a>
                <br />
                <a href="learnAboutBloomReaderApp.html">
                    Learn more about the Bloom Reader app.
                </a>
                <h1>Step 2: Launch the Bloom Reader app on the device</h1>
                <h1>Step 3: Connect this computer to the device</h1>

                {/* TODO: factor out these connect buttons to one class.
                    Perhaps something like
                    <StateButton clickEndpoint= "/bloom/api/publish/android/connectUsb" enabledWhen= {stateId==='ReadyToConnect'} /> */}
                <button
                    disabled={this.state.stateId !== "ReadyToConnect"}
                    onClick={() => axios.get<string>("/bloom/api/publish/android/connectUsb").then((response) => {
                        this.setState({ stateId: response.data });
                    })}
                >
                    {/* TODO: Make localizable. Perhaps something like
                        localize({id:'publish.android.connectWithUSB', comment:'button label' en:'Connect with USB cable'})
                        Or a react component, like
                        <String id='publish.android.connectWithUSB' comment='button label'>
                            Connect with USB cable
                        </String>
                    */}
                    Connect with USB cable
                </button>
                <br />
                <button
                    disabled={this.state.stateId !== "ReadyToConnect"}
                    onClick={() => axios.get<string>("/bloom/api/publish/android/connectWifi").then((response) => {
                        this.setState({ stateId: response.data });
                    })}
                >
                    Connect with Wifi
                </button>
                <h1>Step 4: Send this book to the device.</h1>
                <button
                    {...(this.state.stateId === "ReadyToSend" ? {} : { disabled: true }) }
                    onClick={() => axios.post("/bloom/api/publish/android/sendBook")}
                >
                    Send Book
                </button>
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
