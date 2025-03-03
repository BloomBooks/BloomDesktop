import * as React from "react";
import BloomButton from "../bloomButton";
import {
    showConfirmDialogFromOutsideReact,
    IConfirmDialogProps,
    ConfirmDialog,
    showConfirmDialog
} from "../confirmDialog";
import { AutoUpdateSoftwareDialog } from "../AutoUpdateSoftwareDialog";
import { ForumInvitationDialogLauncher } from "../forumInvitationDialog";
import {
    StorybookDialogWrapper,
    normalDialogEnvironmentForStorybook
} from "../BloomDialog/BloomDialogPlumbing";

import type { Meta, StoryObj } from "@storybook/react";

const meta: Meta = {
    title: "Misc/Dialogs"
};

export default meta;
type Story = StoryObj;

const confirmDialogProps: IConfirmDialogProps = {
    title: "Title",
    titleL10nKey: "",
    message: "Message",
    messageL10nKey: "",
    confirmButtonLabel: "OK",
    confirmButtonLabelL10nKey: "",
    onDialogClose: dialogResult => {
        alert(dialogResult);
    }
};

export const ConfirmDialogStory: Story = {
    name: "ConfirmDialog",
    render: () => (
        <div>
            <div id="modal-container" />
            <BloomButton
                onClick={() => showConfirmDialog()}
                enabled={true}
                hasText={true}
                l10nKey={"dummyKey"}
            >
                Open Confirm Dialog
            </BloomButton>
            <ConfirmDialog {...confirmDialogProps} />
        </div>
    )
};

export const ConfirmDialogFromOutsideReactStory: Story = {
    name: "ConfirmDialog as launched from outside React",
    render: () => (
        <div>
            <div id="modal-container" />
            <BloomButton
                onClick={() =>
                    showConfirmDialogFromOutsideReact(
                        confirmDialogProps,
                        document.getElementById("modal-container")
                    )
                }
                enabled={true}
                hasText={true}
                l10nKey={"dummyKey"}
            >
                Open Confirm Dialog
            </BloomButton>
        </div>
    )
};

export const AutoUpdateSoftwareDialogStory: Story = {
    name: "AutoUpdateSoftwareDialog",
    render: () => (
        <AutoUpdateSoftwareDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    )
};

export const ForumInvitationDialogStory: Story = {
    name: "ForumInvitationDialog",
    render: () => (
        <StorybookDialogWrapper id="ForumInvitationDialog" params={{}}>
            <ForumInvitationDialogLauncher />
        </StorybookDialogWrapper>
    )
};
