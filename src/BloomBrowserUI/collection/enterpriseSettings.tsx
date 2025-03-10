/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { Div, Label } from "../react_components/l10nComponents";
import { Markdown } from "../react_components/markdown";
import { Link } from "../react_components/link";
import { HelpLink } from "../react_components/helpLink";
import { RadioGroup, Radio } from "../react_components/radio";
import { get, post, postJson } from "../utils/bloomApi";
import "./enterpriseSettings.less";
import { FontAwesomeIcon } from "../bloomIcons";
import { tabMargins } from "./commonTabSettings";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { SubscriptionStatus } from "../collectionsTab/SubscriptionStatus";

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
// SubscriptionDownload: a special case where a subscription code is incomplete
//      beccause we are using a branding permitted in a special collection made by
//      downloading a book for editing from BloomLibrary. We show a special message.

interface IState {
    enterpriseStatus: string; // which radio button is active, controls the radio group
    subscriptionCode: string; // The content of the code box
    subscriptionExpiryStringAsYYYYMMDD: string; // displayed if all is well
    subscriptionSummary: string; // markdown of the summary of the branding when identified
    hasSubscriptionFiles: boolean; // whether branding files exist or not
    controlState: string; // controls which parts of the dialog are visible (see above)
    // Set to the branding stored in the bloomCollection, in the special case
    // where the dialog is brought up (when the bloom collection is opened) because
    // the saved branding appears to be a legacy one. Gets inserted into
    // the message and the code box.
    legacyBrandingName: string;
    deprecatedBrandingsExpiryDate: string; // date after which deprecated brandings expire
}

// This class implements the Bloom Enterprise tab of the Settings dialog.
export class EnterpriseSettings extends React.Component<unknown, IState> {
    public readonly state: IState = {
        enterpriseStatus: "None",
        subscriptionCode: "",
        subscriptionExpiryStringAsYYYYMMDD: "",
        subscriptionSummary: "",
        hasSubscriptionFiles: false,
        controlState: "None",
        legacyBrandingName: "",
        deprecatedBrandingsExpiryDate: ""
    };

    public componentDidMount() {
        get("settings/deprecatedBrandingsExpiryDate", result => {
            this.setState({ deprecatedBrandingsExpiryDate: result.data });
        });

        get("settings/enterpriseStatus", result => {
            const status = result.data;
            get("settings/subscriptionCode", result => {
                const code = result.data;
                get("settings/legacyBrandingName", result => {
                    const legacyBrandingName = result.data;
                    get("settings/lockedToOneDownloadedBook", result => {
                        this.setState({ legacyBrandingName });
                        this.setControlState(
                            status,
                            code,
                            legacyBrandingName,
                            result.data
                        );
                    });
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
        } catch (e) {
            //swallow
        }
    }

    private codesUrl =
        "https://gateway.sil.org/display/LSDEV/Bloom+enterprise+subscription+codes";

    public render() {
        const nowAsYYYYMMDD = new Date().toISOString().slice(0, 10);

        return (
            <div
                className="enterpriseSettings"
                css={css`
                    margin: ${tabMargins.top} ${tabMargins.side}
                        ${tabMargins.bottom};
                `}
            >
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
                                This project previously used **%0**. To continue
                                to use this you will need to enter a current
                                subscription code. Codes for SIL brandings can
                                be found [here](%1). For other subscriptions,
                                contact your project administrator.
                            </Markdown>
                        )}
                        {this.state.controlState === "SubscriptionDownload" && (
                            <Div
                                l10nKey="Settings.Enterprise.DownloadForEdit"
                                className={"legacyBrandingName"}
                            >
                                This collection is in "Download for Edit" mode.
                                The book has the same Bloom Enterprise branding
                                as when it was last uploaded.
                            </Div>
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
                                "SubscriptionUnknown" /* ||
                                (this.state.controlState ===
                                    "SubscriptionGood" &&
                                    !this.state.hasSubscriptionFiles)*/ && (
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
                                        {this.state
                                            .subscriptionExpiryStringAsYYYYMMDD &&
                                            new Date(
                                                this.state.subscriptionExpiryStringAsYYYYMMDD
                                            ).toLocaleDateString()}
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
                            // Disable if we are at or past the date for deprecating legacy subscriptions.
                            // deprecatedBrandingsExpiryDate is YYYY-MM-DD, so we can use string comparison
                            // which is safer than things that involve date which brings in time zones.
                            disabled={
                                this.state.deprecatedBrandingsExpiryDate
                                    ? this.state.deprecatedBrandingsExpiryDate <
                                      nowAsYYYYMMDD
                                    : false
                            }
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
                        None. Enterprise features will be unavailable.
                    </Radio>
                </RadioGroup>
                <br />
                <SubscriptionStatus
                    overrideSubscriptionExpiration={
                        this.state.enterpriseStatus === "None"
                            ? ""
                            : this.state.subscriptionExpiryStringAsYYYYMMDD
                    }
                    minimalUI
                />
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
        postJson("settings/enterpriseStatus", status);
        this.setControlState(status, this.state.subscriptionCode, "", false);

        if (status !== "None") {
            get("settings/enterpriseSummary", result => {
                this.setSummary(result.data);
            });
        }
    }

    private onPaste() {
        get("common/clipboardText", result =>
            this.updateSubscriptionCode(result.data)
        );
    }

    private onCopy() {
        postJson("common/clipboardText", {
            text: this.state.subscriptionCode
        });
    }

    private checkForUpdates() {
        post("common/checkForUpdates");
    }

    // Used both with a code from entering something in the text box, and
    // initializing from an axios get. Should not post. Assume we have already
    // set the code on the server. Should set one of the Subscription control states
    // in controlState, subscriptionCode, subscriptionExpiry, enterpriseStatus, and subscriptionSummary
    private setControlState(
        status: string,
        code: string,
        legacyBrandingName: string,
        lockedToOneDownloadedBook: boolean
    ) {
        // as part of squashing time zone issues, we use strings that omit times, and only use dates when we're ready to display
        const nowAsYYYYMMDD = new Date().toISOString().slice(0, 10);
        if (legacyBrandingName) {
            this.setState({
                enterpriseStatus: "Subscription",
                subscriptionCode: code,
                subscriptionExpiryStringAsYYYYMMDD: "",
                controlState: lockedToOneDownloadedBook
                    ? "SubscriptionDownload"
                    : "SubscriptionLegacy",
                subscriptionSummary: ""
            });
            get("settings/enterpriseSummary", result => {
                this.setSummary(result.data);
            });
            return;
        }
        this.setState({ enterpriseStatus: status });

        if (status === "None") {
            this.setState({
                controlState: "None",
                subscriptionCode: code,
                subscriptionExpiryStringAsYYYYMMDD: "",
                subscriptionSummary: ""
            });
            return;
        } else if (status === "Community") {
            this.setState({
                controlState: "Community",
                subscriptionCode: code,
                subscriptionExpiryStringAsYYYYMMDD: this.state
                    .deprecatedBrandingsExpiryDate
            });
            get("settings/enterpriseSummary", result => {
                this.setSummary(result.data);
            });
            return;
        }
        get("settings/subscriptionExpiration", result => {
            if (result.data === "unknown") {
                // Valid-looking code, but not one this version knows about.
                this.setState({
                    subscriptionCode: code,
                    subscriptionExpiryStringAsYYYYMMDD: "",
                    controlState: "SubscriptionUnknown",
                    subscriptionSummary: ""
                });
                return;
            }
            if (result.data === "incomplete") {
                // Invalid code, but looks as if they haven't finished typing
                this.setState({
                    subscriptionCode: code,
                    subscriptionExpiryStringAsYYYYMMDD: "",
                    controlState: "SubscriptionIncomplete",
                    subscriptionSummary: ""
                });
                return;
            }
            const expiryString = result.data === null ? null : result.data;
            this.setState({
                subscriptionCode: code,
                subscriptionExpiryStringAsYYYYMMDD: expiryString
            });
            if (!expiryString) {
                this.setState({ controlState: "SubscriptionIncorrect" });
            } else if (expiryString < nowAsYYYYMMDD) {
                this.setState({ controlState: "SubscriptionExpired" });
            } else {
                this.setState({ controlState: "SubscriptionGood" });
            }
            if (expiryString) {
                get("settings/enterpriseSummary", result => {
                    this.setSummary(result.data);
                });
                get("settings/hasSubscriptionFiles", result => {
                    this.setState({
                        hasSubscriptionFiles: result.data === "true"
                    });
                });
            } else {
                this.setSummary("");
                this.setState({ hasSubscriptionFiles: false });
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
        postJson("settings/subscriptionCode", {
            subscriptionCode: code.trim()
        });
        this.setState({ legacyBrandingName: "" });
        this.setControlState(this.state.enterpriseStatus, code, "", false);
    }
}

WireUpForWinforms(() => <EnterpriseSettings />);
