﻿import * as React from "react";
import { IUILanguageAwareProps } from "../../react_components/l10n";
import axios from "axios";
import "./daisyChecks.less";
import ProgressBox from "../../react_components/progressBox";

const kWebSocketLifetime = "a11yChecklist";

/* Ace By Daisy (https://daisy.github.io/ace/) is a nodejs-based epub checker.
    This class asks the Bloom server to run it and then shows the result
*/
interface IState {
    // when the report is done, we get back from the Bloom server a url to get at the report that was generated
    reportUrl: string;
}
export class DaisyChecks extends React.Component<
    IUILanguageAwareProps,
    IState
> {
    private progressBox: ProgressBox;
    constructor(props) {
        super(props);
        this.state = { reportUrl: "" };
    }
    public componentDidMount() {
        this.refresh();
    }
    private refresh() {
        this.setState({ reportUrl: "" });
        // using axios directly because of explicit catch.
        axios
            .get("/bloom/api/accessibilityCheck/aceByDaisyReportUrl")
            .then(result => {
                this.setState({ reportUrl: result.data });
            })
            .catch(error => {
                this.progressBox.writeLine("Failed");
            });
    }
    public render() {
        return (
            <div id="daisyChecks">
                {this.state.reportUrl.length > 0 ? (
                    <div id="report">
                        <button id="refresh" onClick={() => this.refresh()}>
                            Refresh
                        </button>
                        <iframe src={this.state.reportUrl} />
                    </div>
                ) : (
                    <ProgressBox
                        ref={r => (this.progressBox = r)}
                        lifetimeLabel={kWebSocketLifetime}
                    />
                )}
            </div>
        );
    }
}
