import * as React from "react";
import {
    IUILanguageAwareProps,
    H1
} from "../../react_components/l10nComponents";

import "./accessibilityChecklist.less";
import { CheckItem } from "./checkItem";
import { ApiBackedCheckbox } from "../../react_components/apiBackedCheckbox";
import WebSocketManager from "../../utils/WebSocketManager";
import "errorHandler";

export class AccessibilityChecklist extends React.Component<
    IUILanguageAwareProps
> {
    constructor(props) {
        super(props);
    }

    // C# land will send us a command when something has changed that would
    // warrant us refreshing: either the user has changd to a different book,
    // or the book has changed (e.g. the user has fixed something that we
    // we pointed out).
    // React doesn't have a super natural way of doing this. That is probably
    // because react is normally used in the context of a state system like
    // redux or mobx. In this case, our code (both c# and ts) is much simpler
    // if we just let each child do its own querying of the data (this is NOT
    // obviously true, but JH and JT, pairing agreed that things were getting
    // steadily more complex when we tried that route.)
    // Some react ways to force a child to update include:
    // * providing and then changing "key" attributes on each child
    // * capturing "refs" of each child, then calling some public method on the child
    // * providing a prop that is only used to trick the child into refreshing
    // Each of these were tried, the last was perhaps the best. In the end we
    // went looking for some kind of observer approach, and that led to this.
    // We give each child the following function. The child then calls it, providing
    // a function that causes it to refresh.
    private subscribeChildToRefreshEvent(childRefreshFunction) {
        WebSocketManager.addListener("a11yChecklist", event => {
            if (
                event.id === "bookSelectionChanged" ||
                event.id === "bookContentsMayHaveChanged"
            ) {
                childRefreshFunction();
            }
        });
    }

    public render() {
        return (
            <div className="checkList">
                <section>
                    <H1 l10nKey="AccessibilityCheck.AutomaticChecksHeading">
                        Bloom can automatically check these for you:
                    </H1>
                    <CheckItem
                        subscribeToRefresh={this.subscribeChildToRefreshEvent}
                        apiCheckName="audioForAllText"
                        label="Audio for all text"
                    />
                    <CheckItem
                        subscribeToRefresh={this.subscribeChildToRefreshEvent}
                        apiCheckName="descriptionsForAllImages"
                        label="Descriptions for all images"
                    />
                    <CheckItem
                        subscribeToRefresh={this.subscribeChildToRefreshEvent}
                        apiCheckName="audioForAllImageDescriptions"
                        label="Audio for all image descriptions"
                    />
                    {/* <CheckItem apirUrl="automatedEpubCheck" label="Automated ePUB Check" /> */}
                </section>
                <section>
                    <H1 l10nKey="AccessibilityCheck.ManualChecksHeading">
                        You need to check these yourself:
                    </H1>
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
                subscribeToRefresh={this.subscribeChildToRefreshEvent}
                l10nKey={"AccessibilityCheck." + key}
                apiEndpoint={"accessibilityCheck/" + key}
            >
                {english}
            </ApiBackedCheckbox>
        );
    }
}
