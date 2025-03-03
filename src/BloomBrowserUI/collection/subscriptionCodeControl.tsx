/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Label } from "../react_components/l10nComponents";
import { Link } from "../react_components/link";
import { get, post, postJson } from "../utils/bloomApi";
import { FontAwesomeIcon } from "../bloomIcons";

interface SubscriptionControlsProps {
    enterpriseStatus: string;
    controlState: string;
    subscriptionCode: string;
    subscriptionExpiry: Date | null;
    onSubscriptionCodeChanged: (code: string) => void;
}

// values of controlState:
// None: the None button is selected.
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

// Component for managing subscription code input and display
export const SubscriptionControls: React.FC<SubscriptionControlsProps> = props => {
    // State that was previously managed by the parent component
    const [subscriptionSummary, setSubscriptionSummary] = React.useState("");
    const [hasSubscriptionFiles, setHasSubscriptionFiles] = React.useState(
        false
    );
    const [selectionPosition, setSelectionPosition] = React.useState<
        number | null
    >(0);

    // Fetch subscription summary and files status when controlState changes
    React.useEffect(() => {
        if (
            props.controlState === "SubscriptionGood" ||
            props.controlState === "SubscriptionExpired"
        ) {
            get("settings/enterpriseSummary", result => {
                setSubscriptionSummary(result.data);
            });
            get("settings/hasSubscriptionFiles", result => {
                setHasSubscriptionFiles(result.data === "true");
            });
        } else if (
            props.controlState === "None" ||
            props.controlState === "SubscriptionIncorrect" ||
            props.controlState === "SubscriptionIncomplete" ||
            props.controlState === "SubscriptionUnknown"
        ) {
            setSubscriptionSummary("");
            setHasSubscriptionFiles(false);
        }
    }, [props.controlState]);

    // Handle copy/paste operations
    const handleCopy = () => {
        postJson("common/clipboardText", {
            text: props.subscriptionCode
        });
    };

    const handlePaste = () => {
        get("common/clipboardText", result =>
            props.onSubscriptionCodeChanged(result.data)
        );
    };

    const checkForUpdates = () => {
        post("common/checkForUpdates");
    };

    const shouldShowRedExclamation = () => {
        return (
            props.controlState === "SubscriptionIncorrect" ||
            props.controlState === "SubscriptionUnknown" ||
            props.controlState === "SubscriptionExpired" ||
            props.controlState === "SubscriptionLegacy"
        );
    };

    const handleSubscriptionCodeChanged = (
        event: React.ChangeEvent<HTMLInputElement>
    ) => {
        // Store the selection position before React updates
        setSelectionPosition(
            (document.getElementById(
                "subscriptionCodeInput"
            ) as HTMLInputElement)?.selectionStart
        );

        props.onSubscriptionCodeChanged(event.target.value);
    };

    // Restore cursor position after update
    React.useEffect(() => {
        try {
            const codeElt = document.getElementById(
                "subscriptionCodeInput"
            ) as HTMLInputElement;
            if (codeElt && selectionPosition !== null) {
                codeElt.selectionStart = codeElt.selectionEnd = selectionPosition;
            }
        } catch (e) {
            // swallow
        }
    });

    // Don't render anything if Enterprise is not selected
    if (props.enterpriseStatus === "None" && props.controlState === "None") {
        return null;
    }

    return (
        <div>
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
                value={props.subscriptionCode}
                onChange={handleSubscriptionCodeChanged}
            />
            {props.controlState === "SubscriptionGood" && (
                <span className={"evaluationCode"}>
                    <FontAwesomeIcon icon="check" />
                </span>
            )}
            {props.controlState === "SubscriptionIncomplete" && (
                <span className={"evaluationCode"}>
                    <FontAwesomeIcon icon="question" />
                </span>
            )}
            {shouldShowRedExclamation() && (
                <span className={"evaluationCode"}>
                    <FontAwesomeIcon icon="exclamation-circle" />
                </span>
            )}
            <div className="editButton" onClick={handleCopy}>
                <div className="editButtonAlign">
                    <FontAwesomeIcon className="editIcon" icon="copy" />
                    <Label
                        className="editButtonLabel"
                        l10nKey="EditTab.CopyButton"
                    >
                        Copy
                    </Label>
                </div>
            </div>
            <div className="editButton pasteButton" onClick={handlePaste}>
                <div className="editButtonAlign">
                    <FontAwesomeIcon className="editIcon" icon="paste" />
                    <Label
                        className="editButtonLabel"
                        l10nKey="EditTab.PasteButton"
                    >
                        Paste
                    </Label>
                </div>
            </div>
            {props.controlState === "SubscriptionIncorrect" && (
                <Label
                    l10nKey="Settings.Enterprise.NotValid"
                    className={"error"}
                >
                    That code appears to be incorrect.
                </Label>
            )}
            {props.controlState === "SubscriptionIncomplete" && (
                <Label
                    l10nKey="Settings.Enterprise.Incomplete"
                    className={"incomplete"}
                >
                    The code should look like SOMENAME-123456-7890
                </Label>
            )}
            {props.controlState === "SubscriptionUnknown" && (
                <div>
                    <Label
                        l10nKey="Settings.Enterprise.UnknownCode"
                        className="error"
                    >
                        This version of Bloom does not have the artwork that
                        goes with that subscription.
                    </Label>
                    <Link
                        className="error"
                        l10nKey="Settings.Enterprise.CheckUpdates"
                        onClick={checkForUpdates}
                    >
                        Check for updates
                    </Link>
                </div>
            )}
            {props.controlState === "SubscriptionExpired" && (
                <Label
                    l10nKey="Settings.Enterprise.Expired"
                    className={"error"}
                >
                    That code has expired.
                </Label>
            )}

            {props.controlState === "SubscriptionGood" && (
                <div className={"expiration"}>
                    <Label l10nKey="Settings.Enterprise.Expiration">
                        Expires:
                    </Label>
                    <span>
                        {props.subscriptionExpiry &&
                            props.subscriptionExpiry.toLocaleDateString(
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
                className={"summary" + (subscriptionSummary ? "" : " closed")}
                dangerouslySetInnerHTML={{
                    __html: subscriptionSummary
                }}
            />
        </div>
    );
};
