/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Stepper } from "@mui/material";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { StepperProps } from "@mui/material/Stepper";

export const BloomStepper: React.FunctionComponent<StepperProps> = props => {
    return (
        <Stepper
            // We need to defeat Material-UI's attempt to make the step numbers and text look disabled.
            css={css`
                .MuiStepLabel-label {
                    color: black !important;
                    font-size: larger;
                }
                .MuiStepIcon-root {
                    color: ${kBloomBlue} !important;
                }
            `}
            {...props}
        ></Stepper>
    );
};
