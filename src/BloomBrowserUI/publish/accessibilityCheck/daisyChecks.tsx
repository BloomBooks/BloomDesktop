import * as React from "react";
import { IUILanguageAwareProps } from "../../react_components/l10n";
import axios from "axios";
import "./daisyChecks.less";
import ProgressBox from "../../react_components/progressBox";
import WebSocketManager from "../../utils/WebSocketManager";
import { BloomApi } from "../../utils/bloomApi";

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
        WebSocketManager.addListener(kWebSocketLifetime, data => {
            if (data.id == "daisyResults") {
                this.setState({ reportUrl: data.message });
            }
        });
    }
    public componentDidMount() {
        // when this component first comes up, we're in the publish tab, and have
        // a current preview, or at least are in the process of making one. So we
        // don't need to make a new one like we do when Refresh is clicked.
        this.refresh(false);
    }
    private refresh(forceNewEpub: boolean) {
        this.setState({ reportUrl: "" });
        BloomApi.postDataWithConfig(
            "accessibilityCheck/aceByDaisyReportUrl",
            forceNewEpub,
            {
                headers: { "Content-Type": "application/json" }
            }
        );
    }
    public render() {
        return (
            <div id="daisyChecks">
                {this.state.reportUrl.length > 0 ? (
                    <div id="report">
                        <button id="refresh" onClick={() => this.refresh(true)}>
                            Refresh
                        </button>
                        <iframe src={this.state.reportUrl} />
                    </div>
                ) : (
                    <ProgressBox
                        ref={r => (this.progressBox = r)}
                        clientContext={kWebSocketLifetime}
                    />
                )}
            </div>
        );
    }
}
