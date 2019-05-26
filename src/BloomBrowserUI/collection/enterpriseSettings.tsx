import * as React from "react";
import * as ReactDOM from "react-dom";
import { Label } from "../react_components/l10nComponents";
import { Markdown } from "../react_components/markdown";
import { Link } from "../react_components/link";
import { HelpLink } from "../react_components/helpLink";
import { RadioGroup, Radio } from "../react_components/radio";
import { BloomApi } from "../utils/bloomApi";
import "./enterpriseSettings.less";
import { FontAwesomeIcon } from "../bloomIcons";

// values of controlState:
// None: the None button is selected.
// Community: the Local Community button is selected
// The remaining states all involve the EnterpriseSubscription button being selected
// SubscriptionGood: a valid, current, known code has been entered (or remembered)
//      We get a green check, an expiration date, and a 'summary' of the branding
// SubscriptionUnknown: a valid code has been entered, but this version of Bloom does not recognize it
//      We get a red ! icon and a message
// SubscriptionExpired: a valid code has been entered, but its expiration date is past.
//      We get a red ! icon, a red message about the expiration, and a branding summary
// SubscriptionIncomplete: a valid subscription could be created by typing more at
// the end of the current code. This includes the case where the code is empty.
//      We show a ? icon and a message
// SubscriptionIncorrect: a code has been entered which cannot be fixed by typing more
//      We show a red ! icon and  message
// SubscriptionLegacy: a special case where the subscription code is typically empty,
// or just possibly otherwise incomplete, and this has been detected at startup
//      We show a special message in an orange box and a red ! icon

interface IState {
    enterpriseStatus: string; // which radio button is active, controls the radio group
    subscriptionCode: string; // The content of the code box
    subscriptionExpiry: Date | null; // displayed if all is well
    subscriptionSummary: string; // markdown of the summary of the branding when identified
    controlState: string; // controls which parts of the dialog are visible (see above)
    // Set to the branding stored in the bloomCollection, in the special case
    // where the dialog is brought up (when the bloom collection is opened) because
    // the saved branding appears to be a legacy one. Gets inserted into
    // the message and the code box.
    legacyBrandingName: string;
}

// This class implements the Bloom Enterprise tab of the Settings dialog.
export class EnterpriseSettings extends React.Component<{}, IState> {
    public readonly state: IState = {
        enterpriseStatus: "None",
        subscriptionCode: "",
        subscriptionExpiry: null,
        subscriptionSummary: "",
        controlState: "None",
        legacyBrandingName: ""
    };

    public componentDidMount() {
        BloomApi.get("settings/enterpriseStatus", result => {
            const status = result.data;
            BloomApi.get("settings/subscriptionCode", result => {
                const code = result.data;
                BloomApi.get("settings/legacyBrandingName", result => {
                    this.setState({ legacyBrandingName: result.data });
                    this.setControlState(status, code, result.data);
                });
            });
        });
    }
    // I don't understand why this is necessary but without it the selection
    // moves to the end of the subscription code after every keystroke.
    private oldSelectionPosition: number | null = 0;

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
                <HelpLink
                    className="learnMoreLink"
                    l10nKey="Settings.Enterprise.LearnMore"
                    helpId="Tasks/Edit_tasks/Enterprise/EnterpriseRequired.htm"
                >
                    Learn More
                </HelpLink>
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
                        {this.state.controlState === "SubscriptionLegacy" && (
                            <Markdown
                                l10nKey="Settings.Enterprise.NeedsCode"
                                l10nParam0={this.state.legacyBrandingName}
                                l10nParam1={this.codesUrl}
                                className={"legacyBrandingName"}
                            >
                                In an older version of Bloom, this project used
                                **%0**. To continue to use this you will need to
                                enter a current subscription code. Codes for SIL
                                brandings can be found [here](%1). For other
                                subscriptions, contact your project
                                administrator.
                            </Markdown>
                        )}
                        <div
                            id="enterpriseMain"
                            className={
                                "enterpriseSubitems" +
                                (this.state.enterpriseStatus === "Subscription"
                                    ? this.state.subscriptionSummary
                                        ? " hasSummary"
                                        : ""
                                    : " closed")
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
                                onChange={e =>
                                    this.handleSubscriptionCodeChanged(e)
                                }
                            />
                            {this.state.controlState === "SubscriptionGood" && (
                                <span className={"evaluationCode"}>
                                    <FontAwesomeIcon icon="check" />
                                </span>
                            )}
                            {this.state.controlState ===
                                "SubscriptionIncomplete" && (
                                <span className={"evaluationCode"}>
                                    <FontAwesomeIcon icon="question" />
                                </span>
                            )}
                            {this.shouldShowRedExclamation() && (
                                <span className={"evaluationCode"}>
                                    <FontAwesomeIcon icon="exclamation-circle" />
                                </span>
                            )}
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
                            {this.state.controlState ===
                                "SubscriptionIncorrect" && (
                                <Label
                                    l10nKey="Settings.Enterprise.NotValid"
                                    className={"error"}
                                >
                                    That code appears to be incorrect.
                                </Label>
                            )}
                            {this.state.controlState ===
                                "SubscriptionIncomplete" && (
                                <Label
                                    l10nKey="Settings.Enterprise.Incomplete"
                                    className={"incomplete"}
                                >
                                    The code should look like
                                    SOMENAME-123456-7890
                                </Label>
                            )}
                            {this.state.controlState ===
                                "SubscriptionUnknown" && (
                                <div>
                                    <Label
                                        l10nKey="Settings.Enterprise.UnknownCode"
                                        className="error"
                                    >
                                        This version of Bloom does not have the
                                        artwork that goes with that
                                        subscription.
                                    </Label>
                                    <Link
                                        className="error"
                                        l10nKey="Settings.Enterprise.CheckUpdates"
                                        onClick={() => this.checkForUpdates()}
                                    >
                                        Check for updates
                                    </Link>
                                </div>
                            )}
                            {this.state.controlState ===
                                "SubscriptionExpired" && (
                                <Label
                                    l10nKey="Settings.Enterprise.Expired"
                                    className={"error"}
                                >
                                    That code has expired.
                                </Label>
                            )}

                            {this.state.controlState === "SubscriptionGood" && (
                                <div className={"expiration"}>
                                    <Label l10nKey="Settings.Enterprise.Expiration">
                                        Expires:
                                    </Label>
                                    <span>
                                        {this.state.subscriptionExpiry &&
                                            this.state.subscriptionExpiry.toLocaleDateString(
                                                undefined,
                                                {
                                                    year: "numeric",
                                                    day: "numeric",
                                                    month: "long"
                                                }
                                            )}
                                    </span>
                                </div>
                            )}

                            <div
                                className={
                                    "summary" +
                                    (this.state.subscriptionSummary
                                        ? ""
                                        : " closed")
                                }
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

    private shouldShowRedExclamation() {
        return (
            this.state.controlState === "SubscriptionIncorrect" ||
            this.state.controlState === "SubscriptionUnknown" ||
            this.state.controlState === "SubscriptionExpired" ||
            this.state.controlState === "SubscriptionLegacy"
        );
    }

    private setStatus(status: string) {
        BloomApi.postJson("settings/enterpriseStatus", status);
        this.setControlState(status, this.state.subscriptionCode, "");

        if (status !== "None") {
            BloomApi.get("settings/enterpriseSummary", result => {
                this.setSummary(result.data);
            });
        }
    }

    private onPaste() {
        BloomApi.get("common/clipboardText", result =>
            this.updateSubscriptionCode(result.data)
        );
    }

    private onCopy() {
        BloomApi.postJson("common/clipboardText", {
            text: this.state.subscriptionCode
        });
    }

    private checkForUpdates() {
        BloomApi.post("common/checkForUpdates");
    }

    // Used both with a code from entering something in the text box, and
    // initializing from an axios get. Should not post. Assume we have already
    // set the code on the server. Should set one of the Subscription control states
    // in controlState, subscriptionCode, subscriptionExpiry, enterpriseStatus, and subscriptionSummary
    private setControlState(
        status: string,
        code: string,
        legacyBrandingName: string
    ) {
        if (legacyBrandingName) {
            this.setState({
                enterpriseStatus: "Subscription",
                subscriptionCode: code,
                subscriptionExpiry: null,
                controlState: "SubscriptionLegacy",
                subscriptionSummary: ""
            });
            BloomApi.get("settings/enterpriseSummary", result => {
                this.setSummary(result.data);
            });
            return;
        }
        this.setState({ enterpriseStatus: status });
        if (status === "None") {
            this.setState({
                controlState: "None",
                subscriptionCode: code,
                subscriptionExpiry: null,
                subscriptionSummary: ""
            });
            return;
        } else if (status === "Community") {
            this.setState({
                controlState: "Community",
                subscriptionCode: code,
                subscriptionExpiry: null
            });
            BloomApi.get("settings/enterpriseSummary", result => {
                this.setSummary(result.data);
            });
            return;
        }
        BloomApi.get("settings/enterpriseExpiry", result => {
            if (result.data === "unknown") {
                // Valid-looking code, but not one this version knows about.
                this.setState({
                    subscriptionCode: code,
                    subscriptionExpiry: null,
                    controlState: "SubscriptionUnknown",
                    subscriptionSummary: ""
                });
                return;
            }
            if (result.data === "incomplete") {
                // Invalid code, but looks as if they haven't finished typing
                this.setState({
                    subscriptionCode: code,
                    subscriptionExpiry: null,
                    controlState: "SubscriptionIncomplete",
                    subscriptionSummary: ""
                });
                return;
            }
            const expiry = result.data === null ? null : new Date(result.data);
            this.setState({
                subscriptionCode: code,
                subscriptionExpiry: expiry
            });
            if (!expiry) {
                this.setState({ controlState: "SubscriptionIncorrect" });
            } else if (expiry < new Date()) {
                this.setState({ controlState: "SubscriptionExpired" });
            } else {
                this.setState({ controlState: "SubscriptionGood" });
            }
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
        this.setState({
            subscriptionSummary: content
        });
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
        this.setState({ legacyBrandingName: "" });
        this.setControlState(this.state.enterpriseStatus, code, "");
    }
}

// allow plain 'ol javascript in the html to connect up react
(window as any).connectEnterpriseSettingsScreen = element => {
    ReactDOM.render(<EnterpriseSettings />, element);
};
