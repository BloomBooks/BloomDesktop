import * as React from "react";
import { IUILanguageAwareProps } from "../../react_components/l10nComponents";
import axios from "axios";
import "./daisyChecks.less";
import { ProgressBox } from "../../react_components/Progress/progressBox";

const kWebSocketContext = "a11yChecklist";

/* Ace by DAISY (https://daisy.github.io/ace/) is a nodejs-based epub checker.
    This class asks the Bloom server to run it and then shows the result
*/
interface IState {
    // when the report is done, we get back from the Bloom server a url to get at the report that was generated
    reportUrl: string;
    errorMessage?: string;
}

export class DaisyChecks extends React.Component<
    IUILanguageAwareProps,
    IState
> {
    public readonly state: IState = {
        reportUrl: "",
        errorMessage: undefined
    };

    public componentDidMount() {
        this.refresh();
    }
    private refresh() {
        this.setState({ reportUrl: "", errorMessage: undefined });
        // using axios directly because of explicit catch.
        axios
            .get("/bloom/api/accessibilityCheck/aceByDaisyReportUrl")
            .then(result => {
                this.setState({ reportUrl: result.data });
            })
            .catch(error => {
                this.setState({
                    errorMessage:
                        "The API call to Ace By Daisy failed. " +
                        JSON.stringify(error)
                });
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
                        webSocketContext={kWebSocketContext}
                        preloadedProgressEvents={
                            this.state.errorMessage
                                ? [
                                      {
                                          progressKind: "Error",
                                          id: "message",
                                          clientContext: "unused",
                                          message: this.state.errorMessage
                                      }
                                  ]
                                : []
                        }
                    />
                )}
            </div>
        );
    }
}
