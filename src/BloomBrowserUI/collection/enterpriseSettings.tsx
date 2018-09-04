import * as React from "react";
import * as ReactDOM from "react-dom";
import { Label } from "../react_components/l10n";
import { Markdown } from "../react_components/markdown";
import { Link } from "../react_components/link";
import { RadioGroup, Radio } from "../react_components/radio";
import { BloomApi } from "../utils/bloomApi";
import "./enterpriseSettings.less";
import { FontAwesomeIcon } from "../bloomIcons";

interface IState {
    enterpriseStatus: string;
    subscriptionCode: string;
    subscriptionExpiry: Date;
    subscriptionSummary: string;
    subscriptionUnknown: boolean;
    subscriptionIncomplete: boolean;
    invalidBranding: string;
    subscriptionAnimation: string;
    communityAnimation: string;
    summaryAnimation: string;
}

// This class implements the Bloom Enterprise tab of the Settings dialog.
export class EnterpriseSettings extends React.Component<{}, IState> {
    public readonly state: IState = {
        enterpriseStatus: "none",
        subscriptionCode: "",
        subscriptionExpiry: null,
        subscriptionSummary: "",
        subscriptionUnknown: false,
        subscriptionIncomplete: false,
        invalidBranding: "",
        subscriptionAnimation: "",
        communityAnimation: "",
        summaryAnimation: ""
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
                    className="mainHeading"
                    l10nKey="Settings.Enterprise.Overview"
                >
                    Bloom Enterprise adds features and services that are
                    important for publishers, governments, and international
                    organizations. This paid subscription meets their unique
                    needs while supporting the development and user support of
                    Bloom for the community at large.
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
                    onChange={val => this.setStatus(val)}
                    value={this.state.enterpriseStatus}
                >
                    <div>
                        <Radio
                            l10nKey="Settings.Enterprise.Subscription"
                            value="Subscription"
                        >
                            Enterprise Subscription
                        </Radio>
                        <Markdown
                            l10nKey="Settings.Enterprise.NeedsCode"
                            l10nParam0={this.state.invalidBranding}
                            l10nParam1={this.codesUrl}
                            className={
                                "invalidBranding" +
                                (this.state.invalidBranding &&
                                !this.subscriptionExpired()
                                    ? ""
                                    : " hidden")
                            }
                        >
                            In an older version of Bloom, this project used
                            **%0**. To continue to use this you will need to
                            enter a current subscription code. Codes for SIL
                            brandings can be found [here](%1). For other
                            subscriptions, contact your project administrator.
                        </Markdown>
                        <div
                            id="enterpriseMain"
                            className={
                                "enterpriseSubitems" +
                                (this.state.enterpriseStatus === "Subscription"
                                    ? ""
                                    : " closed")
                            }
                            style={{
                                animationName: this.state.subscriptionAnimation
                            }}
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
                                onChange={e =>
                                    this.handleSubscriptionCodeChanged(e)
                                }
                            />
                            <span
                                className={
                                    "evaluationCode" +
                                    (this.shouldShowGreenCheck()
                                        ? ""
                                        : " hidden")
                                }
                            >
                                <FontAwesomeIcon icon="check" />
                            </span>
                            <span
                                className={
                                    "evaluationCode" +
                                    (this.shouldShowIncomplete()
                                        ? ""
                                        : " hidden")
                                }
                            >
                                <FontAwesomeIcon icon="question" />
                            </span>
                            <span
                                className={
                                    "evaluationCode" +
                                    (this.shouldShowRedError() ? "" : " hidden")
                                }
                            >
                                <FontAwesomeIcon icon="exclamation-circle" />
                            </span>
                            <div
                                className="editButton"
                                onClick={() => this.onCopy()}
                            >
                                <div className="editButtonAlign">
                                    <FontAwesomeIcon
                                        className="editIcon"
                                        icon="copy"
                                    />
                                    <Label
                                        className="editButtonLabel"
                                        l10nKey="EditTab.CopyButton"
                                    >
                                        Copy
                                    </Label>
                                </div>
                            </div>
                            <div
                                className="editButton pasteButton"
                                onClick={() => this.onPaste()}
                            >
                                <div className="editButtonAlign">
                                    <FontAwesomeIcon
                                        className="editIcon"
                                        icon="paste"
                                    />
                                    <Label
                                        className="editButtonLabel"
                                        l10nKey="EditTab.PasteButton"
                                    >
                                        Paste
                                    </Label>
                                </div>
                            </div>
                            <Label
                                l10nKey="Settings.Enterprise.NotValid"
                                className={
                                    "error" +
                                    (this.shouldShowIncorrect()
                                        ? ""
                                        : " hidden")
                                }
                            >
                                That code appears to be incorrect.
                            </Label>
                            <Label
                                l10nKey="Settings.Enterprise.Incomplete"
                                className={
                                    "incomplete" +
                                    (this.shouldShowIncomplete()
                                        ? ""
                                        : " hidden")
                                }
                            >
                                The code should look like SOMENAME-123456-7890
                            </Label>
                            <div
                                className={
                                    this.shouldShowUnknown() ? "" : " hidden"
                                }
                            >
                                <Label
                                    l10nKey="Settings.Enterprise.UnknownCode"
                                    className="error"
                                >
                                    This version of Bloom does not have the
                                    artwork that goes with that subscription.
                                </Label>
                                <Link
                                    className="error"
                                    l10nKey="Settings.Enterprise.CheckUpdates"
                                    onClick={() => this.checkForUpdates()}
                                >
                                    Check for updates
                                </Link>
                            </div>
                            <Label
                                l10nKey="Settings.Enterprise.Expired"
                                className={
                                    "error" +
                                    (this.shouldShowExpired() ? "" : " hidden")
                                }
                            >
                                That code has expired.
                            </Label>
                            <div
                                className={
                                    "expiration" +
                                    (this.shouldShowExpiration()
                                        ? ""
                                        : " hidden")
                                }
                            >
                                <Label l10nKey="Settings.Enterprise.Expiration">
                                    Expires:
                                </Label>
                                <span>
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
                                    (this.state.subscriptionSummary
                                        ? ""
                                        : " closed")
                                }
                                style={{
                                    animationName: this.state.summaryAnimation
                                }}
                                dangerouslySetInnerHTML={{
                                    __html: this.state.subscriptionSummary
                                }}
                            />
                        </div>
                    </div>
                    <div className="mainButton communityGroup">
                        <Radio
                            className="communityRadio"
                            l10nKey="Settings.Enterprise.Community"
                            value="Community"
                        >
                            Funded by the local community only
                        </Radio>
                        <Link
                            className="whatsThisLink"
                            l10nKey="Settings.Enterprise.WhatsThis"
                            href="http://bit.ly/2zTQHfM" // Todo: make a page and link to it.
                        >
                            What's This?
                        </Link>
                    </div>
                    <div
                        id="communityMain"
                        className={
                            "summary communitySummary" +
                            (this.state.subscriptionSummary &&
                            this.state.enterpriseStatus === "Community"
                                ? ""
                                : " closed")
                        }
                        style={{
                            animationName: this.state.communityAnimation
                        }}
                        dangerouslySetInnerHTML={{
                            __html: this.state.subscriptionSummary
                        }}
                    />
                    <Radio
                        className="mainButton"
                        l10nKey="Settings.Enterprise.None"
                        value="None"
                    >
                        None. Bloom will not show Enterprise features.
                    </Radio>
                </RadioGroup>
            </div>
        );
    }

    private shouldShowGreenCheck() {
        if (!this.state.subscriptionCode) {
            return false; // no code at all, don't evaluate
        }
        return !this.shouldShowSomeError();
    }

    private shouldShowExpiration(): boolean {
        return this.state.subscriptionExpiry && !this.subscriptionExpired();
    }

    private shouldShowIncomplete() {
        if (!this.shouldShowSomeError()) {
            return false; // no problem
        }
        if (this.state.subscriptionIncomplete) {
            return true; // incomplete is exactly what the ? is for
        }
        return false; // it's an error
    }

    private shouldShowExpired() {
        if (!this.shouldShowSomeError()) {
            return false; // no problem
        }
        return this.subscriptionExpired();
    }

    private shouldShowRedError() {
        if (!this.shouldShowSomeError()) {
            return false; // no problem
        }
        // If there's a not-valid code, we want either question or error icon
        return !this.shouldShowIncomplete();
    }

    private shouldShowSomeError() {
        if (this.state.enterpriseStatus != "Subscription") {
            return false; // does not apply
        }

        if (this.state.subscriptionExpiry && !this.subscriptionExpired()) {
            return false; // code is good!
        }
        return true;
    }

    private shouldShowIncorrect() {
        if (!this.shouldShowSomeError()) {
            return false; // no problem
        }
        if (
            this.state.subscriptionUnknown ||
            this.state.subscriptionIncomplete ||
            this.subscriptionExpired()
        ) {
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
            return false; // Don't want to use expired message for invalid date
        }
        return this.state.subscriptionExpiry < new Date();
    }

    private setStatus(status: string) {
        BloomApi.postJson("settings/enterpriseStatus", status);
        var oldStatus = this.state.enterpriseStatus;
        // Figure out animation names to set to make the appropriate child blocks
        // slide up and down.
        var subscriptionAnimation = "";
        var communityAnimation = "";
        if (status === "Subscription" && oldStatus != "Subscription") {
            // We want to do something like show160 if there's going to be a summary,
            // show60 otherwise. But we can't know whether there is going to be
            // a summary at this point. Assume that if we have a code, it's good,
            // so there will be. It's better to use the bigger number wrongly, which
            // just makes the transition a bit quick, than a smaller one, which
            // makes it grow and then jerk.
            subscriptionAnimation = this.state.subscriptionCode
                ? "show160"
                : "show60";
        }
        if (status === "Community" && oldStatus != "Community") {
            communityAnimation = "show100";
        }
        if (status !== "Subscription" && oldStatus === "Subscription") {
            subscriptionAnimation = this.state.subscriptionSummary
                ? "hide160"
                : "hide60";
        }
        if (status !== "Community" && oldStatus === "Community") {
            communityAnimation = "hide100";
        }
        this.setState({
            enterpriseStatus: status,
            subscriptionAnimation: subscriptionAnimation,
            communityAnimation: communityAnimation,
            summaryAnimation: ""
        });
        if (status !== "None") {
            BloomApi.get("settings/enterpriseSummary", result => {
                this.setSummary(result.data, false);
            });
        }
    }

    private onPaste() {
        BloomApi.get("settings/paste", result =>
            this.updateSubscriptionCode(result.data)
        );
    }

    private onCopy() {
        BloomApi.postJson("settings/copy", {
            text: this.state.subscriptionCode
        });
    }

    private checkForUpdates() {
        BloomApi.post("settings/checkForUpdates");
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
                    subscriptionIncomplete: false,
                    subscriptionSummary: "",
                    summaryAnimation: "hide100"
                });
                return;
            }
            if (result.data === "incomplete") {
                // Invalid code, but looks as if they haven't finished typing
                this.setState({
                    subscriptionCode: code,
                    subscriptionExpiry: null,
                    subscriptionUnknown: false,
                    subscriptionIncomplete: true,
                    subscriptionSummary: "",
                    summaryAnimation: "hide100"
                });
                return;
            }
            var expiry = result.data === null ? null : new Date(result.data);
            this.setState({
                subscriptionCode: code,
                subscriptionExpiry: expiry,
                subscriptionUnknown: false,
                subscriptionIncomplete: false
            });
            if (expiry) {
                BloomApi.get("settings/enterpriseSummary", result => {
                    this.setSummary(result.data, true);
                });
            } else {
                this.setSummary("", true);
            }
        });
    }

    private setSummary(content: string, animate: boolean) {
        this.setState({
            subscriptionSummary: content
        });
        if (animate) {
            this.setState({
                summaryAnimation: content ? "show100" : "hide100"
            });
        }
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
        this.updateSubscriptionCode(event.target.value);
    }
    private updateSubscriptionCode(code: string) {
        BloomApi.postJson("settings/subscriptionCode", {
            subscriptionCode: code
        });
        this.setSubscriptionCode(code);
        this.setState({ invalidBranding: "" });
    }
}

// allow plain 'ol javascript in the html to connect up react
(window as any).connectEnterpriseSettingsScreen = function(element) {
    ReactDOM.render(<EnterpriseSettings />, element);
};
