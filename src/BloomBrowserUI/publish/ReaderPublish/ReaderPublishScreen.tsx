/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState, useContext } from "react";

import {
    PreviewPanel,
    HelpGroup,
    SettingsPanel,
    CommandsGroup,
    UnderPreviewPanel
} from "../commonPublish/PublishScreenBaseComponents";
import { MethodChooser } from "./MethodChooser";
import { PublishFeaturesGroup } from "./PublishFeaturesGroup";
import { CoverColorGroup } from "./CoverColorGroup";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import { DeviceAndControls } from "../commonPublish/DeviceAndControls";
import ReactDOM = require("react-dom");
import { ThemeProvider } from "@material-ui/styles";
import { darkTheme, lightTheme } from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import {
    useSubscribeToWebSocketForStringMessage,
    useSubscribeToWebSocketForEvent
} from "../../utils/WebSocketManager";
import { BloomApi } from "../../utils/bloomApi";
import HelpLink from "../../react_components/helpLink";
import { Link, LinkWithDisabledStyles } from "../../react_components/link";
import {
    RequiresBloomEnterpriseAdjacentIconWrapper,
    RequiresBloomEnterpriseDialog
} from "../../react_components/requiresBloomEnterprise";
import { PublishProgressDialog } from "../commonPublish/PublishProgressDialog";
import { useL10n } from "../../react_components/l10nHooks";
import { ProgressState } from "../commonPublish/PublishProgressDialogInner";
import { PublishLanguagesGroup } from "./PublishLanguagesGroup";
import {
    BulkBloomPubDialog,
    showBulkBloomPubDialog
} from "./BulkBloomPub/BulkBloomPubDialog";
import { EmbeddedProgressDialog } from "../../react_components/Progress/ProgressDialog";
import { hookupLinkHandler } from "../../utils/linkHandler";

export const ReaderPublishScreen = () => {
    // When the user changes some features, included languages, etc., we
    // need to rebuild the book and re-run all of our Bloom API queries.
    // This requires a hard-reset of the whole screen, which we do by
    // incrementing a `key` prop on the core of this screen.
    const [keyForReset, setKeyForReset] = useState(0);
    return (
        <ReaderPublishScreenInternal
            key={keyForReset}
            onReset={() => {
                setKeyForReset(keyForReset + 1);
            }}
        />
    );
};

const ReaderPublishScreenInternal: React.FunctionComponent<{
    onReset: () => void;
}> = props => {
    const inStorybookMode = useContext(StorybookContext);
    const [heading, setHeading] = useState(
        useL10n("Creating Digital Book", "PublishTab.Android.Creating")
    );
    const [closePending, setClosePending] = useState(false);
    const [highlightRefresh, setHighlightRefresh] = useState(false);
    const [progressState, setProgressState] = useState(ProgressState.Working);
    React.useEffect(() => hookupLinkHandler(), []);

    // bookUrl is expected to be a normal, well-formed URL.
    // (that is, one that you can directly copy/paste into your browser and it would work fine)
    const [bookUrl, setBookUrl] = useState(
        inStorybookMode
            ? window.location.protocol +
                  "//" +
                  window.location.host +
                  "/templates/Sample Shells/The Moon and the Cap" // Enhance: provide an actual bloompub in the source tree
            : // otherwise, wait for the websocket to deliver a url when the c# has finished creating the bloompub.
              //BloomPlayer recognizes "working" as a special value; it will show some spinner or some such.
              "working"
    );

    const [defaultLandscape] = BloomApi.useApiBoolean(
        "publish/android/defaultLandscape",
        false
    );
    const [canRotate] = BloomApi.useApiBoolean(
        "publish/android/canRotate",
        false
    );
    useSubscribeToWebSocketForStringMessage(
        "publish-android",
        "androidPreview",
        url => {
            setBookUrl(url);
        }
    );

    const pathToOutputBrowser = inStorybookMode ? "./" : "../../";
    const usbWorking = useL10n("Publishing", "PublishTab.Common.Publishing");
    const wifiWorking = useL10n("Publishing", "PublishTab.Common.Publishing");

    useSubscribeToWebSocketForEvent(
        "publish-android",
        "publish/android/state",
        e => {
            switch (e.message) {
                case "stopped":
                    setClosePending(true);
                    break;
                case "UsbStarted":
                    setClosePending(false);
                    setHeading(usbWorking);
                    setProgressState(ProgressState.Serving);
                    break;
                case "ServingOnWifi":
                    setClosePending(false);
                    setHeading(wifiWorking);
                    setProgressState(ProgressState.Serving);
                    break;
                default:
                    throw new Error(
                        "Method Chooser does not understand the state: " +
                            e.message
                    );
            }
        }
    );

    const mainPanel = (
        <React.Fragment>
            <PreviewPanel>
                <ThemeProvider theme={darkTheme}>
                    <DeviceAndControls
                        defaultLandscape={defaultLandscape}
                        canRotate={canRotate}
                        url={
                            pathToOutputBrowser +
                            "bloom-player/dist/bloomplayer.htm?centerVertically=true&url=" +
                            encodeURIComponent(bookUrl) + // Need to apply encoding to the bookUrl again as data to use it as a parameter of another URL
                            "&independent=false" + // you can temporarily comment this out to send BloomPlayer analytics from Bloom Editor
                            "&host=bloomdesktop"
                        }
                        showRefresh={true}
                        highlightRefreshIcon={highlightRefresh}
                        onRefresh={() => props.onReset()}
                    />
                </ThemeProvider>
            </PreviewPanel>
            <UnderPreviewPanel
                css={css`
                    display: block;
                    flex-grow: 1;
                `}
            >
                <MethodChooser />
            </UnderPreviewPanel>
        </React.Fragment>
    );

    const optionsPanel = (
        <SettingsPanel>
            <PublishFeaturesGroup
                onChange={() => {
                    props.onReset();
                }}
            />
            <CoverColorGroup onChange={() => props.onReset()} />
            <PublishLanguagesGroup onChange={() => setHighlightRefresh(true)} />
            {/* push everything to the bottom */}
            <div
                css={css`
                    margin-top: auto;
                `}
            />
            <CommandsGroup>
                <RequiresBloomEnterpriseAdjacentIconWrapper>
                    <LinkWithDisabledStyles
                        l10nKey="PublishTab.BulkBloomPub.MakeAllBloomPubs"
                        onClick={() => {
                            showBulkBloomPubDialog();
                        }}
                    >
                        Make All BloomPUBs from Collection
                    </LinkWithDisabledStyles>
                </RequiresBloomEnterpriseAdjacentIconWrapper>
            </CommandsGroup>
            <HelpGroup>
                <HelpLink
                    l10nKey="PublishTab.Android.AboutBloomPUB"
                    helpId="Tasks/Publish_tasks/Make_a_BloomPUB_file_overview.htm"
                >
                    About BloomPUB
                </HelpLink>
                <HelpLink
                    l10nKey="PublishTab.Android.AboutBloomReader"
                    helpId="Concepts/Bloom_Reader_App.htm"
                >
                    About Bloom Reader
                </HelpLink>
                <HelpLink
                    l10nKey="PublishTab.TasksOverview"
                    helpId="Tasks/Publish_tasks/Publish_tasks_overview.htm"
                >
                    Publish tab tasks overview
                </HelpLink>
                <div className="icon-link-row get-bloom-reader">
                    <a href="https://play.google.com/store/search?q=%22sil%20international%22%2B%22bloom%20reader%22&amp;c=apps">
                        <img
                            css={css`
                                height: 1.5em;
                                margin-right: 10px;
                            `}
                            src="/bloom/images/Google_Play_symbol_2016.svg"
                        />
                    </a>
                    <Link
                        id="getBloomReaderLink"
                        href="https://play.google.com/store/search?q=%22sil%20international%22%2B%22bloom%20reader%22&amp;c=apps"
                        l10nKey="PublishTab.Android.GetBloomReader"
                        l10nComment="Link to find Bloom Reader on Google Play Store"
                    >
                        Get Bloom Reader App
                    </Link>
                </div>
            </HelpGroup>
        </SettingsPanel>
    );

    return (
        <React.Fragment>
            <BulkBloomPubDialog />
            <RequiresBloomEnterpriseDialog />
            <PublishScreenTemplate
                bannerTitleEnglish="Publish as BloomPUB"
                bannerTitleL10nId="PublishTab.BloomPUB.BannerTitle"
                // The BloomPUB Viewer link "should" jump to the "Related Software" section.
                // Unfortunately our blorg load process doesn't seem to handle hash suffixes yet.
                // Perhaps someday we'll figure out how to do that.
                bannerDescriptionMarkdown="BloomPUBs are a kind of eBook that can be read on phone apps ([Bloom Reader](https://bloomlibrary.org/page/create/bloom-reader) and [Reading App Builder](https://software.sil.org/readingappbuilder/)) and on Windows using [BloomPUB Viewer](https://bloomlibrary.org/page/create/downloads#related-software). They offer many advantages over ePUBs, including WYSIWYG fidelity and the full range of Bloom digital book features."
                bannerDescriptionL10nId="PublishTab.BloomPUB.BannerDescription"
                optionsPanelContents={optionsPanel}
            >
                {mainPanel}
            </PublishScreenTemplate>
            {/* In storybook, there's no bloom backend to run the progress dialog */}
            {inStorybookMode || (
                <PublishProgressDialog
                    heading={heading}
                    startApiEndpoint="publish/android/updatePreview"
                    webSocketClientContext="publish-android"
                    progressState={progressState}
                    setProgressState={setProgressState}
                    closePending={closePending}
                    setClosePending={setClosePending}
                    onUserStopped={() => {
                        BloomApi.postData("publish/android/usb/stop", {});
                        BloomApi.postData("publish/android/wifi/stop", {});
                        setClosePending(true);
                    }}
                />
            )}
            <EmbeddedProgressDialog id="readerPublish" />
        </React.Fragment>
    );
};

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("BloomReaderPublishScreen")) {
    ReactDOM.render(
        <ThemeProvider theme={lightTheme}>
            <ReaderPublishScreen />
        </ThemeProvider>,
        document.getElementById("BloomReaderPublishScreen")
    );
}
