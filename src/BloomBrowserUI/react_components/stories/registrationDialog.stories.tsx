import { Meta, StoryObj } from "@storybook/react";
import { showRegistrationDialog } from "../registration/registrationDialog";
import { normalDialogEnvironmentForStorybook } from "../BloomDialog/BloomDialogPlumbing";

const meta: Meta = {
    title: "Misc/Dialogs/RegistrationDialog",
};

export default meta;

type Story = StoryObj;

export const NormalStory: Story = {
    name: "Normal Dialog",
    render: () => {
        showRegistrationDialog({
            emailRequiredForTeamCollection: false,
            dialogEnvironment: normalDialogEnvironmentForStorybook,
        });
        return <div>Dialog should be open</div>;
    },
};

export const EmailRequiredStory: Story = {
    name: "Email required",
    render: () => {
        showRegistrationDialog({
            emailRequiredForTeamCollection: true,
            dialogEnvironment: normalDialogEnvironmentForStorybook,
        });
        return <div>Dialog should be open</div>;
    },
};
