import { Meta, StoryObj } from "@storybook/react";
import {
    RegistrationDialogLauncher,
    showRegistrationDialog
} from "../registrationDialog";
import { StorybookDialogWrapper } from "../BloomDialog/BloomDialogPlumbing";

const meta: Meta = {
    title: "Misc/Dialogs/RegistrationDialog"
};

export default meta;

type Story = StoryObj;

export const NormalStory: Story = {
    name: "Normal Dialog",
    render: () => {
        showRegistrationDialog({
            registrationIsOptional: true,
            emailRequiredForTeamCollection: false
        });
        return (
            <StorybookDialogWrapper id="RegistrationDialog" params={{}}>
                <RegistrationDialogLauncher />
            </StorybookDialogWrapper>
        );
    }
};

export const EmailRequiredStory: Story = {
    name: "Email required",
    render: () => {
        showRegistrationDialog({
            registrationIsOptional: false,
            emailRequiredForTeamCollection: true
        });
        return (
            <StorybookDialogWrapper id="RegistrationDialog" params={{}}>
                <RegistrationDialogLauncher />
            </StorybookDialogWrapper>
        );
    }
};
