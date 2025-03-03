/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState, useEffect, useCallback, useMemo } from "react";
import { Div, Label } from "../react_components/l10nComponents";
import { Markdown } from "../react_components/markdown";
import { get, postJson } from "../utils/bloomApi";
import "./enterpriseSettings.less";
import { tabMargins } from "./commonTabSettings";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { SubscriptionStatus } from "../collectionsTab/SubscriptionStatus";
import { NoteBox } from "../react_components/boxes";
import { Checkbox } from "../react_components/checkbox";
import { SubscriptionControls } from "./subscriptionCodeControl";

// This component implements the Bloom Enterprise tab of the Settings dialog.
export const EnterpriseSettings: React.FunctionComponent = () => {
    const [enterpriseStatus, setEnterpriseStatus] = useState("None");
    const [subscriptionCode, setSubscriptionCode] = useState("");
    const [subscriptionExpiry, setSubscriptionExpiry] = useState<Date | null>(
        null
    );
    const [subscriptionSummary, setSubscriptionSummary] = useState("");
    const [hasSubscriptionFiles, setHasSubscriptionFiles] = useState(false);
    const [controlState, setControlState] = useState("None");
    const [legacyBrandingName, setLegacyBrandingName] = useState("");
    const [
        deprecatedBrandingsExpiryDate,
        setDeprecatedBrandingsExpiryDate
    ] = useState("");

    // URL is a constant, so define it with useMemo
    const codesUrl = useMemo(
        () =>
            "https://gateway.sil.org/display/LSDEV/Bloom+enterprise+subscription+codes",
        []
    );

    const handleSetSummary = useCallback((content: string) => {
        setSubscriptionSummary(content);
    }, []);

    // Get summary information from the API
    const fetchSummary = useCallback(() => {
        get("settings/enterpriseSummary", result => {
            handleSetSummary(result.data);
        });
    }, [handleSetSummary]);

    // Used both with a code from entering something in the text box, and
    // initializing from an axios get. Should not post. Assume we have already
    // set the code on the server. Should set one of the Subscription control states
    // in controlState, subscriptionCode, subscriptionExpiry, enterpriseStatus, and subscriptionSummary
    const handleSetControlState = useCallback(
        (
            status: string,
            code: string,
            brandingName: string,
            lockedToOneDownloadedBook: boolean
        ) => {
            if (brandingName) {
                setEnterpriseStatus("Subscription");
                setSubscriptionCode(code);
                setSubscriptionExpiry(null);
                setControlState(
                    lockedToOneDownloadedBook
                        ? "SubscriptionDownload"
                        : "SubscriptionLegacy"
                );
                setSubscriptionSummary("");
                fetchSummary();
                return;
            }

            setEnterpriseStatus(status);
            if (status === "None") {
                setControlState("None");
                setSubscriptionCode(code);
                setSubscriptionExpiry(null);
                setSubscriptionSummary("");
                return;
            }

            get("settings/subscriptionExpiration", result => {
                if (result.data === "unknown") {
                    // Valid-looking code, but not one this version knows about.
                    setSubscriptionCode(code);
                    setSubscriptionExpiry(null);
                    setControlState("SubscriptionUnknown");
                    setSubscriptionSummary("");
                    return;
                }

                if (result.data === "incomplete") {
                    // Invalid code, but looks as if they haven't finished typing
                    setSubscriptionCode(code);
                    setSubscriptionExpiry(null);
                    setControlState("SubscriptionIncomplete");
                    setSubscriptionSummary("");
                    return;
                }

                const expiry =
                    result.data === null ? null : new Date(result.data);
                setSubscriptionCode(code);
                setSubscriptionExpiry(expiry);

                if (!expiry) {
                    setControlState("SubscriptionIncorrect");
                    setSubscriptionSummary("");
                    setHasSubscriptionFiles(false);
                } else if (expiry < new Date()) {
                    setControlState("SubscriptionExpired");
                    fetchSummary();
                    get("settings/hasSubscriptionFiles", result => {
                        setHasSubscriptionFiles(result.data === "true");
                    });
                } else {
                    setControlState("SubscriptionGood");
                    fetchSummary();
                    get("settings/hasSubscriptionFiles", result => {
                        setHasSubscriptionFiles(result.data === "true");
                    });
                }
            });
        },
        [fetchSummary]
    );

    const handleSetStatus = useCallback(
        (status: string) => {
            postJson("settings/enterpriseStatus", status);
            handleSetControlState(status, subscriptionCode, "", false);

            if (status !== "None") {
                fetchSummary();
            }
        },
        [subscriptionCode, handleSetControlState, fetchSummary]
    );

    const updateSubscriptionCode = useCallback(
        (code: string) => {
            const trimmedCode = code.trim();
            postJson("settings/subscriptionCode", {
                subscriptionCode: trimmedCode
            });
            setLegacyBrandingName("");
            handleSetControlState(enterpriseStatus, trimmedCode, "", false);
        },
        [enterpriseStatus, handleSetControlState]
    );

    // Load initial data
    useEffect(() => {
        // Fetch deprecated brandings expiry date
        get("settings/deprecatedBrandingsExpiryDate", result => {
            setDeprecatedBrandingsExpiryDate(result.data);
        });

        // Use Promise pattern to chain API calls and avoid excessive nesting
        const fetchInitialData = async () => {
            try {
                // Get enterprise status
                const statusResult = await new Promise<any>(resolve => {
                    get("settings/enterpriseStatus", resolve);
                });
                const status = statusResult.data;

                // Get subscription code
                const codeResult = await new Promise<any>(resolve => {
                    get("settings/subscriptionCode", resolve);
                });
                const code = codeResult.data;

                // Get legacy branding name
                const brandingResult = await new Promise<any>(resolve => {
                    get("settings/legacyBrandingName", resolve);
                });
                const branding = brandingResult.data;

                // Get locked to one downloaded book
                const lockedResult = await new Promise<any>(resolve => {
                    get("settings/lockedToOneDownloadedBook", resolve);
                });

                setLegacyBrandingName(branding);
                handleSetControlState(
                    status,
                    code,
                    branding,
                    lockedResult.data
                );
            } catch (error) {
                console.error("Error fetching initial data:", error);
            }
        };

        fetchInitialData();
    }, [handleSetControlState]);

    // Derived state for subscription expiration display
    const subscriptionExpiryDisplay = useMemo(() => {
        return enterpriseStatus === "None"
            ? ""
            : subscriptionExpiry?.toISOString();
    }, [enterpriseStatus, subscriptionExpiry]);

    return (
        <div
            className="enterpriseSettings"
            css={css`
                margin: ${tabMargins.top} ${tabMargins.side}
                    ${tabMargins.bottom};
            `}
        >
            <Markdown l10nKey="Settings.Subscription.IntroText">
                To help cover a portion of the costs associated with providing
                Bloom, we offer [advanced features](%1) and customizations as a
                subscription service.
            </Markdown>
            <Markdown
                l10nKey="Settings.Subscription.RequestLicense"
                l10nParam0={"subscriptions@bloomlibrary.org"}
                l10nParam1={"mailto://subscriptions@bloomlibrary.org"}
            >
                Please contact [%1](%2) to request your license.
            </Markdown>
            <Checkbox
                l10nKey="Common.Subscription"
                checked={enterpriseStatus === "Subscription"}
                onCheckChanged={checked =>
                    handleSetStatus(checked ? "Subscription" : "None")
                }
            >
                I have a subscription code
            </Checkbox>

            {controlState === "SubscriptionLegacy" && (
                <Markdown
                    l10nKey="Settings.Enterprise.NeedsCode"
                    l10nParam0={legacyBrandingName}
                    l10nParam1={codesUrl}
                    className={"legacyBrandingName"}
                >
                    This project previously used **%0**. To continue to use this
                    you will need to enter a current subscription code. Codes
                    for SIL brandings can be found [here](%1). For other
                    subscriptions, contact your project administrator.
                </Markdown>
            )}

            {controlState === "SubscriptionDownload" && (
                <Div
                    l10nKey="Settings.Enterprise.DownloadForEdit"
                    className={"legacyBrandingName"}
                >
                    This collection is in "Download for Edit" mode. The book has
                    the same Bloom Enterprise branding as when it was last
                    uploaded.
                </Div>
            )}

            <SubscriptionControls
                enterpriseStatus={enterpriseStatus}
                subscriptionCode={subscriptionCode}
                controlState={controlState}
                subscriptionExpiry={subscriptionExpiry}
                onSubscriptionCodeChanged={updateSubscriptionCode}
            />

            <br />

            <SubscriptionStatus
                overrideSubscriptionExpiration={subscriptionExpiryDisplay}
                minimalUI
            />

            <NoteBox
                css={css`
                    p {
                        margin: 0; // markdown wraps everything in a p tag which adds a big margin we don't need
                    }
                `}
            >
                {/* wrap in a div with display inline */}
                <div
                    css={css`
                        display: inline;
                    `}
                >
                    <Markdown l10nKey="Settings.Enterprise.Community.Invitation">
                        If your project is fully funded and managed by your
                        local language community, you may qualify for a free
                        [Bloom Community Subscription](https://example.com).
                    </Markdown>
                    <br />
                    <Markdown
                        l10nKey="Settings.Subscription.RequestLicense"
                        l10nParam0={"subscriptions@bloomlibrary.org"}
                        l10nParam1={"mailto://subscriptions@bloomlibrary.org"}
                    >
                        Please contact [%1](%2) to request your license.
                    </Markdown>
                </div>
            </NoteBox>
        </div>
    );
};

WireUpForWinforms(() => <EnterpriseSettings />);
