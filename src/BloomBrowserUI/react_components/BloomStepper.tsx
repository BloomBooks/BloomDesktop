/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Stepper } from "@mui/material";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { StepperProps } from "@mui/material/Stepper";

export const BloomStepper: React.FunctionComponent<{
    defeatDisabling?: boolean;
} & StepperProps> = props => {
    const cssForDefeatingDisabling = css`
        .MuiStepLabel-label {
            color: black !important;
        }
        .MuiStepIcon-root {
            color: ${kBloomBlue} !important;
        }
    `;
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const { defeatDisabling, ...propsForStepper } = props;
    return (
        <Stepper
            // We need to defeat Material-UI's attempt to make the step numbers and text look disabled.
            css={css`
                .MuiStepLabel-label {
                    font-size: larger;
                }
                ${props.defeatDisabling ? cssForDefeatingDisabling : ""}
            `}
            {...propsForStepper}
        ></Stepper>
    );
};
