import * as React from "react";
import { IUILanguageAwareProps } from "../../react_components/l10n";

import "./accessibilityChecklist.less";
import { CheckItem } from "./checkItem";
import { ApiBackedCheckbox } from "../../react_components/apiBackedCheckbox";
import WebSocketManager from "../../utils/WebSocketManager";

interface IState {
    // This is something of a hack... we increase it whenever
    // we want to refresh and have our children go ask the server
    // for updated data. We do that by including "refreshCount" as
    // one of their props.
    refreshCount: number;
}
export class AccessibilityChecklist extends React.Component<
    IUILanguageAwareProps,
    IState
> {
    constructor(props) {
        super(props);
        this.state = { refreshCount: 0 };
    }
    public componentDidMount() {
        // Listen for changes to state from C#-land
        // Notice that at this time, we don't even pay attention
        // to the content of the message, as "refresh" is all there is
        // and all we can anticipate needing.
        WebSocketManager.addListener("a11yChecklist", event => {
            this.setState({
                refreshCount: this.state.refreshCount + 1
            });
            this.forceUpdate();
        });
    }

    public render() {
        return (
            <div className="checkList">
                <section>
                    <h1>Bloom can automatically check these for you</h1>
                    <CheckItem
                        refreshCount={this.state.refreshCount}
                        apiCheckName="audioForAllText"
                        label="Audio for all text"
                    />
                    <CheckItem
                        refreshCount={this.state.refreshCount}
                        apiCheckName="descriptionsForAllImages"
                        label="Descriptions for all images"
                    />
                    <CheckItem
                        refreshCount={this.state.refreshCount}
                        apiCheckName="audioForAllImageDescriptions"
                        label="Audio for all image descriptions"
                    />
                    {/* <CheckItem apirUrl="automatedEpubCheck" label="Automated Epub Check" /> */}
                </section>
                <section>
                    <h1>You need to check these yourself:</h1>
                    {this.addCheck(
                        "noEssentialInfoByColor",
                        "No essential information by color"
                    )}
                    {/* This check makes sense in the TTS world, but in the Bloom world, we are saying you have
                        to have image descriptions and recordings of all images anyways. So even if there was
                        important text in an image, the image description should describe it.
                    {this.addCheck(
                        "noTextIncludedInAnyImages",
                        "No text included in any images"
                    )} */}
                </section>
            </div>
        );
    }
    private addCheck(key: string, english: string): JSX.Element {
        return (
            <ApiBackedCheckbox
                refreshCount={this.state.refreshCount}
                l10nKey={"Accessibility." + key}
                apiPath={"/bloom/api/accessibilityCheck/" + key}
            >
                {english}
            </ApiBackedCheckbox>
        );
    }
}
