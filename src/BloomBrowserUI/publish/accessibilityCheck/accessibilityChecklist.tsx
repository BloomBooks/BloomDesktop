import * as React from "react";
import * as ReactDOM from "react-dom";
import { IUILanguageAwareProps } from "../../react_components/l10n";

import "./accessibilityChecklist.less";
import { CheckItem } from "./checkItem";
import { Checkbox } from "../../react_components/checkbox";
import Axios from "axios";
import { ApiBackedCheckbox } from "../../react_components/apiBackedCheckbox";

interface IState {
    noEssentialInfoByColor: boolean;
}
export class AccessibilityChecklist extends React.Component<
    IUILanguageAwareProps,
    IState
> {
    constructor(props) {
        super(props);
        this.state = { noEssentialInfoByColor: false };
    }

    public render() {
        return (
            <div className="checkList">
                <section>
                    <h1>Bloom can automatically check these for you</h1>
                    <CheckItem
                        apiCheckName="audioForAllText"
                        label="Audio for all text"
                    />
                    <CheckItem
                        apiCheckName="descriptionsForAllImages"
                        label="Descriptions for all images"
                    />
                    <CheckItem
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
                    {this.addCheck(
                        "noTextIncludedInAnyImages",
                        "No text included in any images"
                    )}
                    <p>(todo: make checkbox) No text included in an image</p>
                </section>
            </div>
        );
    }
    private addCheck(key: string, english: string): JSX.Element {
        return (
            <ApiBackedCheckbox
                l10nKey={"Accessibility." + key}
                apiPath={"/bloom/api/accessibilityCheck/" + key}
            >
                {english}
            </ApiBackedCheckbox>
        );
    }
}
