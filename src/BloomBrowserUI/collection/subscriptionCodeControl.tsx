/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Div, Label } from "../react_components/l10nComponents";
import { Link } from "../react_components/link";
import {
    get,
    post,
    postJson,
    useApiBoolean,
    useApiState,
    useApiStringState
} from "../utils/bloomApi";
import { FontAwesomeIcon } from "../bloomIcons";
import Button from "@mui/material/Button";
import { Stack } from "@mui/material";
import ContentCopy from "@mui/icons-material/ContentCopy";
import ContentPaste from "@mui/icons-material/ContentPaste";
import { Markdown } from "../react_components/markdown";

type Status =
    | "None"
    | "SubscriptionGood"
    | "SubscriptionUnknown"
    | "SubscriptionExpired"
    | "SubscriptionIncomplete"
    | "SubscriptionIncorrect"
    | "SubscriptionLegacy"
    | "EditingBlorgBook";

// values of Status:
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
// EditingBlorgBook: a special case where a subscription code is incomplete
//      because we are using a branding permitted in a special collection made by
//      downloading a book for editing from BloomLibrary. We show a special message.

// Custom event for subscription code changes
const dispatchSubscriptionChangedEvent = () => {
    const event = new Event("subscriptionCodeChanged");
    document.dispatchEvent(event);
};

// Component for managing subscription code input and display
export const SubscriptionControls: React.FC = () => {
    const [status, setStatus] = React.useState<Status>("None");
    const [subscriptionCode, setSubscriptionCode] = useApiStringState(
        "settings/subscriptionCode",
        ""
    );
    const [editingBlorgBook] = useApiBoolean(
        "settings/lockedToOneDownloadedBook",
        false
    );

    const [
        expiryDateStringAsYYYYMMDD,
        setExpiryDateStringAsYYYYMMDD
    ] = useApiState("settings/subscriptionExpiration", "pending");

    const [subscriptionSummary, setSubscriptionSummary] = React.useState<
        string
    >("");
    const [selectionPosition, setSelectionPosition] = React.useState<
        number | null
    >(0);

    // Whenever subscription code changes, trigger a refresh of the expiry date and summary
    React.useEffect(() => {
        get("settings/subscriptionExpiration", result => {
            setExpiryDateStringAsYYYYMMDD(result.data);
            // Dispatch event to notify parent components
            dispatchSubscriptionChangedEvent();
        });
        get("settings/subscriptionSummary", result => {
            setSubscriptionSummary(result.data);
        });
        dispatchSubscriptionChangedEvent();
    }, [subscriptionCode, setExpiryDateStringAsYYYYMMDD]);

    React.useEffect(() => {
        setStatus(
            getStatus(
                subscriptionCode,
                expiryDateStringAsYYYYMMDD,
                editingBlorgBook
            )
        );
    }, [subscriptionCode, expiryDateStringAsYYYYMMDD, editingBlorgBook]);

    // Handle copy/paste operations
    const handleCopy = () => {
        postJson("common/clipboardText", {
            text: subscriptionCode
        });
    };

    const handlePaste = () => {
        get("common/clipboardText", result => {
            setSubscriptionCode(result.data);
        });
    };

    const shouldShowRedExclamation = () => {
        return (
            status === "SubscriptionIncorrect" ||
            status === "SubscriptionUnknown" ||
            status === "SubscriptionExpired" ||
            status === "SubscriptionLegacy"
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

        setSubscriptionCode(event.target.value);
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

    return (
        <div
            css={css`
                //gap: 10px;
                display: flex;
                flex-direction: column;
            `}
        >
            {editingBlorgBook && (
                <Div
                    l10nKey="Settings.Enterprise.DownloadForEdit"
                    className={"legacyBrandingName"}
                >
                    This collection is in "Download for Edit" mode. The book has
                    the same Bloom Enterprise branding as when it was last
                    uploaded.
                </Div>
            )}

            <div
                css={css`
                    display: flex;
                    flex-direction: row;
                    align-items: baseline;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        flex-direction: row;
                        align-items: baseline;
                        flex-grow: 1;
                    `}
                >
                    <Label
                        className="subscriptionCodeLabel"
                        l10nKey="Settings.Enterprise.SubscriptionCode"
                    >
                        Subscription Code:
                    </Label>

                    <input
                        id="subscriptionCodeInput"
                        //className="subscriptionCodeInput"
                        type="text"
                        value={subscriptionCode}
                        onChange={handleSubscriptionCodeChanged}
                        css={css`
                            width: 260px;
                            margin-left: 5px;
                            padding-right: 20px; // clear of icon
                            flex-grow: 1;
                            font-family: "Consolas"; // show zeros distinctly
                            padding: 5px;
                        `}
                    />
                    {status === "SubscriptionGood" && (
                        <span className={"evaluationCode"}>
                            <FontAwesomeIcon icon="check" />
                        </span>
                    )}
                    {status === "SubscriptionIncomplete" && (
                        <span className={"evaluationCode"}>
                            <FontAwesomeIcon icon="question" />
                        </span>
                    )}
                    {shouldShowRedExclamation() && (
                        <span className={"evaluationCode"}>
                            <FontAwesomeIcon
                                icon="exclamation-circle"
                                css={{ color: "red" }}
                            />
                        </span>
                    )}
                </div>
                <Button variant="text" onClick={handleCopy} size="small">
                    <Stack direction="column" alignItems="center">
                        <ContentCopy />
                        <Label
                            className="editButtonLabel"
                            l10nKey="EditTab.CopyButton"
                        >
                            Copy
                        </Label>
                    </Stack>
                </Button>
                <Button variant="text" onClick={handlePaste} size="small">
                    <Stack direction="column" alignItems="center">
                        <ContentPaste />
                        <Label
                            className="editButtonLabel"
                            l10nKey="EditTab.PasteButton"
                        >
                            Paste
                        </Label>
                    </Stack>
                </Button>
            </div>
            <StatusText
                status={status}
                expiryDateStringAsYYYYMMDD={expiryDateStringAsYYYYMMDD}
            />

            <div
                className="summary"
                css={css`
                    height: ${subscriptionSummary ? "106px" : "0px"};
                `}
                dangerouslySetInnerHTML={{
                    __html: subscriptionSummary
                }}
            />
        </div>
    );
};
const StatusText: React.FC<{
    status: Status;
    expiryDateStringAsYYYYMMDD: string;
}> = props => (
    <div>
        {props.status === "SubscriptionIncorrect" && (
            <Label l10nKey="Settings.Enterprise.NotValid" className={"error"}>
                That code appears to be incorrect.
            </Label>
        )}

        {props.status === "SubscriptionIncomplete" && (
            <Label
                l10nKey="Settings.Enterprise.Incomplete"
                className={"incomplete"}
            >
                The code should look like SOMENAME-123456-7890
            </Label>
        )}
        {props.status === "SubscriptionUnknown" && (
            <div>
                <Label
                    l10nKey="Settings.Enterprise.UnknownCode"
                    className="error"
                >
                    This version of Bloom does not have the artwork that goes
                    with that subscription.
                </Label>
                <Link
                    className="error"
                    l10nKey="Settings.Enterprise.CheckUpdates"
                    onClick={() => post("common/checkForUpdates")}
                >
                    Check for updates
                </Link>
            </div>
        )}
        {props.status === "SubscriptionExpired" && (
            <Label l10nKey="Settings.Enterprise.Expired" className={"error"}>
                That code has expired.
            </Label>
        )}

        {props.status === "SubscriptionGood" && (
            <div className={"expiration"}>
                <Label l10nKey="Settings.Enterprise.Expiration">Expires:</Label>
                <span>
                    {getSafeLocalizedDate(props.expiryDateStringAsYYYYMMDD)}
                </span>
            </div>
        )}
    </div>
);
export function getSafeLocalizedDate(dateAsYYYYMMDD: string | undefined) {
    const dateParts = dateAsYYYYMMDD?.split("-");
    return dateParts
        ? new Date(
              Number(dateParts[0]),
              Number(dateParts[1]) - 1,
              Number(dateParts[2])
          ).toLocaleDateString()
        : "";
}

function getStatus(
    subscriptionCode: string,
    expiryDateStringAsYYYYMMDD: string,
    editingBlorgBook: boolean
): Status {
    console.log(
        `getStatus(${subscriptionCode}, ${expiryDateStringAsYYYYMMDD})`
    );
    const todayAsYYYYMMDD = new Date().toISOString().slice(0, 10);
    if (subscriptionCode === "") {
        return "None";
    }
    if (!expiryDateStringAsYYYYMMDD || expiryDateStringAsYYYYMMDD === "invalid")
        return "SubscriptionIncorrect";
    if (expiryDateStringAsYYYYMMDD < todayAsYYYYMMDD) {
        return "SubscriptionExpired";
    }
    // this is the case where we have a valid-looking code, but the server
    // does not have special files for it
    if (expiryDateStringAsYYYYMMDD === "unknown") {
        return "SubscriptionUnknown";
    }
    // i think this happens if it looks like they haven't finished typing
    if (expiryDateStringAsYYYYMMDD === "incomplete") {
        return "SubscriptionIncomplete";
    }
    if (expiryDateStringAsYYYYMMDD <= todayAsYYYYMMDD) {
        return "SubscriptionExpired";
    }
    if (editingBlorgBook) {
        return "EditingBlorgBook";
    }
    return "SubscriptionGood";
}
