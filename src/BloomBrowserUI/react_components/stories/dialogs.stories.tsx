import BloomButton from "../bloomButton";
import {
    showConfirmDialogFromOutsideReact,
    IConfirmDialogProps,
    ConfirmDialog,
    showConfirmDialog,
} from "../confirmDialog";
import { AutoUpdateSoftwareDialog } from "../AutoUpdateSoftwareDialog";
import { ForumInvitationDialogLauncher } from "../forumInvitationDialog";
import {
    INumberChooserDialogProps,
    NumberChooserDialog,
} from "../numberChooserDialog";
import { AboutDialogLauncher } from "../aboutDialog";
import {
    StorybookDialogWrapper,
    normalDialogEnvironmentForStorybook,
} from "../BloomDialog/BloomDialogPlumbing";

import { Meta, StoryObj } from "@storybook/react-vite";
import { MakeReaderTemplateBloomPackDialog } from "../makeReaderTemplateBloomPackDialog";

const meta: Meta = {
    title: "Misc/Dialogs",
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
    onDialogClose: (dialogResult) => {
        alert(dialogResult);
    },
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
    ),
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
                        document.getElementById("modal-container"),
                    )
                }
                enabled={true}
                hasText={true}
                l10nKey={"dummyKey"}
            >
                Open Confirm Dialog
            </BloomButton>
        </div>
    ),
};

export const AutoUpdateSoftwareDialogStory: Story = {
    name: "AutoUpdateSoftwareDialog",
    render: () => (
        <AutoUpdateSoftwareDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    ),
};

export const ForumInvitationDialogStory: Story = {
    name: "ForumInvitationDialog",
    render: () => (
        <StorybookDialogWrapper id="ForumInvitationDialog" params={{}}>
            <ForumInvitationDialogLauncher />
        </StorybookDialogWrapper>
    ),
};

const numberChooserDialogProps: INumberChooserDialogProps = {
    min: 2,
    max: 777,
    title: "My Random Chooser Title",
    prompt: "Enter some number from 2 to 777",
    onClick: (num) => {
        console.log(`We chose ${num}.`);
    },
    dialogEnvironment: normalDialogEnvironmentForStorybook,
};

export const NumberChooserDialogStory: Story = {
    name: "NumberChooserDialog",
    render: () => (
        <NumberChooserDialog
            {...numberChooserDialogProps}
        ></NumberChooserDialog>
    ),
};

export const AboutDialogStory: Story = {
    name: "AboutDialog",
    render: () => (
        <StorybookDialogWrapper id="AboutDialog" params={{}}>
            <AboutDialogLauncher />
        </StorybookDialogWrapper>
    ),
};

export const MakeReaderTemplateBloomPackDialogStory: Story = {
    name: "MakeReaderTemplateBloomPackDialog",
    render: () => (
        <MakeReaderTemplateBloomPackDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        ></MakeReaderTemplateBloomPackDialog>
    ),
};
