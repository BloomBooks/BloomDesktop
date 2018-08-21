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
    subscriptionSummary: string;
    subscriptionUnknown: boolean;
    invalidBranding: string;
}

// This class implements the Bloom Enterprise tab of the Settings dialog.
export class EnterpriseSettings extends React.Component<{}, IState> {
    public readonly state: IState = {
        enterpriseStatus: "none",
        subscriptionCode: "",
        subscriptionExpiry: null,
        subscriptionSummary: "",
        subscriptionUnknown: false,
        invalidBranding: ""
    };

    public componentDidMount() {
        BloomApi.get("settings/enterpriseStatus", result => {
            this.setState({ enterpriseStatus: result.data });
        });
        BloomApi.get("settings/subscriptionCode", result => {
            this.setSubscriptionCode(result.data);
        });
        BloomApi.get("settings/invalidBranding", result => {
            this.setState({ invalidBranding: result.data });
        });
    }
    // I don't understand why this is necessary but without it the selection
    // moves to the end of the subscription code after every keystroke.
    private oldSelectionPosition: number = 0;

    public componentDidUpdate() {
        // somehow this can fail when we are hiding the control. Just ignore it.
        try {
            const codeElt = document.getElementById(
                "subscriptionCodeInput"
            ) as HTMLInputElement;
            codeElt.selectionStart = codeElt.selectionEnd = this.oldSelectionPosition;
        } catch (e) {}
    }

    private codesUrl =
        "https://gateway.sil.org/display/LSDEV/Bloom+enterprise+subscription+codes";

    public render() {
        return (
            <div className="enterpriseSettings">
                <Label
                    l10nKey="Settings.Enterprise.NeedsCode"
                    l10nParam0={this.state.invalidBranding}
                    className={
                        "invalidBranding" +
                        (this.state.invalidBranding ? "" : " hidden")
                    }
                >
                    This project is configured to use %0 branding. To continue
                    to use this you will need to enter a current subscription
                    code. For SIL brandings, SIL members can find codes at the
                    link below. For other brandings, contact your project
                    administrator.
                </Label>
                <a
                    className={
                        "invalidBrandingLink" +
                        (this.state.invalidBranding ? "" : " hidden")
                    }
                    href={this.codesUrl}
                >
                    {this.codesUrl}
                </a>
                <Label
                    className="mainHeading"
                    l10nKey="Settings.Enterprise.Overview"
                >
                    Bloom Enterprise is a paid service offered by SIL
                    International. It adds some additional features and services
                    that are important to publishers, governments, and
                    international organizations. By subscribing to this service,
                    these projects can meet some of their unique needs while
                    also supporting the development and user support of Bloom,
                    which helps everybody.
                </Label>
                <Link
                    className="learnMoreLink"
                    l10nKey="Settings.Enterprise.LearnMore"
                    href="http://bit.ly/2zTQHfM"
                >
                    Learn More
                </Link>
                <div className="bloomEnterpriseStatus">
                    <Label l10nKey="Settings.Enterprise.Status">
                        Bloom Enterprise Status
                    </Label>
                </div>
                <RadioGroup
                    onChange={val => this.setStatus(val, true)}
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
                            Funded by local community only
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
                <div
                    className={
                        "subscriptionCodeWrapper" +
                        (this.state.enterpriseStatus == "Subscription"
                            ? ""
                            : " hidden")
                    }
                >
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
                        onChange={e => this.handleSubscriptionCodeChanged(e)}
                    />
                    <span className="codeEvaluation">
                        {this.state.subscriptionCode &&
                        this.state.subscriptionExpiry !== null &&
                        !this.state.subscriptionUnknown
                            ? "✓"
                            : ""}
                    </span>
                </div>
                <Label
                    l10nKey="Settings.Enterprise.NotValid"
                    className={
                        "error" + (this.shouldShowIncorrect() ? "" : " hidden")
                    }
                >
                    Sorry, that code appears incorrect
                </Label>
                <Label
                    l10nKey="Settings.Enterprise.UnknownCode"
                    className={
                        "error" + (this.shouldShowUnknown() ? "" : " hidden")
                    }
                >
                    That code looks good, but this version of Bloom does not
                    know about that project
                </Label>
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
                    className={
                        "summary" +
                        (this.state.enterpriseStatus == "None" ? " hidden" : "")
                    }
                    dangerouslySetInnerHTML={{
                        __html: this.state.subscriptionSummary
                    }}
                />
            </div>
        );
    }

    private shouldShowSomeError() {
        if (this.state.enterpriseStatus != "Subscription") {
            return false; // does not apply
        }
        if (!this.state.subscriptionCode) {
            return false;
        } // nothing entered to complain about

        if (this.state.subscriptionExpiry) {
            return false; // code is good!
        }
        return true;
    }

    private shouldShowIncorrect() {
        if (!this.shouldShowSomeError()) {
            return false; // no problem
        }
        if (this.state.subscriptionUnknown) {
            return false; // we'll show a different message
        }
        return true;
    }

    private shouldShowUnknown() {
        if (!this.shouldShowSomeError()) {
            return false; // no problem
        }
        if (!this.state.subscriptionUnknown) {
            return false; // we'll show a different message
        }
        return true;
    }

    private subscriptionExpired() {
        if (!this.state.subscriptionExpiry) {
            return true; // arbitrary, not used.
        }
        return this.state.subscriptionExpiry < new Date();
    }

    private setStatus(status: string, updateSumary: boolean) {
        BloomApi.postJson("settings/enterpriseStatus", status);
        this.setState({ enterpriseStatus: status });
        if (updateSumary) {
            if (status === "Community") {
                BloomApi.get("settings/enterpriseSummary", result => {
                    this.setSummary(result.data);
                });
            } else {
                this.setSummary("");
            }
        }
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
                    subscriptionSummary: ""
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
                BloomApi.get("settings/enterpriseSummary", result => {
                    this.setSummary(result.data);
                });
            } else {
                this.setSummary("");
            }
        });
    }

    private setSummary(content: string) {
        this.setState({ subscriptionSummary: content });
    }

    private handleSubscriptionCodeChanged(event) {
        // before React starts messing with things, note where the selection was,
        // so we can put it back after everything gets updated (in componentDidUpdate).
        // Note: don't be tempted to put this in componentWillUpdate. Somehow that doesn't
        // think the selection can be at a position beyond the end of the old string
        // content, so typing at the end causes the selection to be pushed back before
        // the new character.
        this.oldSelectionPosition = (document.getElementById(
            "subscriptionCodeInput"
        ) as HTMLInputElement).selectionStart;

        BloomApi.postJson("settings/subscriptionCode", {
            subscriptionCode: event.target.value
        });
        this.setSubscriptionCode(event.target.value);
        this.setStatus(event.target.value ? "Subscription" : "None", false);
        this.setState({ invalidBranding: "" });
    }
}

// allow plain 'ol javascript in the html to connect up react
(window as any).connectEnterpriseSettingsScreen = function(element) {
    ReactDOM.render(<EnterpriseSettings />, element);
};
