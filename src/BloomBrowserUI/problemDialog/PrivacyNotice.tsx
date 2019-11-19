import * as React from "react";
import { Button, Typography } from "@material-ui/core";
import "./ProblemDialog.less";
import WarningIcon from "@material-ui/icons/Warning";
import { useL10n } from "../react_components/l10nHooks";

export const PrivacyNotice: React.FunctionComponent<{
    onLearnMore: () => void;
}> = props => {
    return (
        <div id="privacy">
            <WarningIcon color="primary" />
            <Typography>
                {useL10n(
                    "Bloom will include diagnostic information with your report. Your report will not be private.",
                    "ReportProblemDialog.PrivacyNotice"
                )}
            </Typography>
            <Button color="primary" onClick={() => props.onLearnMore()}>
                {useL10n("Learn More...", "Common.LearnMore")}
            </Button>
        </div>
    );
};
