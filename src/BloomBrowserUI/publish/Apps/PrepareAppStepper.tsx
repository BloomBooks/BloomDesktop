import { css, keyframes } from "@emotion/react";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";
import {
    CircularProgress,
    Step,
    StepLabel,
    StepIconProps,
} from "@mui/material";
import * as React from "react";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { BloomStepper } from "../../react_components/BloomStepper";
import {
    AppBuilderPrepareStepId,
    IAppBuilderPrepareStepStatus,
} from "./appBuilderShared";

const pulse = keyframes`
    0% {
        transform: scale(1);
        opacity: 0.75;
    }

    70% {
        transform: scale(1.08);
        opacity: 1;
    }

    100% {
        transform: scale(1);
        opacity: 0.75;
    }
`;

const PrepareStepIcon: React.FunctionComponent<StepIconProps> = (props) => {
    if (props.completed) {
        return (
            <CheckCircleIcon
                css={css`
                    color: ${kBloomBlue};
                    font-size: 24px;
                `}
            />
        );
    }

    if (props.active) {
        return (
            <span
                css={css`
                    width: 24px;
                    height: 24px;
                    display: inline-flex;
                    align-items: center;
                    justify-content: center;
                    animation: ${pulse} 1.2s ease-in-out infinite;
                `}
            >
                <CircularProgress
                    size={22}
                    thickness={5}
                    css={css`
                        color: ${kBloomBlue};
                    `}
                />
            </span>
        );
    }

    return (
        <span
            css={css`
                width: 24px;
                height: 24px;
                border-radius: 50%;
                border: 2px solid #9aa5b1;
                color: #5b6572;
                display: inline-flex;
                align-items: center;
                justify-content: center;
                font-size: 12px;
                font-weight: 700;
                background: white;
            `}
        >
            {props.icon}
        </span>
    );
};

export const PrepareAppStepper: React.FunctionComponent<{
    steps: Array<IAppBuilderPrepareStepStatus & { label: string }>;
    activeStepId?: AppBuilderPrepareStepId;
    isBusy: boolean;
}> = (props) => {
    return (
        <div
            data-testid="prepare-stepper"
            css={css`
                margin-top: 12px;
                display: flex;
                max-width: 900px;
            `}
        >
            <div
                css={css`
                    overflow-x: auto;
                    padding: 2px 0;
                `}
            >
                <BloomStepper
                    alternativeLabel={true}
                    orientation="horizontal"
                    areStepsAlwaysEnabled={true}
                    css={css`
                        .MuiStep-root {
                            min-width: 0;
                            padding-left: 4px;
                            padding-right: 4px;
                        }

                        .MuiStepConnector-line {
                            border-top-width: 2px;
                        }

                        .MuiStepLabel-labelContainer {
                            min-width: 0;
                        }

                        .MuiStepLabel-label {
                            display: block;
                            margin-top: 8px;
                            font-size: 13px;
                            line-height: 1.3;
                            white-space: normal;
                            text-align: center;
                        }

                        .prepare-step-label {
                            display: inline-block;
                            width: min(100%, 11ch);
                            overflow-wrap: anywhere;
                        }

                        .MuiStepLabel-label.Mui-active {
                            color: ${kBloomBlue};
                            font-weight: 700;
                        }
                    `}
                >
                    {props.steps.map((step, index) => {
                        const stepState =
                            step.id === props.activeStepId && props.isBusy
                                ? "active"
                                : step.complete
                                  ? "complete"
                                  : "pending";

                        return (
                            <Step
                                key={step.id}
                                completed={step.complete}
                                active={stepState === "active"}
                                data-testid={`prepare-step-${step.id}`}
                                data-state={stepState}
                            >
                                <StepLabel StepIconComponent={PrepareStepIcon}>
                                    <span className="prepare-step-label">
                                        {step.label}
                                    </span>
                                </StepLabel>
                            </Step>
                        );
                    })}
                </BloomStepper>
            </div>
        </div>
    );
};
