import { css } from "@emotion/react";
import * as React from "react";
import {
    kBloomBlue,
    kPanelBackground,
    lightTheme,
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
import { AppPublisherScreen } from "../Apps/AppPublisherScreen";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { NoteBox, WarningBox } from "../../react_components/boxes";
import { kBloomUnselectedTabBackground } from "../../utils/colorUtils";
import { PublishingBookRequiresHigherTierNotice } from "./PublishingBookRequiresHigherTierNotice";
import {
    FeatureStatus,
    useGetFeatureStatus,
} from "../../react_components/featureStatus";
import { AboutDialogLauncher } from "../../react_components/aboutDialog";
import { RegistrationDialogEventLauncher } from "../../react_components/registration/registrationDialogLauncher";
import { RequiresSubscriptionOverlayWrapper } from "../../react_components/requiresSubscription";
import { useWorkspaceTabInfo } from "../../react_components/TopBar/TopBar";

export const CheckoutNeededScreen: React.FunctionComponent<{
    titleForDisplay: string;
}> = (_props) => {
    const needsCheckoutText1 = useL10n(
        "Please check out this book from the Team Collection before publishing it.",
        "TeamCollection.CheckoutRequiredExplanation",
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
    // Start on a sentinel index until C# tells us which tab should be active for the current book.
    // This avoids flashing the last book's publish mode while new tab info loads.
    const kWaitForUserToChooseTabIndex = 6;

    // Temporary: notify c# about clicks so WinForms menus can close.
    // Remove this once menus move into the same browser UI.
    React.useEffect(() => {
        const notifyBrowserClicked = () => {
            (
                window as Window & {
                    chrome?: {
                        webview?: { postMessage(message: string): void };
                    };
                }
            ).chrome?.webview?.postMessage("browser-clicked");
        };

        window.addEventListener("click", notifyBrowserClicked);
        return () => {
            window.removeEventListener("click", notifyBrowserClicked);
        };
    }, []);

    const [publishTabReady, setPublishTabReady] = React.useState(false);
    const [publishTabInfo, setPublishTabInfo] = React.useState({
        checkoutNeeded: false,
        canUpload: false,
        bookTitle: "",
        featurePreventingPublishing: undefined as FeatureStatus | undefined,
    });
    const [tabIndex, setTabIndex] = React.useState(
        kWaitForUserToChooseTabIndex,
    );
    // True while a long-running publish operation has made itself modal by locking navigation:
    // a BloomLibrary upload, or one of the Apps tool's Reading App Builder actions. C# owns this
    // flag (it is the same one that greys out the main workspace tabs), so the publish tools
    // unlock at exactly the moment the operation really finishes or is cancelled — not when the
    // browser guesses it has. See BL-16654.
    const navigationLocked = useWorkspaceTabInfo().navigationLocked;
    // The Web tool tells us directly when the user has committed to an upload, because C# does
    // not take its lock until a couple of API round trips later, leaving a window where the
    // screen already shows Cancel but the tools were still clickable (BL-16654).
    const [uploadUnderway, setUploadUnderway] = React.useState(false);
    // OR, never AND. C#'s flag is the authority on when an operation has really finished, and
    // uploadUnderway is deliberately only ever an *additional* reason to lock: it is unreliable
    // as an unlock signal (it clears the moment Cancel is pressed, and on any error line in the
    // progress log) but adding it can only lock more than C# alone would, never less.
    //
    // Both are then gated on a tool actually showing. The lock exists to stop the user walking
    // away from an operation in progress, and none can be in progress while the sentinel "no tool
    // chosen yet" panel is up. That gate matters because the C# flag is shared with other
    // subsystems — e.g. the Copyright and License dialog, reachable from this tab's own "Missing
    // Copyright" link, posts editView/setModalState, which locks. Without it, a lock still set
    // while tabIndex is the sentinel would grey out every tool at once and leave the user no way
    // to choose one at all.
    const publishToolsLocked =
        (navigationLocked || uploadUnderway) &&
        tabIndex !== kWaitForUserToChooseTabIndex;
    const appBuilderFeatureStatus = useGetFeatureStatus("AppBuilder");
    const setup = () => {
        setTabIndex(kWaitForUserToChooseTabIndex);
        get("publish/getInitialPublishTabInfo", (result) => {
            // There should be a current selection by now but just in case:
            if (!result.data) {
                return;
            }
            setPublishTabInfo({
                checkoutNeeded: result.data.cannotPublishWithoutCheckout,
                canUpload: result.data.canUpload,
                bookTitle: result.data.titleForDisplay,
                featurePreventingPublishing:
                    result.data.featurePreventingPublishing,
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
        id?: string;
        hidden?: boolean;
    }
    const publishTabs: PublishTabProps[] = [
        {
            tipL10nKey: "PublishTab.PdfPrintButton-tooltip",
            iconSrc: "/bloom/publish/PublishTab/PdfPrint.png",
            labelL10nKey: "PublishTab.PdfPrint.Button",
            label: "PDF & Print",
        },
        {
            tipL10nKey: "PublishTab.ButtonThatShowsUploadForm-tooltip",
            iconSrc: "/bloom/publish/PublishTab/upload.png",
            labelL10nKey: "PublishTab.ButtonThatShowsUploadForm",
            label: "Web",
        },
        {
            tipL10nKey: "PublishTab.bloomPUBButton-tooltip",
            iconSrc: "/bloom/publish/PublishTab/BloomPUB.png",
            labelL10nKey: "PublishTab.bloomPUBButton",
            label: "BloomPUB",
        },
        {
            tipL10nKey: "PublishTab.Apps-tooltip",
            iconSrc: "/bloom/publish/PublishTab/AppsPublishButton.svg",
            labelL10nKey: "PublishTab.Apps",
            label: "Apps",
            id: "apps",
            hidden: !appBuilderFeatureStatus?.visible,
        },
        {
            tipL10nKey: "PublishTab.EpubRadio-tooltip",
            iconSrc: "/bloom/publish/PublishTab/ePUBPublishButton.png",
            labelL10nKey: "PublishTab.EpubButton",
            label: "ePUB",
        },
        {
            tipL10nKey: "PublishTab.RecordVideoButton-tooltip",
            iconSrc: "/bloom/publish/PublishTab/publish video.png",
            labelL10nKey: "PublishTab.RecordVideoButton",
            label: "Audio or Video",
        },
    ];

    function logPublishTabSelected(idx: number) {
        if (idx < 0 || idx >= publishTabs.length) {
            postString(
                "logger/writeEvent",
                `Publish tab selected: ${idx} (unknown)`,
            );
        } else {
            postString(
                "logger/writeEvent",
                `Publish tab selected: ${publishTabs[idx].label}`,
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
                            onSelect={(newIndex) => {
                                // While an upload or an Apps action is running (its Cancel button is
                                // showing), the operation is modal: veto switching to another publish
                                // tool until it finishes or is cancelled. The main workspace tabs are
                                // locked from C# by the same flag.
                                if (publishToolsLocked) {
                                    return false;
                                }
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
                                // Doubled class for enough specificity to override the tab color
                                // rule above, so tools disabled during a modal operation read as
                                // greyed out (react-tabs already makes them non-clickable).
                                .react-tabs__tab--disabled.react-tabs__tab--disabled {
                                    opacity: 0.4;
                                    cursor: default;
                                }
                            `}
                        >
                            {/* Dark panel: the far-left tab strip scrolls and is
                                dark, so opt it into Bloom's shared dark scrollbar
                                style (bloomUI.less). The class goes on the tab-list
                                only, not on BloomTabs, so the light tab panels
                                (which are siblings, not descendants) are unaffected.
                                react-tabs supplies "react-tabs__tab-list" only as a
                                default className, so a custom className replaces it;
                                we must repeat it here to keep the tab-list styling. */}
                            <TabList className="react-tabs__tab-list bloomDarkScrollbars">
                                {publishTabs.map((tab, index) => (
                                    <Tab
                                        key={index}
                                        // Grey out the other publish tools while a modal operation
                                        // is running, so it's clear why they don't respond. The tool
                                        // the operation belongs to stays looking normal, the same way
                                        // C# leaves the active workspace tab looking active.
                                        disabled={
                                            publishToolsLocked &&
                                            index !== tabIndex
                                        }
                                        className={
                                            tab.hidden
                                                ? "invisible_tab"
                                                : undefined
                                        }
                                    >
                                        <BloomTooltip
                                            tip={{
                                                l10nKey: tab.tipL10nKey,
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
                                    <LibraryPublishScreen
                                        onUploadingChange={setUploadUnderway}
                                    />
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
                                <RequiresSubscriptionOverlayWrapper featureName="AppBuilder">
                                    <AppPublisherScreen
                                        isActive={
                                            publishTabs[tabIndex]?.id === "apps"
                                        }
                                    />
                                </RequiresSubscriptionOverlayWrapper>
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
                <RegistrationDialogEventLauncher />
                <AboutDialogLauncher />
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

WireUpForWinforms(PublishTabPane, kBloomUnselectedTabBackground);
