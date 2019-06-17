import * as React from "react";
import Typography from "@material-ui/core/Typography";
import { withStyles } from "@material-ui/styles";
import "./ProblemDialog.less";
import Slider from "@material-ui/lab/Slider";

export const HowMuchGroup: React.FunctionComponent<{}> = () => {
    return (
        <div id="how_much_group">
            <Typography>How much has this happened?</Typography>
            <HowMuchSlider
                id="slider"
                defaultValue={1}
                min={0}
                max={2}
                step={1}
                //onChange={this.handleChange}
                marks={[
                    {
                        value: 0,
                        label: "" //"First Time"
                    },
                    {
                        value: 1,
                        label: ""
                    },
                    {
                        value: 2,
                        label: "" //"It keeps happening"
                    }
                ]}
            />
            <div id="scale_labels">
                <Typography variant="body2">First Time</Typography>
                <Typography variant="body2">It keeps happening</Typography>
            </div>
        </div>
    );
};

// The classnames used have runtime numbers, so it's not possible to
// do the styling just with css, have to use MUI's style system:
const HowMuchSlider = withStyles({
    track: {
        height: 2
    },
    rail: {
        height: 2,
        //opacity: 0.5,
        backgroundColor: "#bfbfbf"
    },
    mark: {
        width: 6,
        height: 6,
        // //border-radius: 4px;
        backgroundColor: "lightgray",
        marginTop: -2,
        borderRadius: 3
    },
    markActive: {
        backgroundColor: "currentColor"
    }
})(Slider);
