import * as React from "react";
import Typography from "@material-ui/core/Typography";
import "./ProblemDialog.less";
import { Button } from "@material-ui/core";
import WarningIcon from "@material-ui/icons/Warning";

export const PrivacyGroup: React.FunctionComponent<{}> = () => {
    return (
        <div id="privacy">
            <WarningIcon color="primary" />
            <Typography>
                Bloom will include diagnostic information with your report. Your
                report will not be private.
            </Typography>
            <Button color="primary">Learn More...</Button>
        </div>
    );
};
