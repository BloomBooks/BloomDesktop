import * as React from "react";
import * as ReactDOM from "react-dom";
import { Label } from "../react_components/l10n";
import { Link } from "../react_components/link";
import { RadioGroup, Radio } from "../react_components/radio";
import { BloomApi } from "../utils/bloomApi";
import "./enterpriseSettings.less";
//import { IUILanguageAwareProps, H1 } from "../../react_components/l10n";

//import WebSocketManager from "../../utils/WebSocketManager";

interface IState {
    enterpriseStatus: string;
    subscriptionCode: string;
    subscriptionExpiry: Date;
    subscriptionFeedback: string;
    subscriptionUnknown: boolean;
}

// This class implements the Bloom Enterprise tab of the Settings dialog.
export class EnterpriseSettings extends React.Component<{}, IState> {
    constructor(props) {
        super(props);
        this.state = {
            enterpriseStatus: "none",
            subscriptionCode: "",
            subscriptionExpiry: null,
            subscriptionFeedback: "",
            subscriptionUnknown: false
        };

        this.handleSubscriptionCodeChanged = this.handleSubscriptionCodeChanged.bind(
            this
        );
    }

    public componentDidMount() {
        BloomApi.get("settings/enterpriseStatus", result => {
            this.setState({ enterpriseStatus: result.data });
        });
        BloomApi.get("settings/enterpriseCode", result => {
            this.setSubscriptionCode(result.data);
        });
    }
    // I don't understand why this is necessary but without it the selection
    // moves to the end of the subscription code after every keystroke.
    private oldSelectionPosition: number;
    public componentWillUpdate() {
        this.oldSelectionPosition = (document.getElementById(
            "subscriptionCodeInput"
        ) as HTMLInputElement).selectionStart;
    }

    public componentDidUpdate() {
        const codeElt = document.getElementById(
            "subscriptionCodeInput"
        ) as HTMLInputElement;
        codeElt.selectionStart = codeElt.selectionEnd = this.oldSelectionPosition;
    }

    public render() {
        return (
            <div className="enterpriseSettings">
                <div>
                    <Label l10nKey="Settings.Enterprise.Overview">
                        Bloom Enterprise is a paid service offered by SIL
                        International. It adds some additional features and
                        services that are important to publishers, governments,
                        and international organizations. By subscribing to this
                        service, these projects can meet some of their unique
                        needs while also supporting the development and user
                        support of Bloom, which helps everybody.
                    </Label>
                </div>
                <div className="learnMoreLink">
                    <Link
                        l10nKey="Settings.Enterprise.LearnMore"
                        href="http://bit.ly/2zTQHfM"
                    >
                        Learn More
                    </Link>
                </div>
                <div className="bloomEnterpriseStatus">
                    <Label l10nKey="Settings.Enterprise.Status">
                        Bloom Enterprise Status
                    </Label>
                </div>
                <div>
                    <RadioGroup
                        onChange={val => this.setStatus(val)}
                        value={this.state.enterpriseStatus}
                    >
                        <Radio l10nKey="Settings.Enterprise.None" value="None">
                            None
                        </Radio>
                        <div className="communityGroup">
                            <Radio
                                className="communityRadio"
                                l10nKey="Settings.Enterprise.Community"
                                value="Community"
                            >
                                Funded Local Community Only
                            </Radio>
                            <Link
                                className="whatsThisLink"
                                l10nKey="Settings.Enterprise.WhatsThis"
                                href="http://bit.ly/2zTQHfM" // Todo: make a page and link to it.
                            >
                                What's This?
                            </Link>
                        </div>
                        <Radio
                            l10nKey="Settings.Enterprise.Subscription"
                            value="Subscription"
                        >
                            Enterprise Subscription
                        </Radio>
                    </RadioGroup>
                </div>
                <div className="subscriptionCodeWrapper">
                    <Label
                        className="subscriptionCodeLabel"
                        l10nKey="Settings.Enterprise.SubscriptionCode"
                    >
                        Subscription Code:
                    </Label>

                    <input
                        id="subscriptionCodeInput"
                        className="subscriptionCodeInput"
                        type="text"
                        value={this.state.subscriptionCode}
                        onChange={this.handleSubscriptionCodeChanged}
                    />
                    <span className="codeEvaluation">
                        {this.state.subscriptionCode &&
                        this.state.subscriptionExpiry !== null &&
                        !this.state.subscriptionUnknown
                            ? "\u2713"
                            : ""}
                    </span>
                </div>
                <div
                    className="error"
                    style={{
                        display:
                            this.state.subscriptionCode &&
                            !this.state.subscriptionExpiry &&
                            !this.state.subscriptionUnknown
                                ? "block"
                                : "none"
                    }}
                >
                    <Label l10nKey="Settings.Enterprise.NotValid">
                        Sorry, that code appears incorrect
                    </Label>
                </div>
                <div
                    className="error"
                    style={{
                        display:
                            this.state.subscriptionCode &&
                            !this.state.subscriptionExpiry &&
                            this.state.subscriptionUnknown
                                ? "block"
                                : "none"
                    }}
                >
                    <Label l10nKey="Settings.Enterprise.UnknownCode">
                        That code looks good, but this version of Bloom does not
                        know about that project
                    </Label>
                </div>
                <div
                    className="expiration"
                    style={{
                        display: this.state.subscriptionExpiry
                            ? "block"
                            : "none"
                    }}
                >
                    <Label l10nKey="Settings.Enterprise.Expiration">
                        Expires:
                    </Label>
                    <span
                        style={{
                            color: this.subscriptionExpired() ? "red" : ""
                        }}
                    >
                        {this.state.subscriptionExpiry
                            ? this.state.subscriptionExpiry.toLocaleDateString(
                                  undefined,
                                  {
                                      year: "numeric",
                                      day: "numeric",
                                      month: "long"
                                  }
                              )
                            : ""}
                    </span>
                </div>
                <div
                    className="summary"
                    dangerouslySetInnerHTML={{
                        __html: this.state.subscriptionFeedback
                    }}
                />
            </div>
        );
    }

    private subscriptionExpired() {
        if (!this.state.subscriptionExpiry) {
            return true; // arbitrary, not used.
        }
        return this.state.subscriptionExpiry < new Date();
    }

    private setStatus(status: string) {
        BloomApi.postDataWithConfig("settings/enterpriseStatus", status, {
            headers: { "Content-Type": "application/json" }
        });
        this.setState({ enterpriseStatus: status });
    }

    // Used both with a code from entering something in the text box, and
    // initializing from an axios get. Should not post. Assume we have already
    // set the code on the server.
    private setSubscriptionCode(code: string) {
        BloomApi.get("settings/enterpriseExpiry", result => {
            if (result.data === "unknown") {
                // Valid-looking code, but not one this version knows about.
                this.setState({
                    subscriptionCode: code,
                    subscriptionExpiry: null,
                    subscriptionUnknown: true,
                    subscriptionFeedback: ""
                });
                return;
            }
            var expiry = result.data === null ? null : new Date(result.data);
            this.setState({
                subscriptionCode: code,
                subscriptionExpiry: expiry,
                subscriptionUnknown: false
            });
            if (expiry) {
                BloomApi.get("settings/enterpriseFeedback", result => {
                    this.setFeedback(result.data);
                });
            } else {
                this.setFeedback("");
            }
        });
    }

    private setFeedback(content: string) {
        this.setState({ subscriptionFeedback: content });
    }

    private handleSubscriptionCodeChanged(event) {
        BloomApi.postDataWithConfig(
            "settings/enterpriseCode",
            { enterpriseCode: event.target.value },
            {
                headers: { "Content-Type": "application/json" }
            }
        );
        this.setSubscriptionCode(event.target.value);
        this.setStatus(event.target.value ? "Subscription" : "None");
    }
}

// allow plain 'ol javascript in the html to connect up react
(window as any).connectEnterpriseSettingsScreen = function(element) {
    ReactDOM.render(<EnterpriseSettings />, element);
};
