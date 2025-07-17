/**
 * @jsx jsx
 * @jsxFrag React.Fragment
 **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Label } from "../react_components/l10nComponents";
import { Link } from "../react_components/link";
import { get, post, postJson, useApiStringState } from "../utils/bloomApi";
import { FontAwesomeIcon } from "../bloomIcons";
import Button from "@mui/material/Button";
import { Stack } from "@mui/material";
import ContentCopy from "@mui/icons-material/ContentCopy";
import ContentPaste from "@mui/icons-material/ContentPaste";
import { useState } from "react";
import {
    SubscriptionCodeIntegrity,
    useLocalizedTier,
    useSubscriptionInfo
} from "./useSubscriptionInfo";
import { NoteBox, WarningBox } from "../react_components/boxes";

type Status =
    | "None"
    | "SubscriptionGood"
    | "NoBrandingFilesYet"
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
// NoBrandingFilesYet: a valid code has been entered, but this version of Bloom does not recognize it
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

// Component for managing subscription code input and display
export const SubscriptionControls: React.FC = () => {
    const {
        code,
        tier,
        subscriptionCodeIntegrity,
        expiryDateStringAsYYYYMMDD,
        subscriptionSummary,
        missingBrandingFiles,
        editingBlorgBook,
        haveData
    } = useSubscriptionInfo();

    const [status, setStatus] = useState<Status>("None");

    React.useEffect(() => {
        setStatus(
            getStatus(
                code,
                subscriptionCodeIntegrity,
                expiryDateStringAsYYYYMMDD,
                editingBlorgBook,
                missingBrandingFiles
            )
        );
    }, [
        code,
        expiryDateStringAsYYYYMMDD,
        editingBlorgBook,
        subscriptionCodeIntegrity,
        missingBrandingFiles
    ]);

    if (!haveData) {
        return null;
    }

    return (
        <div
            css={css`
                //gap: 10px;
                display: flex;
                flex-direction: column;
            `}
        >
            {status === "EditingBlorgBook" && (
                <NoteBox l10nKey="Settings.Subscription.DownloadForEdit">
                    This collection is in "Download for Edit" mode. The book has
                    the same subscription settings as when it was last uploaded.
                </NoteBox>
            )}

            <Editor status={status} />

            <StatusText
                tier={tier}
                status={status}
                expiryDateStringAsYYYYMMDD={expiryDateStringAsYYYYMMDD}
            />

            {["SubscriptionGood", "EditingBlorgBook"].includes(status) &&
                subscriptionSummary && (
                    <BrandingSummary summaryHtml={subscriptionSummary} />
                )}
        </div>
    );
};
const StatusText: React.FC<{
    tier: string;
    status: Status;
    expiryDateStringAsYYYYMMDD: string;
}> = props => {
    const localizedTier = useLocalizedTier(props.tier);
    return (
        <div>
            {props.status === "SubscriptionIncorrect" && (
                <Label
                    l10nKey="Settings.Subscription.NotValid"
                    className={"error"}
                >
                    That code appears to be incorrect.
                </Label>
            )}

            {props.status === "SubscriptionIncomplete" && (
                <Label
                    l10nKey="Settings.Subscription.Incomplete"
                    className={"incomplete"}
                >
                    The code should look like SOMENAME-123456-7890
                </Label>
            )}
            {props.status === "NoBrandingFilesYet" && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        width: 100%;
                    `}
                >
                    <WarningBox
                        l10nKey="Settings.Subscription.UnknownCode"
                        bottomRightButton={
                            <Button
                                variant="outlined"
                                onClick={() => post("common/checkForUpdates")}
                                sx={{
                                    color: "black",
                                    borderColor: "black",
                                    "&:hover": {
                                        borderColor: "black"
                                    }
                                }}
                            >
                                <Label l10nKey="Settings.Subscription.CheckUpdates">
                                    Check for updates
                                </Label>
                            </Button>
                        }
                    >
                        This version of Bloom does not have the artwork that
                        goes with that subscription.
                    </WarningBox>
                </div>
            )}
            {props.status === "SubscriptionExpired" && (
                <Label
                    l10nKey="Settings.Subscription.Expired"
                    className={"error"}
                >
                    That code has expired.
                </Label>
            )}

            {props.status === "SubscriptionGood" && (
                <div
                    css={css`
                        margin-top: 5px;
                    `}
                >
                    <Label l10nKey="Subscription.TierWithColon">Tier:</Label>{" "}
                    <span
                        css={css`
                            margin-right: 20px;
                        `}
                    >
                        {localizedTier}
                    </span>
                    <Label l10nKey="Settings.Subscription.Expiration">
                        Expires:
                    </Label>{" "}
                    <span>
                        {getSafeLocalizedDate(props.expiryDateStringAsYYYYMMDD)}
                    </span>
                </div>
            )}
        </div>
    );
};

export function getSafeLocalizedDate(dateAsYYYYMMDD: string | null) {
    const dateParts = dateAsYYYYMMDD ? dateAsYYYYMMDD.split("-") : null;
    return dateParts
        ? new Date(
              Number(dateParts[0]),
              Number(dateParts[1]) - 1,
              Number(dateParts[2])
          ).toLocaleDateString()
        : "";
}

// takes in all the info and distills down to one "mode", which then drives icons and a message to give to the user
function getStatus(
    subscriptionCode: string,
    subscriptionCodeIntegrity: SubscriptionCodeIntegrity,
    expiryDateStringAsYYYYMMDD: string,
    editingBlorgBook: boolean,
    missingBrandingFiles: boolean
): Status {
    let status = getStatusSansEditingBlorgBook(
        subscriptionCode,
        subscriptionCodeIntegrity,
        expiryDateStringAsYYYYMMDD,
        missingBrandingFiles
    );
    // I'm not 100% sure this is the best way to handle EditingBlorgBook,
    // but I'm following 6.0 which treats a full, normal subscription as normal
    // in this control, even if editingBlorgBook is true.
    // The scenario of
    //  editingBlorgBook && status === "SubscriptionGood"
    // means the user has added the full subscription code
    // since the download, either for the original subscription or another one.
    if (editingBlorgBook && status !== "SubscriptionGood")
        status = "EditingBlorgBook";

    return status;
}
function getStatusSansEditingBlorgBook(
    subscriptionCode: string,
    subscriptionCodeIntegrity: SubscriptionCodeIntegrity,
    expiryDateStringAsYYYYMMDD: string,
    missingBrandingFiles: boolean
): Status {
    const todayAsYYYYMMDD = new Date().toISOString().slice(0, 10);
    if (subscriptionCode === "" || subscriptionCodeIntegrity === "none") {
        return "None";
    }
    if (subscriptionCodeIntegrity === "invalid") return "SubscriptionIncorrect";
    // this is the case where we have a valid-looking code, but the server
    // does not have special files for it
    if (missingBrandingFiles) {
        return "NoBrandingFilesYet";
    }
    // if it looks like they haven't finished typing
    if (subscriptionCodeIntegrity === "incomplete") {
        return "SubscriptionIncomplete";
    }

    if (expiryDateStringAsYYYYMMDD < todayAsYYYYMMDD) {
        return "SubscriptionExpired";
    }
    return "SubscriptionGood";
}

export const BrandingSummary: React.FC<{ summaryHtml: string }> = props => {
    return (
        <div
            className="summary"
            css={css`
                background-color: white;
                padding: 5px;
                height: 106px;
            `}
            dangerouslySetInnerHTML={{
                __html: props.summaryHtml
            }}
        />
    );
};

const Editor: React.FC<{ status: Status }> = ({ status }) => {
    const [subscriptionCode, setSubscriptionCode] = useApiStringState(
        "settings/subscriptionCode",
        ""
    );

    // Handle copy/paste operations
    const handleCopy = () => {
        postJson("common/clipboardText", {
            text: subscriptionCode
        });
    };

    const handlePaste = () => {
        get("common/clipboardText", result => {
            setSubscriptionCode(result.data);
            document.dispatchEvent(new Event("subscriptionCodeChanged"));
        });
    };

    const shouldShowRedExclamation = () => {
        return [
            "SubscriptionIncorrect",
            "NoBrandingFilesYet",
            "SubscriptionExpired",
            "SubscriptionLegacy"
        ].includes(status);
    };
    const [selectionPosition, setSelectionPosition] = React.useState<
        number | null
    >(0); // Restore cursor position after update
    React.useEffect(() => {
        try {
            const codeElt = document.getElementById(
                "subscriptionCodeInput"
            ) as HTMLInputElement;
            if (codeElt !== null && selectionPosition !== null) {
                codeElt.selectionStart = codeElt.selectionEnd = selectionPosition;
            }
        } catch (e) {
            console.error("Error restoring cursor position:", e);
        }
    }, [selectionPosition]);
    const userTypedOrPastedCode = (
        event: React.ChangeEvent<HTMLInputElement>
    ) => {
        // Store the selection position before React updates
        setSelectionPosition(
            (document.getElementById(
                "subscriptionCodeInput"
            ) as HTMLInputElement)?.selectionStart
        );

        setSubscriptionCode(event.target.value);
        document.dispatchEvent(new Event("subscriptionCodeChanged"));
    };

    return (
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
                    l10nKey="Settings.Subscription.SubscriptionCodeLabel"
                >
                    Subscription Code:
                </Label>

                <input
                    id="subscriptionCodeInput"
                    //className="subscriptionCodeInput"
                    type="text"
                    value={
                        status === "EditingBlorgBook"
                            ? getSubscriptionCodeToDisplayForEditingBlorgBook(
                                  subscriptionCode
                              )
                            : subscriptionCode
                    }
                    onChange={userTypedOrPastedCode}
                    css={css`
                        width: 260px;
                        margin-left: 5px;
                        padding-right: 20px; // clear of icon
                        flex-grow: 1;
                        font-family: Consolas, monospace; // show zeros distinctly
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
    );
};

function getSubscriptionCodeToDisplayForEditingBlorgBook(
    subscriptionCode: string
): string {
    let result = subscriptionCode;
    result = result.replace("-***-***", "");
    return result === "Default" ? "" : result;
}
