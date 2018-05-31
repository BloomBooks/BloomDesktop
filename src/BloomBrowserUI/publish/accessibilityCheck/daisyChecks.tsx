import * as React from "react";
import * as ReactDOM from "react-dom";
import { IUILanguageAwareProps } from "../../react_components/l10n";
import axios from "axios";
import "./daisyChecks.less";

/* Ace By Daisy (https://daisy.github.io/ace/) is a nodejs-based epub checker.
    This class asks the Bloom server to run it and then shows the result
*/
interface IState {
    // when the report is done, we get back from the Bloom server a url to get at the report that was generated
    reportUrl: string;
    // but if we don't have a report for some reason, this is what we show the user
    statusMessageHtml: string;
}
export class DaisyChecks extends React.Component<
    IUILanguageAwareProps,
    IState
> {
    constructor(props) {
        super(props);
        this.state = { reportUrl: "", statusMessageHtml: "" };
    }
    public componentDidMount() {
        this.setState({
            statusMessageHtml: "Generating..."
        });
        axios
            .get("/bloom/api/accessibilityCheck/aceByDaisyReportUrl")
            .then(result => {
                // we use text/plain as an indicator that we are being given a url
                switch (result.headers["content-type"]) {
                    case "text/plain":
                        this.setState({ reportUrl: result.data });
                        break;
                    case "text/html":
                        this.setState({ statusMessageHtml: result.data });
                        break;
                    default:
                        this.setState({
                            statusMessageHtml:
                                "aceByDaisyReportUrl returned unexpected content-type"
                        });
                        break;
                }
            })
            .catch(error => {
                this.setState({
                    statusMessageHtml: error
                });
            });
    }
    public render() {
        return (
            <div id="daisyChecks">
                {this.state.reportUrl.length > 0 ? (
                    <iframe src={this.state.reportUrl} />
                ) : (
                    <p id="statusMessage">{this.state.statusMessageHtml}</p>
                )}
            </div>
        );
    }
}
