import { Meta, StoryObj } from "@storybook/react";
import { RegistrationDialogLauncher } from "../registrationDialog";
import { StorybookDialogWrapper } from "../BloomDialog/BloomDialogPlumbing";

const meta: Meta = {
    title: "Misc/Dialogs/RegistrationDialog"
};

export default meta;

type Story = StoryObj;

export const NormalStory: Story = {
    name: "Normal Dialog",
    render: () => (
        <StorybookDialogWrapper id="RegistrationDialog" params={{}}>
            <RegistrationDialogLauncher
                mayChangeEmail={true}
                registrationIsOptional={true}
                emailRequiredForTeamCollection={false}
            />
        </StorybookDialogWrapper>
    )
};

export const EmailRequiredStory: Story = {
    name: "Email required",
    render: () => (
        <StorybookDialogWrapper id="RegistrationDialog" params={{}}>
            <RegistrationDialogLauncher
                mayChangeEmail={true}
                registrationIsOptional={false}
                emailRequiredForTeamCollection={true}
            />
        </StorybookDialogWrapper>
    )
};

export const EmailReadonlyStory: Story = {
    name: "Email readonly",
    render: () => (
        <StorybookDialogWrapper id="RegistrationDialog" params={{}}>
            <RegistrationDialogLauncher
                mayChangeEmail={false}
                registrationIsOptional={false}
                emailRequiredForTeamCollection={false}
            />
        </StorybookDialogWrapper>
    )
};
