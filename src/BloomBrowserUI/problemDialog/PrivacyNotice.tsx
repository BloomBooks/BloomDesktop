import * as React from "react";
import Typography from "@material-ui/core/Typography";
import "./ProblemDialog.less";
import { Button } from "@material-ui/core";
import WarningIcon from "@material-ui/icons/Warning";
import { LocalizedString } from "../react_components/l10nComponents";

export const PrivacyNotice: React.FunctionComponent<{
    onLearnMore: () => void;
}> = props => {
    return (
        <div id="privacy">
            <WarningIcon color="primary" />
            <Typography>
                <LocalizedString l10nKey="bogus">
                    Bloom will include diagnostic information with your report.
                    Your report will not be private.
                </LocalizedString>
            </Typography>
            <Button color="primary" onClick={() => props.onLearnMore()}>
                <LocalizedString l10nKey="Common.LearnMore">
                    Learn More...
                </LocalizedString>
            </Button>
        </div>
    );
};
