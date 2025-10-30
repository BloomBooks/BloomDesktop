import { Meta, StoryObj } from "@storybook/react-vite";
import {
    ProgressDialogInner,
    ProgressState,
} from "../commonPublish/PublishProgressDialogInner";
import { loremIpsum } from "lorem-ipsum";

const testText =
    loremIpsum({
        count: 3,
        format: "html",
        units: "paragraphs",
    }) + "<a target='_blank' href='https://google.com'>google.com</a>";

const meta: Meta<typeof ProgressDialogInner> = {
    title: "Publish/ProgressDialog",
    component: ProgressDialogInner,
};

export default meta;
type Story = StoryObj<typeof ProgressDialogInner>;

export const Working: Story = {
    render: () => (
        <div>
            <ProgressDialogInner
                progressState={ProgressState.Working}
                messages={testText}
                heading={"Working hard..."}
                instruction={"Just sit there and watch it spin."}
                onUserClosed={() => {}}
                onUserStopped={() => {}}
            />
        </div>
    ),
};

export const Done: Story = {
    render: () => (
        <div>
            <ProgressDialogInner
                progressState={ProgressState.Done}
                messages={testText}
                onUserClosed={() => {}}
                onUserStopped={() => {}}
            />
        </div>
    ),
};

export const Error: Story = {
    render: () => (
        <div>
            <ProgressDialogInner
                progressState={ProgressState.Done}
                errorEncountered={true}
                messages={testText}
                heading={"Sky is falling"}
                onUserClosed={() => {}}
                onUserStopped={() => {}}
            />
        </div>
    ),
};
