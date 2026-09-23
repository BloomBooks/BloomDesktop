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
import { useL10n } from "../../react_components/l10nHooks";

const usbDebuggingHowToVideoUrl = "https://www.youtube.com/shorts/Vox832sN_D4";

// The steps a user follows on the phone itself. Each is its own localizable
// string; the hooks are called unconditionally and in a fixed order.
const useUsbDebuggingSteps = (): string[] => [
    useL10n(
        "Open the Settings on your phone.",
        "PublishTab.Apps.UsbDebuggingDialog.OpenSettings",
    ),
    useL10n(
        "Open 'About phone'. On some phones, you may need to then open 'Software information' to find the 'Build number'.",
        "PublishTab.Apps.UsbDebuggingDialog.OpenAboutPhone",
    ),
    useL10n(
        "Find 'Build number' and tap it 7 times until Developer options are enabled.",
        "PublishTab.Apps.UsbDebuggingDialog.TapBuildNumber",
    ),
    useL10n(
        "Go back to Settings and open 'Developer options'.",
        "PublishTab.Apps.UsbDebuggingDialog.OpenDeveloperOptions",
    ),
    useL10n(
        "Turn on 'USB debugging'.",
        "PublishTab.Apps.UsbDebuggingDialog.TurnOnUsbDebugging",
    ),
    useL10n(
        "Connect the phone to your computer with a USB cable and allow USB debugging if your phone asks.",
        "PublishTab.Apps.UsbDebuggingDialog.ConnectCable",
    ),
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
    const usbDebuggingSteps = useUsbDebuggingSteps();
    const dialogTitle = useL10n(
        "How to set up your Android phone to receive apps via USB",
        "PublishTab.Apps.UsbDebuggingDialog.Title",
    );
    const howToVideoLabel = useL10n(
        "How to video",
        "PublishTab.Apps.UsbDebuggingDialog.HowToVideo",
    );
    return (
        <BloomDialog
            open={props.open}
            onClose={props.onClose}
            onCancel={props.onClose}
            maxWidth={"sm"}
            fullWidth={true}
        >
            <DialogTitle title={dialogTitle} />
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
                        {howToVideoLabel}
                    </MuiLink>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCloseButton onClick={props.onClose} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
