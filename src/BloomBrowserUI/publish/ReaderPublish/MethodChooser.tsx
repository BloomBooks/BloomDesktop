/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { RadioGroup } from "../../react_components/RadioGroup";
import BloomButton from "../../react_components/bloomButton";
import { useApiStringState, useWatchBooleanEvent } from "../../utils/bloomApi";
import { isLinux } from "../../utils/isLinux";
import { useL10n } from "../../react_components/l10nHooks";
import Typography from "@mui/material/Typography";
import { LocalizedString } from "../../react_components/l10nComponents";
import { default as InfoIcon } from "@mui/icons-material/InfoOutlined";
import HtmlHelpLink from "../../react_components/htmlHelpLink";
import { kMutedTextGray } from "../../bloomMaterialUITheme";
import { kBloomWarning } from "../../utils/colorUtils";
import HelpLink from "../../react_components/helpLink";

const methodNameToImageFileName = {
    wifi: "publish-via-wifi.svg",
    usb: "publish-via-usb.svg",
    file: "publish-to-file.svg"
};

// Lets the user choose how they want to "publish" the bloompub, along with a button to start that process.
// This is a set of radio buttons and image that goes with each choice, plus a button to start off the sharing/saving
export const MethodChooser: React.FunctionComponent = () => {
    const [method, setMethod] = useApiStringState(
        "publish/bloompub/method",
        "file"
    );
    const isLicenseOK = useWatchBooleanEvent(
        true,
        "publish-bloompub",
        "publish/licenseOK"
    );

    const methodImageFileName: string = methodNameToImageFileName[method];

    const radioLabelElement = (label: string): JSX.Element => (
        <Typography
            css={css`
                font-size: 1rem;
            `}
        >
            {label}
        </Typography>
    );

    return (
        <React.Fragment>
            <div
                css={css`
                    display: flex;
                    flex-direction: row;
                    // We don't need to set the height here: it displays just fine without being
                    // explicitly set.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7506.

                    // The center of a selected radio button is drawn with an <svg> element by materialui.
                    // For some reason, in Firefox 45, in Publish:Reader, the "left" says 20.4667px, whereas
                    // it says 0px in modern browsers. A mystery. Anyhow this resets it.
                    // (I expected unset to fix it, but it doesn't.)
                    .MuiRadio-root svg {
                        left: 0;
                    }
                `}
            >
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        flex-shrink: 0;
                        // leave room for the image, wrap radios if translations are really long
                        padding-right: 20px;
                        .MuiFormControl-root {
                            margin-top: 0;
                        }
                    `}
                >
                    <RadioGroup
                        value={method}
                        onChange={m => setMethod(m)}
                        choices={{
                            file: radioLabelElement(
                                useL10n(
                                    "Save BloomPUB File",
                                    "PublishTab.Android.ChooseBloomPUBFile"
                                )
                            ),
                            wifi: radioLabelElement(
                                useL10n(
                                    "Share over Wi-Fi",
                                    "PublishTab.Android.ChooseWifi"
                                )
                            ),
                            usb: radioLabelElement(
                                useL10n(
                                    "Send over USB Cable",
                                    "PublishTab.Android.ChooseUSB"
                                )
                            )
                        }}
                    />
                    {getStartButton(method, isLicenseOK)}
                </div>
                <div
                    css={css`
                        max-width: 500px; // this is just to limit the hint text length on a big monitor.
                        display: flex;
                        flex-direction: column;
                        flex-grow: 1;
                        padding-left: 15px;
                        padding-right: 15px;
                    `}
                >
                    <img
                        css={css`
                            width: 200px;
                            object-fit: contain;
                            margin-bottom: 20px;
                        `}
                        src={`/bloom/publish/ReaderPublish/${methodImageFileName}`}
                        alt="An image that just illustrates the currently selected publishing method."
                    />
                    {getHint(method)}
                </div>
            </div>
        </React.Fragment>
    );
};

function getStartButton(method: string, licenseOK: boolean) {
    const buttonCss =
        "align-self: flex-end; min-width: 120px; margin-top: 20px;";
    switch (method) {
        case "file":
            return (
                <BloomButton
                    css={css`
                        ${buttonCss}
                    `}
                    l10nKey="PublishTab.Save"
                    l10nComment="Button that tells Bloom to save the book in the current format."
                    clickApiEndpoint="publish/bloompub/file/save"
                    enabled={licenseOK}
                    hasText={true}
                    size="large"
                >
                    Save...
                </BloomButton>
            );
        case "usb":
            return (
                <BloomButton
                    css={css`
                        ${buttonCss}
                    `}
                    l10nKey="PublishTab.Android.Usb.Start"
                    l10nComment="Button that tells Bloom to send the book to a device via USB cable."
                    enabled={licenseOK}
                    clickApiEndpoint="publish/bloompub/usb/start"
                    hidden={isLinux()}
                    hasText={true}
                    size="large"
                >
                    Connect with USB cable
                </BloomButton>
            );
        case "wifi":
            return (
                <BloomButton
                    css={css`
                        ${buttonCss}
                    `}
                    l10nKey="PublishTab.Android.Wifi.Start"
                    l10nComment="Button that tells Bloom to begin offering this book on the wifi network."
                    enabled={licenseOK}
                    clickApiEndpoint="publish/bloompub/wifi/start"
                    hasText={true}
                    size="large"
                >
                    Share
                </BloomButton>
            );
        default:
            throw new Error("Unhandled method choice");
    }
}

function getHint(method: string) {
    // Despite Typography using 'variant="h6"', the actual element used is "h1", so we target that.
    // Also, the "!important" below is needed to overrule MUI's typography default.
    const hintHeadingCss =
        "display: flex;\nalign-items: center;\nh1 { margin-left: 10px !important; }";
    switch (method) {
        case "file":
            return (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        a {
                            margin-bottom: 0.5em;
                        }
                    `}
                >
                    <HelpLink
                        l10nKey="PublishTab.Android.BloomPUB.MakeRABAppHelpLink"
                        helpId="Tasks/Publish_tasks/Making_BloomPUB_Apps_with_Reading_App_Builder.htm"
                    >
                        Making BloomPUB Apps with Reading App Builder
                    </HelpLink>
                    <HelpLink
                        l10nKey="PublishTab.Android.BloomPUB.ViewingWithBRHelpLink"
                        helpId="Tasks/Publish_tasks/Make_a_BloomPUB_file_overview.htm"
                    >
                        Viewing BloomPUBs on Bloom Reader (Android)
                    </HelpLink>
                    <HelpLink
                        l10nKey="PublishTab.Android.BloomPUB.ViewingOnWindowsHelpLink"
                        helpId="Tasks/Publish_tasks/Viewing_BloomPUBs_on_Windows.htm"
                    >
                        Viewing BloomPUBs on Windows
                    </HelpLink>
                </div>
            );
        case "usb":
            return (
                <React.Fragment>
                    <Typography
                        css={css`
                            margin-bottom: 10px;
                        `}
                    >
                        <LocalizedString l10nKey="PublishTab.Android.USB.OpenMenuItem">
                            On the Android device, run Bloom Reader, open the
                            menu and choose 'Receive books via USB'.
                        </LocalizedString>
                    </Typography>
                    <div
                        css={css`
                            ${hintHeadingCss}
                        `}
                    >
                        <InfoIcon htmlColor={kBloomWarning} />
                        <Typography variant="h6">
                            <LocalizedString l10nKey="PublishTab.Android.USB.Hint.Heading">
                                USB is Difficult
                            </LocalizedString>
                        </Typography>
                    </div>
                    <Typography
                        css={css`
                            color: ${kMutedTextGray};
                        `}
                    >
                        <LocalizedString l10nKey="PublishTab.Android.USB.Hint">
                            To Send via USB, you may need to get the right
                            cable, install phone drivers on your computer, or
                            modify settings on your phone.
                        </LocalizedString>
                    </Typography>
                </React.Fragment>
            );
        case "wifi":
            return (
                <React.Fragment>
                    <div
                        css={css`
                            ${hintHeadingCss}
                        `}
                    >
                        <InfoIcon color="primary" />
                        <Typography variant="h6">
                            <LocalizedString l10nKey="PublishTab.Android.WiFi.Hint.Heading">
                                No Wi-Fi Network?
                            </LocalizedString>
                        </Typography>
                    </div>
                    <Typography
                        css={css`
                            color: ${kMutedTextGray};
                        `}
                    >
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
                </React.Fragment>
            );
        default:
            throw new Error("Unhandled method choice");
    }
}
