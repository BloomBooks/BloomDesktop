import * as React from "react";
import { Typography } from "@material-ui/core";
import "./ProblemDialog.less";
import ArrowBack from "@material-ui/icons/ArrowBack";
import { BloomApi } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";
import BloomButton from "../react_components/bloomButton";

export const PrivacyScreen: React.FunctionComponent<{
    includeBook: boolean;
    email: string;
    userInput: string;
    onBack: () => void;
}> = props => {
    const localizedLoadingMsg = useL10n(
        "Loading...",
        "Common.Loading",
        "This is shown when Bloom is slowly loading something, so the user doesn't worry about why they don't see the result immediately."
    );
    const log = BloomApi.useApiString(
        `problemReport/diagnosticInfo?includeBook=${
            props.includeBook ? "true" : "false"
        }&email=${props.email}&userInput=${props.userInput}`,
        localizedLoadingMsg
    );
    const privateEmailAddress = "private@bloomlibrary.org";
    const localizedPrivacyInfoMessage = useL10n(
        "Your report will go into our issue tracking system and will be visible via the web. If you have something private to say, please email to '{0}'.",
        "ReportProblemDialog.PrivacyInfo",
        "The {0} is where the Bloom team's private email address will be inserted.",
        privateEmailAddress
    );
    const localizedPrivacyIntro = useL10n(
        "Bloom will include the following diagnostic information with your report:",
        "ReportProblemDialog.DiagnosticReportIntro"
    );

    return (
        <div id="privacyDetails">
            <div className="buttonWrapper">
                <BloomButton
                    className="backButton"
                    enabled={true}
                    hasText={true}
                    l10nKey="Common.BackButton"
                    variant="contained"
                    onClick={() => props.onBack()}
                    iconBeforeText={<ArrowBack />}
                >
                    Back
                </BloomButton>
            </div>
            <div className="privacy_report">
                <Typography className="privacy_info">
                    {localizedPrivacyInfoMessage}
                </Typography>
                <Typography className="intro_to_report">
                    {localizedPrivacyIntro}
                </Typography>
                <code id="report">{log}</code>
            </div>
        </div>
    );
};
