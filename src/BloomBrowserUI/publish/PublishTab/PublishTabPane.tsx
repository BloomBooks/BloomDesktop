/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import ReactDOM = require("react-dom");
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
import { get, post } from "../../utils/bloomApi";
import { useSubscribeToWebSocketForEvent } from "../../utils/WebSocketManager";
import { LibraryPublishScreen } from "../LibraryPublish/LibraryPublishScreen";
import { PDFPrintPublishScreen } from "../PDFPrintPublish/PDFPrintPublishScreen";
import { PublishAudioVideo } from "../video/PublishAudioVideo";
import { EPUBPublishScreen } from "../ePUBPublish/ePUBPublishScreen";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { NoteBox, WarningBox } from "../../react_components/boxes";
import { kBloomUnselectedTabBackground } from "../../utils/colorUtils";

export const EnterpriseNeededScreen: React.FunctionComponent<{
    titleForDisplay: string;
    firstOverlayPage: number;
}> = props => {
    const needsEnterpriseText1 = useL10n(
        "The book titled '{0}' adds new Overlay elements. Overlay elements are a Bloom Enterprise feature.",
        "PublishTab.PublishRequiresEnterprise.ProblemExplanation",
        "",
        props.titleForDisplay
    );

    const needsEnterpriseText2 = useL10n(
        "In order to publish your book, you need to either activate Bloom Enterprise, or remove the Overlay elements from your book.",
        "PublishTab.PublishRequiresEnterprise.Options"
    );

    const needsEnterpriseText3 = useL10n(
        "Page {0} is the first page that uses Overlay elements.",
        "PublishTab.PublishRequiresEnterprise.FirstOverlayPage",
        "",
        "" + props.firstOverlayPage
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
                        l10nKey="Common.EnterpriseRequired"
                        css={css`
                            margin-top: 0;
                        `}
                    >
                        Enterprise Required
                    </H2>
                    <p>{needsEnterpriseText1}</p>
                    <p>{needsEnterpriseText2}</p>
                    <p>{needsEnterpriseText3}</p>
                </div>
            </NoteBox>
        </div>
    );
};

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

export const PublishTabPane: React.FunctionComponent<{}> = () => {
    const kWaitForUserToChooseTabIndex = 5;

    const [publishTabReady, setPublishTabReady] = React.useState(false);
    const [publishTabInfo, setPublishTabInfo] = React.useState({
        enterpriseNeeded: false,
        checkoutNeeded: false,
        canUpload: false,
        bookTitle: "",
        firstOverlayPage: 0
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
                enterpriseNeeded: result.data.cannotPublishWithoutEnterprise,
                checkoutNeeded: result.data.cannotPublishWithoutCheckout,
                canUpload: result.data.canUpload,
                bookTitle: result.data.titleForDisplay,
                firstOverlayPage: result.data.numberOfFirstPageWithOverlay
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
    } else if (publishTabInfo.enterpriseNeeded) {
        altContent = (
            <EnterpriseNeededScreen
                titleForDisplay={publishTabInfo.bookTitle}
                firstOverlayPage={publishTabInfo.firstOverlayPage}
            />
        );
    } else if (publishTabInfo.checkoutNeeded) {
        altContent = (
            <CheckoutNeededScreen titleForDisplay={publishTabInfo.bookTitle} />
        );
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
                                <Tab>
                                    <BloomTooltip
                                        tip={{
                                            l10nKey:
                                                "PublishTab.PdfPrintButton-tooltip"
                                        }}
                                    >
                                        <img src="/bloom/publish/PublishTab/PdfPrint.png" />
                                        <Span
                                            l10nKey="PublishTab.PdfPrint.Button"
                                            className="sidebar-tab-label"
                                        >
                                            PDF & Print
                                        </Span>
                                    </BloomTooltip>
                                </Tab>
                                <Tab>
                                    <BloomTooltip
                                        tip={{
                                            l10nKey:
                                                "PublishTab.ButtonThatShowsUploadForm-tooltip"
                                        }}
                                    >
                                        <img src="/bloom/publish/PublishTab/upload.png" />
                                        <Span
                                            l10nKey="PublishTab.ButtonThatShowsUploadForm"
                                            className="sidebar-tab-label"
                                        >
                                            Web
                                        </Span>
                                    </BloomTooltip>
                                </Tab>
                                <Tab>
                                    <BloomTooltip
                                        tip={{
                                            l10nKey:
                                                "PublishTab.bloomPUBButton-tooltip"
                                        }}
                                    >
                                        <img src="/bloom/publish/PublishTab/BloomPUB.png" />
                                        <Span
                                            l10nKey="PublishTab.bloomPUBButton"
                                            className="sidebar-tab-label"
                                        >
                                            BloomPUB
                                        </Span>
                                    </BloomTooltip>
                                </Tab>
                                <Tab>
                                    <BloomTooltip
                                        tip={{
                                            l10nKey:
                                                "PublishTab.EpubRadio-tooltip"
                                        }}
                                    >
                                        <img src="/bloom/publish/PublishTab/ePUBPublishButton.png" />
                                        <Span
                                            l10nKey="PublishTab.EpubButton"
                                            className="sidebar-tab-label"
                                        >
                                            ePUB
                                        </Span>
                                    </BloomTooltip>
                                </Tab>
                                <Tab>
                                    <BloomTooltip
                                        tip={{
                                            l10nKey:
                                                "PublishTab.RecordVideoButton-tooltip"
                                        }}
                                    >
                                        <img src="/bloom/publish/PublishTab/publish video.png" />
                                        <Span
                                            l10nKey="PublishTab.RecordVideoButton"
                                            className="sidebar-tab-label"
                                        >
                                            Audio or Video
                                        </Span>
                                    </BloomTooltip>
                                </Tab>

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
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

WireUpForWinforms(PublishTabPane);
