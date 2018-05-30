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

interface IState {
    publishImageDescriptions: string; // one of "none", "onPage", "links"
}

// This is a screen of controls that gives the user instructions and controls
// for creating epubs
class EpubPublishUI extends React.Component<IUILanguageAwareProps, IState> {
    private isLinux: boolean;
    constructor(props) {
        super(props);
        this.state = { publishImageDescriptions: "none" };

        WebSocketManager.addListener(kWebSocketLifetime, event => {
            var e = JSON.parse(event.data);
            if (e.id === "publish/epub/state") {
                this.setState({ publishImageDescriptions: e.payload });
            }
        });
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
        // axios.post("/bloom/api/publish/epub/cleanup").then(result => {
        //     WebSocketManager.closeSocket(kWebSocketLifetime);
        // });
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
                            clickEndpoint={"publish/epub/save?publishImageDescription=" + this.state.publishImageDescriptions}
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
                            <ProgressBox lifetimeLabel={kWebSocketLifetime} />
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
                        <H1 l10nKey="PublishTab.Epub.BooksForBlind">Books for the Blind</H1>
                        <RadioGroup
                            onChange={val => this.setPublishRadio(val)}
                            value={this.state.publishImageDescriptions}>
                            <Radio
                                l10nKey="PublishTab.Epub.NoAudioDescriptions"
                                value="none">
                                No recorded audio image descriptions
                            </Radio>
                            <Radio
                                l10nKey="PublishTab.Epub.IncludeOnPage"
                                value="onPage">
                                Include image descriptions on page
                            </Radio>
                        </RadioGroup>
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

    private setPublishRadio(val: string) {
        if (val === this.state.publishImageDescriptions) return;
        this.setState({ publishImageDescriptions: val });
        axios.post("/bloom/api/publish/epub/imageDescription?publishImageDescription=" + val);
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