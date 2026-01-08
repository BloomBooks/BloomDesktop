import * as React from "react";
import { Typography } from "@mui/material";
import "./ProblemDialog.less";
import WarningIcon from "@mui/icons-material/Warning";
import { useL10n } from "../react_components/l10nHooks";
import BloomButton from "../react_components/bloomButton";

export const PrivacyNotice: React.FunctionComponent<{
    onLearnMore: () => void;
}> = (props) => {
    return (
        <div id="privacy">
            <WarningIcon color="error" />
            <Typography>
                {useL10n(
                    "Bloom will include diagnostic information with your report. Your report will not be private.",
                    "ReportProblemDialog.PrivacyNotice",
                )}
            </Typography>
            <BloomButton
                l10nKey="Common.LearnMore"
                enabled={true}
                variant="text"
                hasText={true}
                onClick={() => props.onLearnMore()}
            >
                Learn More
            </BloomButton>
        </div>
    );
};
