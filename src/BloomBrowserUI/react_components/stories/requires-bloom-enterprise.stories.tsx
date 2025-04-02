import {
    RequiresSubscriptionDialog,
    RequiresSubscriptionNotice,
    RequiresSubscriptionNoticeDialog,
    RequiresSubscriptionOverlayWrapper
} from "../requiresSubscription";
import { normalDialogEnvironmentForStorybook } from "../BloomDialog/BloomDialogPlumbing";

import { Meta, StoryObj } from "@storybook/react";

const meta: Meta = {
    title: "RequiresSubscription"
};

export default meta;
type Story = StoryObj;

export const RequiresSubscriptionNoticeDialogStory: Story = {
    name: "RequiresSubscriptionNoticeDialog",
    render: () => <RequiresSubscriptionNoticeDialog />
};

export const RequiresSubscriptionDialogStory: Story = {
    name: "RequiresSubscriptionDialog",
    render: () => (
        <RequiresSubscriptionDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    )
};

export const RequiresSubscriptionNoticeStory: Story = {
    name: "RequiresSubscriptionNotice",
    render: () => <RequiresSubscriptionNotice featureName={"foobar"} />
};

export const RequiresSubscriptionOverlayWrapperStory: Story = {
    name: "RequiresSubscriptionOverlayWrapper",
    render: () => <RequiresSubscriptionOverlayWrapper featureName={"foobar"} />
};
