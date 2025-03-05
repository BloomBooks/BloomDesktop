import * as React from "react";
import {
    RequiresBloomEnterpriseDialog,
    RequiresBloomEnterpriseNotice,
    RequiresBloomEnterpriseNoticeDialog,
    RequiresBloomEnterpriseOverlayWrapper
} from "../requiresBloomEnterprise";
import { normalDialogEnvironmentForStorybook } from "../BloomDialog/BloomDialogPlumbing";

import { Meta, StoryObj } from "@storybook/react";

const meta: Meta = {
    title: "RequiresBloomEnterprise"
};

export default meta;
type Story = StoryObj;

export const RequiresBloomEnterpriseNoticeDialogStory: Story = {
    name: "RequiresBloomEnterpriseNoticeDialog",
    render: () => <RequiresBloomEnterpriseNoticeDialog />
};

export const RequiresBloomEnterpriseDialogStory: Story = {
    name: "RequiresBloomEnterpriseDialog",
    render: () => (
        <RequiresBloomEnterpriseDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    )
};

export const RequiresBloomEnterpriseNoticeStory: Story = {
    name: "RequiresBloomEnterpriseNotice",
    render: () => <RequiresBloomEnterpriseNotice />
};

export const RequiresBloomEnterpriseOverlayWrapperStory: Story = {
    name: "RequiresBloomEnterpriseOverlayWrapper",
    render: () => <RequiresBloomEnterpriseOverlayWrapper />
};
