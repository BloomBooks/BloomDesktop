import * as React from "react";
import Typography from "@mui/material/Typography";
import { withStyles } from "@mui/styles";
import "./ProblemDialog.less";
import Slider from "@mui/material/Slider";
import { useL10n } from "../react_components/l10nHooks";

export const HowMuchGroup: React.FunctionComponent<{
    onHowMuchChange: (value: number) => void;
}> = (props) => {
    const localizedHowMuch = useL10n(
        "How much has this happened?",
        "ReportProblemDialog.HowMuch",
        "The label above the frequency slider.",
    );
    const localizedStartLabel = useL10n(
        "First Time",
        "ReportProblemDialog.FirstTime",
        "The begin point label for the frequency slider.",
    );
    const localizedEndLabel = useL10n(
        "It keeps happening",
        "ReportProblemDialog.ItKeepsHappening",
        "The end point label for the frequency slider.",
    );
    return (
        <div id="how_much_group">
            <Typography>{localizedHowMuch}</Typography>
            <HowMuchSlider
                id="slider"
                defaultValue={1}
                min={0}
                max={2}
                step={1}
                onChange={(event, value: number) =>
                    props.onHowMuchChange(value)
                }
                size="small"
                marks={true}
            />
            <div id="scale_labels">
                <Typography variant="body2">{localizedStartLabel}</Typography>
                <Typography variant="body2">{localizedEndLabel}</Typography>
            </div>
        </div>
    );
};

// The classnames used have runtime numbers, so it's not possible to
// do the styling just with css, have to use MUI's style system:
const HowMuchSlider = withStyles({
    rail: {
        backgroundColor: "#bfbfbf",
    },
    mark: {
        width: 6,
        height: 6,
        backgroundColor: "lightgray",
        borderRadius: 3,
    },
    markActive: {
        backgroundColor: "currentColor",
    },
})(Slider);
