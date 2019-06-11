import * as React from "react";
import "./ReaderPublish.less";
import { RadioGroup } from "../../react_components/RadioGroup";
import BloomButton from "../../react_components/bloomButton";
import { BloomApi } from "../../utils/bloomApi";
import { isLinux } from "../../utils/isLinux";
import { useL10n } from "../../react_components/l10nHooks";
import { Typography } from "@material-ui/core";
import { LocalizedString } from "../../react_components/l10nComponents";
import { default as InfoIcon } from "@material-ui/icons/InfoOutlined";
import HtmlHelpLink from "../../react_components/htmlHelpLink";

const wifiImage = require("./publish-via-wifi.svg");
const usbImage = require("./publish-via-usb.svg");
const fileImage = require("./publish-to-file.svg");

const methodNameToImageUrl = {
    wifi: wifiImage,
    usb: usbImage,
    file: fileImage
};

// Lets the user choose how they want to "publish" the bloomd, along with a button to start that process.
// This is a set of radio buttons and image that goes with each choice, plus a button to start off the sharing/saving
export const MethodChooser: React.FunctionComponent = () => {
    const [method, setMethod] = BloomApi.useApiString(
        "publish/android/method",
        "wifi"
    );

    const methodImage = (methodNameToImageUrl as any)[method];
    return (
        <>
            <div className={"methodChooserRoot"}>
                <div className={"column1"}>
                    <RadioGroup
                        value={method}
                        onChange={m => setMethod(m)}
                        choices={{
                            wifi: useL10n(
                                "Share over Wi-Fi",
                                "PublishTab.Android.ChooseWifi"
                            ),
                            file: useL10n(
                                "Save Bloom Reader File",
                                "PublishTab.Android.ChooseFile"
                            ),
                            usb: useL10n(
                                "Send over USB Cable",
                                "PublishTab.Android.ChooseUSB"
                            )
                        }}
                    />
                    {getStartButton(method)}
                </div>
                <div className={"column2"}>
                    <img
                        src={methodImage}
                        alt="An image that just illustrates the currently selected publishing method."
                    />
                    {getHint(method)}
                </div>
            </div>
        </>
    );
};

function getStartButton(method: string) {
    switch (method) {
        case "file":
            return (
                <BloomButton
                    l10nKey="PublishTab.Save"
                    l10nComment="Button that tells Bloom to save the book as a .bloomD file."
                    clickApiEndpoint="publish/android/file/save"
                    enabled={true}
                    hasText={true}
                >
                    Save...
                </BloomButton>
            );
        case "usb":
            return (
                <BloomButton
                    l10nKey="PublishTab.Android.Usb.Start"
                    l10nComment="Button that tells Bloom to send the book to a device via USB cable."
                    enabled={true}
                    clickApiEndpoint="publish/android/usb/start"
                    hidden={isLinux()}
                    hasText={true}
                >
                    Connect with USB cable
                </BloomButton>
            );
        case "wifi":
            return (
                <BloomButton
                    l10nKey="PublishTab.Android.Wifi.Start"
                    l10nComment="Button that tells Bloom to begin offering this book on the wifi network."
                    enabled={true}
                    clickApiEndpoint="publish/android/wifi/start"
                    hasText={true}
                >
                    Share
                </BloomButton>
            );
        default:
            throw new Error("Unhandled method choice");
    }
}

function getHint(method: string) {
    switch (method) {
        case "file":
            return (
                <>
                    <div className="hint-heading">
                        <InfoIcon color="primary" />
                        <Typography variant="h6">
                            <LocalizedString l10nKey="PublishTab.Android.BloomD.Hint.Heading">
                                Sharing .bloomd Files
                            </LocalizedString>
                        </Typography>
                    </div>
                    <Typography>
                        <LocalizedString l10nKey="PublishTab.Android.BloomD.Hint">
                            You can use sharing apps like email, Dropbox, and
                            WhatsApp to get your .bloomd file onto a device.
                        </LocalizedString>
                    </Typography>
                </>
            );
        case "usb":
            return (
                <>
                    <div className="hint-heading">
                        <InfoIcon className="warning" />
                        <Typography variant="h6">
                            <LocalizedString l10nKey="PublishTab.Android.USB.Hint.Heading">
                                USB is Difficult
                            </LocalizedString>
                        </Typography>
                    </div>
                    <Typography>
                        <LocalizedString l10nKey="PublishTab.Android.USB.Hint">
                            To Send via USB, you may need to get the right
                            cable, install phone drivers on your computer, or
                            modify settings on your phone.
                        </LocalizedString>
                    </Typography>
                </>
            );
        case "wifi":
            return (
                <>
                    <div className="hint-heading">
                        <InfoIcon color="primary" />
                        <Typography variant="h6">
                            <LocalizedString l10nKey="PublishTab.Android.WiFi.Hint.Heading">
                                No Wi-Fi Network?
                            </LocalizedString>
                        </Typography>
                    </div>
                    <Typography>
                        <LocalizedString
                            l10nKey="PublishTab.Android.WiFi.Hint"
                            l10nComment="This is preceded by a heading that says 'No Wi-Fi Network'. 'one' here refers to 'Wi Fi' network."
                        >
                            There are several ways to start a temporary one.
                        </LocalizedString>
                        &nbsp;
                        <HtmlHelpLink
                            l10nKey="Common.LearnMore"
                            fileid="Publish-WiFi-Network"
                        >
                            Learn More
                        </HtmlHelpLink>
                    </Typography>
                </>
            );
        default:
            throw new Error("Unhandled method choice");
    }
}
