import * as React from "react";
import Typography from "@material-ui/core/Typography";
import "./ProblemDialog.less";
import ArrowBack from "@material-ui/icons/ArrowBack";
import { BloomApi } from "../utils/bloomApi";
import { Button } from "@material-ui/core";
export const PrivacyScreen: React.FunctionComponent<{
    includeBook: boolean;
    onBack: () => void;
}> = props => {
    const log = BloomApi.useApiString(
        `problemReport/diagnosticInfo?includeBook=${
            props.includeBook ? "true" : "false"
        }`,
        "Loading..."
    );
    return (
        <div id="privacyDetails">
            <Button
                variant="contained"
                color="primary"
                onClick={() => props.onBack()}
            >
                <ArrowBack />
                Back
            </Button>
            <Typography className="privacy_info">
                {`Your report will go into our issue tracking system and will be visible via the the web. If you have something private to say, please email to private@${"bloomlibrary"}.org.`}
            </Typography>
            <Typography className="intro_to_report">
                Bloom will include the following diagnostic information with
                your report:
            </Typography>
            <code id="report">{log}</code>
        </div>
    );
};
