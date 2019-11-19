import * as React from "react";
import { Button, Typography } from "@material-ui/core";
import "./ProblemDialog.less";
import ArrowBack from "@material-ui/icons/ArrowBack";
import { BloomApi } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";

export const PrivacyScreen: React.FunctionComponent<{
    includeBook: boolean;
    email: string;
    userInput: string;
    onBack: () => void;
}> = props => {
    const log = BloomApi.useApiString(
        `problemReport/diagnosticInfo?includeBook=${
            props.includeBook ? "true" : "false"
        }&email=${props.email}&userInput=${props.userInput}`,
        "Loading..."
    );
    const privateEmailAddress = "private@bloomlibrary.org";
    const localizedBack = useL10n("Back", "Common.BackButton");
    const localizedPrivacyInfoMessage = useL10n(
        "Your report will go into our issue tracking system and will be visible via the web. If you have something private to say, please email to '{0}'.",
        "ReportProblemDialog.PrivacyInfo",
        undefined,
        privateEmailAddress
    );
    const localizedPrivacyIntro = useL10n(
        "Bloom will include the following diagnostic information with your report:",
        "ReportProblemDialog.DiagnosticReportIntro"
    );

    return (
        <div id="privacyDetails">
            <Button
                // We are not using BloomButton here because it doesn't handle the Mui ArrowBack icon.
                variant="contained"
                color="primary"
                onClick={() => props.onBack()}
            >
                <ArrowBack />
                {localizedBack}
            </Button>
            <Typography className="privacy_info">
                {localizedPrivacyInfoMessage}
            </Typography>
            <Typography className="intro_to_report">
                {localizedPrivacyIntro}
            </Typography>
            <code id="report">{log}</code>
        </div>
    );
};
