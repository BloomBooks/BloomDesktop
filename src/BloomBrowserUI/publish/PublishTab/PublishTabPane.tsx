/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import * as ReactDOM from "react-dom";
import {
    kBloomBlue,
    kPanelBackground,
    lightTheme
} from "../../bloomMaterialUITheme";
import { BloomTabs } from "../../react_components/BloomTabs";
import { Tab, TabList, TabPanel } from "react-tabs";
import { Div, H2, Span } from "../../react_components/l10nComponents";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { StyledEngineProvider, ThemeProvider } from "@mui/material/styles";
import { ReaderPublishScreen } from "../ReaderPublish/ReaderPublishScreen";
import { useL10n } from "../../react_components/l10nHooks";
import { get, post, postString } from "../../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../../utils/WebSocketManager";
import { LibraryPublishScreen } from "../LibraryPublish/LibraryPublishScreen";
import { PDFPrintPublishScreen } from "../PDFPrintPublish/PDFPrintPublishScreen";
import { PublishAudioVideo } from "../video/PublishAudioVideo";
import { EPUBPublishScreen } from "../ePUBPublish/ePUBPublishScreen";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { NoteBox, WarningBox } from "../../react_components/boxes";
import { kBloomUnselectedTabBackground } from "../../utils/colorUtils";
import { PublishingBookRequiresHigherTierNotice } from "./PublishingBookRequiresHigherTierNotice";
import { FeatureStatus } from "../../react_components/featureStatus";
import { AboutDialogLauncher } from "../../react_components/aboutDialog";
import { RegistrationDialogLauncher } from "../../react_components/registrationDialog";

export const CheckoutNeededScreen: React.FunctionComponent<{
    titleForDisplay: string;
}> = props => {
    const needsCheckoutText1 = useL10n(
        "Please check out this book from the Team Collection before publishing it.",
        "TeamCollection.CheckoutRequiredExplanation"
    );

    return (
        <div
            css={css`
                background-color: ${kBloomUnselectedTabBackground};
                margin: 0;
                height: 100%;
                width: 100%;
                position: absolute;
            `}
        >
            <NoteBox
                css={css`
                    max-width: 800px;
                    width: fit-content;
                    margin: 30px;
                `}
                iconSize="large"
            >
                <div>
                    <H2
                        l10nKey="TeamCollection.CheckoutRequired"
                        css={css`
                            margin-top: 0;
                        `}
                        temporarilyDisableI18nWarning={true}
                    >
                        Checkout Required
                    </H2>
                    <p>{needsCheckoutText1}</p>
                </div>
            </NoteBox>
        </div>
    );
};

export const PublishTabPane: React.FunctionComponent = () => {
    const kWaitForUserToChooseTabIndex = 5;

    const [publishTabReady, setPublishTabReady] = React.useState(false);
    const [publishTabInfo, setPublishTabInfo] = React.useState({
        checkoutNeeded: false,
        canUpload: false,
        bookTitle: "",
        featurePreventingPublishing: undefined as FeatureStatus | undefined
    });
    const [tabIndex, setTabIndex] = React.useState(
        kWaitForUserToChooseTabIndex
    );
    const setup = () => {
        setTabIndex(kWaitForUserToChooseTabIndex);
        get("publish/getInitialPublishTabInfo", result => {
            // There should be a current selection by now but just in case:
            if (!result.data) {
                return;
            }
            setPublishTabInfo({
                checkoutNeeded: result.data.cannotPublishWithoutCheckout,
                canUpload: result.data.canUpload,
                bookTitle: result.data.titleForDisplay,
                featurePreventingPublishing:
                    result.data.featurePreventingPublishing
            });
            setPublishTabReady(true);
        });
    };
    // User is switching to publish tab from another tab
    useSubscribeToWebSocketForEvent("publish", "switchToPublishTab", () => {
        setup();
    });
    // User is switching out of publish tab, clear the display so the the old stuff doesn't flash when the user comes back on another book
    useSubscribeToWebSocketForEvent("publish", "switchOutOfPublishTab", () => {
        setPublishTabReady(false);
    });
    React.useEffect(() => {
        // While the top bar is still in winforms, the first time the user loads the publish tab, the websocket event may occur before the component is ready
        setup();
    }, []);

    let altContent: JSX.Element | undefined = undefined;
    if (!publishTabReady) {
        // Show a blank screen until we get initial data for the publish tab
        altContent = <div></div>;
    } else if (publishTabInfo.featurePreventingPublishing) {
        altContent = (
            <PublishingBookRequiresHigherTierNotice
                titleForDisplay={publishTabInfo.bookTitle}
                featurePreventingPublishing={
                    publishTabInfo.featurePreventingPublishing
                }
            />
        );
    } else if (publishTabInfo.checkoutNeeded) {
        altContent = (
            <CheckoutNeededScreen titleForDisplay={publishTabInfo.bookTitle} />
        );
    }

    interface PublishTabProps {
        tipL10nKey: string;
        iconSrc: string;
        labelL10nKey: string;
        label: string;
    }
    const publishTabs: PublishTabProps[] = [
        {
            tipL10nKey: "PublishTab.PdfPrintButton-tooltip",
            iconSrc: "/bloom/publish/PublishTab/PdfPrint.png",
            labelL10nKey: "PublishTab.PdfPrint.Button",
            label: "PDF & Print"
        },
        {
            tipL10nKey: "PublishTab.ButtonThatShowsUploadForm-tooltip",
            iconSrc: "/bloom/publish/PublishTab/upload.png",
            labelL10nKey: "PublishTab.ButtonThatShowsUploadForm",
            label: "Web"
        },
        {
            tipL10nKey: "PublishTab.bloomPUBButton-tooltip",
            iconSrc: "/bloom/publish/PublishTab/BloomPUB.png",
            labelL10nKey: "PublishTab.bloomPUBButton",
            label: "BloomPUB"
        },
        {
            tipL10nKey: "PublishTab.EpubRadio-tooltip",
            iconSrc: "/bloom/publish/PublishTab/ePUBPublishButton.png",
            labelL10nKey: "PublishTab.EpubButton",
            label: "ePUB"
        },
        {
            tipL10nKey: "PublishTab.RecordVideoButton-tooltip",
            iconSrc: "/bloom/publish/PublishTab/publish video.png",
            labelL10nKey: "PublishTab.RecordVideoButton",
            label: "Audio or Video"
        }
    ];

    function logPublishTabSelected(idx: number) {
        if (idx < 0 || idx >= publishTabs.length) {
            postString(
                "logger/writeEvent",
                `Publish tab selected: ${idx} (unknown)`
            );
        } else {
            postString(
                "logger/writeEvent",
                `Publish tab selected: ${publishTabs[idx].label}`
            );
        }
    }

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div
                    css={css`
                        height: 100%;
                        width: 100%;
                        background-color: ${kBloomUnselectedTabBackground};
                    `}
                >
                    {altContent || (
                        <BloomTabs
                            id="tabs"
                            color="white"
                            selectedColor="white"
                            labelBackgroundColor={kPanelBackground}
                            selectedIndex={tabIndex}
                            onSelect={newIndex => {
                                post("publish/switchingPublishMode");
                                logPublishTabSelected(newIndex);
                                setTabIndex(newIndex);
                            }}
                            css={css`
                                height: 100%;
                                width: 100%;
                                display: flex;
                                flex-direction: row;
                                .react-tabs__tab-list {
                                    box-sizing: border-box;
                                    width: min-content;
                                    overflow-y: auto;
                                    display: flex;
                                    flex-direction: column;
                                    flex-shrink: 0;
                                    justify-content: flex-start; // keeps the first button up near the top of the page controls panel.
                                    align-items: center; // buttons will be in the center of the (side) panel.
                                    margin: 0px;
                                    padding: 0px;
                                    background-color: ${kPanelBackground};
                                    list-style-type: none;
                                }

                                .react-tabs__tab.react-tabs__tab {
                                    min-height: 80px;
                                    min-width: 100px;
                                    width: fit-content;
                                    flex: 0 0 auto;
                                    margin: 10px 10px;
                                    border: 3px solid transparent; // to be colored when selected
                                    border-radius: 10px;
                                    padding: 0;
                                    align-items: center;
                                    font-size: 14px;

                                    .sidebar-tab-label {
                                        display: block;
                                        margin: 5px;
                                        text-transform: none;
                                        text-align: center;
                                    }
                                    img {
                                        display: block;
                                        margin: 5px auto;
                                    }
                                }
                                .react-tabs__tab--selected {
                                    border-color: ${kBloomBlue} !important;
                                    font-weight: normal;
                                }
                                .react-tabs__tab-panel {
                                    flex-grow: 1;
                                }
                                .react-tabs__tab.react-tabs__tab--selected::after {
                                    // get rid of a white bar at the bottom of the icon (BL-12791)
                                    display: none;
                                }
                                .invisible_tab {
                                    display: none;
                                }
                            `}
                        >
                            <TabList>
                                {publishTabs.map((tab, index) => (
                                    <Tab key={index}>
                                        <BloomTooltip
                                            tip={{
                                                l10nKey: tab.tipL10nKey
                                            }}
                                        >
                                            <img src={tab.iconSrc} />
                                            <Span
                                                l10nKey={tab.labelL10nKey}
                                                className="sidebar-tab-label"
                                            >
                                                {tab.label}
                                            </Span>
                                        </BloomTooltip>
                                    </Tab>
                                ))}

                                <Tab className={"invisible_tab"}>
                                    {/* The default tab for before user has selected a publish mode. Should not be visible or clickable */}
                                </Tab>
                            </TabList>
                            <TabPanel>
                                <PDFPrintPublishScreen />
                            </TabPanel>
                            <TabPanel>
                                {publishTabInfo.canUpload ? (
                                    <LibraryPublishScreen />
                                ) : (
                                    <WarningBox
                                        css={css`
                                            width: fit-content;
                                            max-width: 400px;
                                            margin: 30px;
                                            padding-right: 20px;
                                        `}
                                    >
                                        <Div l10nKey="PublishTab.CannotUpload">
                                            The creator of this book does not
                                            allow derivatives to be uploaded.
                                            Please contact the creator for more
                                            information.
                                        </Div>
                                    </WarningBox>
                                )}
                            </TabPanel>
                            <TabPanel>
                                <ReaderPublishScreen />
                            </TabPanel>
                            <TabPanel>
                                <EPUBPublishScreen />
                            </TabPanel>
                            <TabPanel>
                                <PublishAudioVideo />
                            </TabPanel>
                            <TabPanel>
                                {/* Before user has selected a publish mode, show a blank panel */}
                                <div
                                    css={css`
                                        background-color: ${kBloomUnselectedTabBackground};
                                        width: 100%;
                                        height: 100%;
                                    `}
                                ></div>
                            </TabPanel>
                        </BloomTabs>
                    )}
                </div>
                <RegistrationDialogLauncher />
                <AboutDialogLauncher />
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

WireUpForWinforms(PublishTabPane);
