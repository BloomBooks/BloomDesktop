import { css } from "@emotion/react";
import * as React from "react";
import { Stepper } from "@mui/material";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { StepperProps } from "@mui/material/Stepper";

export const BloomStepper: React.FunctionComponent<
    {
        areStepsAlwaysEnabled?: boolean;
    } & StepperProps
> = (props) => {
    // Defeat Material-UI's attempt to make the step numbers and text look disabled.
    const cssForAlwaysEnabledSteps = css`
        .MuiStepLabel-label {
            color: black !important;
        }
        .MuiStepIcon-root {
            color: ${kBloomBlue} !important;
        }
    `;
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const { areStepsAlwaysEnabled, ...propsForStepper } = props;
    return (
        <Stepper
            css={css`
                .MuiStepLabel-label {
                    font-size: larger;
                }
                ${props.areStepsAlwaysEnabled ? cssForAlwaysEnabledSteps : ""}
            `}
            {...propsForStepper}
        ></Stepper>
    );
};
