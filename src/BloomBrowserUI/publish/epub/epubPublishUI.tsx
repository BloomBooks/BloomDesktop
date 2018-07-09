import { BloomApi } from "../../utils/bloomApi";
import * as React from "react";
import * as ReactDOM from "react-dom";
import ProgressBox from "../../react_components/progressBox";
import BloomButton from "../../react_components/bloomButton";
import { ApiBackedCheckbox } from "../../react_components/apiBackedCheckbox";
import { Checkbox } from "../../react_components/checkbox";
import Link from "../../react_components/link";
import HelpLink from "../../react_components/helpLink";
import { H1, H2, IUILanguageAwareProps } from "../../react_components/l10n";
import "./epubPublishUI.less";
import EpubPreview from "./EpubPreview";
// import { RadioGroup, Radio } from "../../react_components/radio";
import WebSocketManager from "../../utils/WebSocketManager";

const kWebSocketClientContext = "publish-epub";

interface IPublishSettings {
    howToPublishImageDescriptions: string; // one of "None", "OnPage", "Links"
    removeFontSizes: boolean;
}

// This is a screen of controls that gives the user instructions and controls
// for creating epubs
class EpubPublishUI extends React.Component<
    IUILanguageAwareProps,
    IPublishSettings
> {
    private isLinux: boolean;
    constructor(props: IUILanguageAwareProps) {
        super(props);
        this.state = {
            howToPublishImageDescriptions: "None",
            removeFontSizes: false
        };

        BloomApi.get("publish/epub/epubSettings", result => {
            this.setState(result.data);
        });
    }

    private readyToReceiveProgress() {
        // once the progress box is ready, we can start generating a preview.
        // If we don't wait for that, it's pretty random whether we get the
        // "preparing preview" message.
        BloomApi.postData("publish/epub/updatePreview", this.state);
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

    private componentCleanup() {
        WebSocketManager.closeSocket(kWebSocketClientContext);
    }

    public render() {
        return (
            <div id="epubPublishReactRoot" className={"screen-root"}>
                <header>
                    <img src="epub.png" />
                    <H1 l10nKey="PublishTab.Epub.Title">Create an ePUB book</H1>
                </header>
                <div className="sections">
                    <section className="preview-section">
                        <H1 l10nKey="Common.Preview">Preview</H1>
                        <EpubPreview
                            websocketClientContext={kWebSocketClientContext}
                        />
                    </section>
                    <section className="publish-section">
                        <H1 l10nKey="PublishTab.Publish">Publish</H1>
                        <BloomButton
                            className="save-button"
                            enabled={true}
                            clickEndpoint={"publish/epub/save"}
                            hasText={true}
                            l10nKey="PublishTab.Save"
                        >
                            Save...
                        </BloomButton>
                        <div
                            id="progress-section"
                            style={{ visibility: "visible" }}
                        >
                            <H2 className="label" l10nKey="Common.Progress">
                                Progress
                            </H2>
                            <ProgressBox
                                clientContext={kWebSocketClientContext}
                                onReadyToReceive={() =>
                                    this.readyToReceiveProgress()
                                }
                            />
                        </div>
                    </section>
                    <div className="column">
                        <section className="help-section">
                            <H1 l10nKey="Common.Help">Help</H1>
                            <HelpLink
                                l10nKey="PublishTab.Epub.Help.AboutEpubs"
                                helpId="Concepts/EPUB.htm"
                            >
                                About ePUBs
                            </HelpLink>
                            <HelpLink
                                l10nKey="PublishTab.Epub.BooksForBlind"
                                helpId="Tasks/Publish_tasks/Create_an_ePUB_book_overview.htm"
                            >
                                Books for the Blind
                            </HelpLink>
                            <HelpLink
                                l10nKey="PublishTab.Epub.Help.EReaders"
                                helpId="Concepts/Epub_Readers.htm"
                            >
                                Compatible ePUB Readers
                            </HelpLink>
                            <HelpLink
                                l10nKey="PublishTab.Epub.Help.Publishing"
                                helpId="Tasks/Publish_tasks/Digital_publishing_options.htm"
                            >
                                Getting ePUBs onto a device
                            </HelpLink>
                        </section>
                        <section
                            className={"settings-section section-below-another"}
                        >
                            <H1 l10nKey="Common.Settings">Settings</H1>{" "}
                        </section>
                        <H1 l10nKey="PublishTab.Epub.BooksForBlind">
                            Books for the Blind
                        </H1>
                        {/* Can't use ApiBackedCheckbox here, because it is backed by an enum. */}
                        <Checkbox
                            name="includeImageDesc"
                            checked={
                                this.state.howToPublishImageDescriptions ===
                                "OnPage"
                            }
                            onCheckChanged={val =>
                                this.setPublishRadio(val ? "OnPage" : "None")
                            }
                            l10nKey="PublishTab.Epub.IncludeOnPage"
                        >
                            Include image descriptions on page
                        </Checkbox>
                        <ApiBackedCheckbox
                            apiEndpoint="publish/epub/removeFontSizesSetting"
                            l10nKey="PublishTab.Epub.RemoveFontSizes"
                        >
                            Use ePUB reader's text size
                        </ApiBackedCheckbox>
                        {/* l10nKey is intentionally not under PublishTab.Epub... we may end up with this link in other places */}
                        <Link
                            id="a11yCheckerLink"
                            l10nKey="AccessibilityCheck.AccessibilityChecker"
                            onClick={() =>
                                BloomApi.post(
                                    "accessibilityCheck/showAccessibilityChecker"
                                )
                            }
                        >
                            Accessibility Checker
                        </Link>
                    </div>
                </div>
            </div>
        );
    }

    // To restore the image description links option, add
    // <Radio
    //     l10nKey="PublishTab.Epub.Links"
    //     value="links">
    //     Image description links
    // </Radio>

    // This slightly obsolete name reflects the possibility of more than two modes requiring a set of radio buttons
    // (e.g., the implemented but not shipped "links" option)
    private setPublishRadio(val: string) {
        this.setState({ howToPublishImageDescriptions: val });
        BloomApi.postDataWithConfig(
            "publish/epub/imageDescriptionSetting",
            val,
            { headers: { "Content-Type": "application/json" } }
        );
    }
}

// a bit goofy... currently the html loads everying in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("epubPublishUI")) {
    ReactDOM.render(
        <EpubPublishUI />,
        document.getElementById("epubPublishUI")
    );
}
