import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import ProgressBox from "../../react_components/progressBox";
import BloomButton from "../../react_components/bloomButton";
import { Checkbox } from "../../react_components/checkbox";
import Option from "../../react_components/option";
import Link from "../../react_components/link";
import HelpLink from "../../react_components/helpLink";
import HtmlHelpLink from "../../react_components/htmlHelpLink";
import {
    H1,
    H2,
    Div,
    IUILanguageAwareProps
} from "../../react_components/l10n";
import WebSocketManager from "../../utils/WebSocketManager";
import "./epubPublishUI.less";
import EpubPreview from "./EpubPreview";
import { RadioGroup, Radio } from "../../react_components/radio";

const kWebSocketLifetime = "publish-epub";

interface PublishSettings {
    howToPublishImageDescriptions: string; // one of "None", "OnPage", "Links"
    removeFontSizes: boolean;
}

interface IState {
    settings: PublishSettings;
}

// This is a screen of controls that gives the user instructions and controls
// for creating epubs
class EpubPublishUI extends React.Component<IUILanguageAwareProps, IState> {
    private isLinux: boolean;
    constructor(props: IUILanguageAwareProps) {
        super(props);
        this.state = {
            settings: {
                howToPublishImageDescriptions: "None",
                removeFontSizes: false
            }
        };

        axios.get("/bloom/api/publish/epub/epubSettings").then(result => {
            this.setState({ settings: result.data });
        });
    }

    private readyToReceiveProgress() {
        // once the progress box is ready, we can start generating a preview.
        // If we don't wait for that, it's pretty random whether we get the
        // "preparing preview" message.
        axios.post(
            "/bloom/api/publish/epub/updatePreview",
            this.state.settings
        );
    }

    public render() {
        return (
            <div id="epubPublishReactRoot" className={"screen-root"}>
                <header>
                    <img src="epub.png" />
                    <H1 l10nKey="PublishTab.epub.Title">Create an Epub book</H1>
                </header>
                <div className="sections">
                    <section className="preview-section">
                        <H1 l10nKey="PublishTab.Preview">Preview</H1>
                        <EpubPreview lifetimeLabel={kWebSocketLifetime} />
                    </section>
                    <section className="publish-section">
                        {/* todo: pick correct l10nkey... same as tab? */}
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
                                lifetimeLabel={kWebSocketLifetime}
                                onReadyToReceive={() =>
                                    this.readyToReceiveProgress()
                                }
                            />
                        </div>
                    </section>
                    <div className="column">
                        <section className="help-section">
                            <H1 l10nKey="Common.Help">Help</H1>
                            <HtmlHelpLink
                                l10nKey="PublishTab.Epub.Help.AboutEpubs"
                                fileid="todo"
                            >
                                About Epubs
                            </HtmlHelpLink>
                            <HtmlHelpLink
                                l10nKey="PublishTab.Epub.Help.todo"
                                fileid="todo"
                            >
                                Compatible Epub Readers
                            </HtmlHelpLink>
                            <HtmlHelpLink
                                l10nKey="PublishTab.Epub.Help.todo"
                                fileid="todo"
                            >
                                Gettings Epubs onto a device
                            </HtmlHelpLink>
                        </section>
                        <section
                            className={"settings-section section-below-another"}
                        >
                            {/* todo: pick correct l10nkey */}
                            <H1 l10nKey="Common.Settings">Settings</H1>{" "}
                        </section>
                        <H1 l10nKey="PublishTab.Epub.BooksForBlind">
                            Books for the Blind
                        </H1>
                        <Checkbox
                            name="includeImageDesc"
                            checked={
                                this.state.settings
                                    .howToPublishImageDescriptions === "OnPage"
                            }
                            onCheckChanged={val =>
                                this.setPublishRadio(val ? "OnPage" : "None")
                            }
                            l10nKey="PublishTab.Epub.IncludeOnPage"
                        >
                            Include image descriptions on page
                        </Checkbox>
                        <Checkbox
                            name="removeFontSizes"
                            checked={this.state.settings.removeFontSizes}
                            onCheckChanged={val => this.setRemoveFontSizes(val)}
                            l10nKey="PublishTab.Epub.RemoveFontSizes"
                        >
                            Use epub reader's text size
                        </Checkbox>
                        {/* l10nKey is intentionally not under PublishTab.Epub... we may end up with this link in other places */}
                        <Link
                            id="a11yCheckerLink"
                            l10nKey="AccessibilityCheck.ShowAccessibilityChecker"
                            onClick={() =>
                                axios.post(
                                    "/bloom/api/accessibilityCheck/showAccessibilityChecker"
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
        if (val === this.state.settings.howToPublishImageDescriptions) return;
        // We want to keep the old settings except for the one we want to modify.
        // SetState will do this itself at the top level, but we are changing something one level down.
        var merged = { ...this.state.settings }; // clone, keep other settings
        merged.howToPublishImageDescriptions = val;
        this.setState({ settings: merged });
        axios.post("/bloom/api/publish/epub/epubSettings", merged); // not this.state.settings, which is updated asynchronously later
    }

    private setRemoveFontSizes(val: boolean): void {
        var merged = { ...this.state.settings }; // clone
        merged.removeFontSizes = val;
        this.setState({ settings: merged });
        axios.post("/bloom/api/publish/epub/epubSettings", merged);
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
