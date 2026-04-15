import { css } from "@emotion/react";
import { Link as MuiLink, Step, StepIconProps, StepLabel } from "@mui/material";
import * as React from "react";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { BloomStepper } from "../../react_components/BloomStepper";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import { DialogCloseButton } from "../../react_components/BloomDialog/commonDialogComponents";

const usbDebuggingHowToVideoUrl = "https://www.youtube.com/shorts/Vox832sN_D4";

const usbDebuggingSteps = [
    "Open the Settings on your phone.",
    "Open 'About phone'. On some phones, you may need to then open 'Software information' to find the 'Build number'.",
    "Find 'Build number' and tap it 7 times until Developer options are enabled.",
    "Go back to Settings and open 'Developer options'.",
    "Turn on 'USB debugging'.",
    "Connect the phone to your computer with a USB cable and allow USB debugging if your phone asks.",
];

const UsbDebuggingStepIcon: React.FunctionComponent<StepIconProps> = (
    props,
) => {
    return (
        <span
            css={css`
                width: 24px;
                height: 24px;
                border-radius: 50%;
                border: 2px solid ${props.active ? kBloomBlue : "#9aa5b1"};
                color: ${props.active ? kBloomBlue : "#5b6572"};
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

export const UsbDebuggingHelpDialog: React.FunctionComponent<{
    open: boolean;
    onClose: () => void;
}> = (props) => {
    return (
        <BloomDialog
            open={props.open}
            onClose={props.onClose}
            onCancel={props.onClose}
            maxWidth={"sm"}
            fullWidth={true}
        >
            <DialogTitle title="How to set up your Android phone to receive apps via USB" />
            <DialogMiddle
                css={css`
                    width: 100%;
                    max-width: 560px;
                `}
            >
                <BloomStepper
                    activeStep={usbDebuggingSteps.length}
                    orientation="vertical"
                    areStepsAlwaysEnabled={true}
                    css={css`
                        .MuiStep-root {
                            padding-bottom: 10px;
                        }

                        .MuiStepLabel-label {
                            font-size: 14px;
                            line-height: 1.4;
                        }
                    `}
                >
                    {usbDebuggingSteps.map((step) => (
                        <Step key={step} completed={true} expanded={true}>
                            <StepLabel StepIconComponent={UsbDebuggingStepIcon}>
                                {step}
                            </StepLabel>
                        </Step>
                    ))}
                </BloomStepper>
                <div
                    css={css`
                        margin-top: 8px;
                    `}
                >
                    <MuiLink href={usbDebuggingHowToVideoUrl} underline="hover">
                        How to video
                    </MuiLink>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCloseButton onClick={props.onClose} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
