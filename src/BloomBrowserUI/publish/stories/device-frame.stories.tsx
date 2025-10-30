import { Meta, StoryObj } from "@storybook/react-vite";
import { DeviceAndControls } from "../commonPublish/DeviceAndControls";

const meta: Meta<typeof DeviceAndControls> = {
    title: "Publish/DeviceFrame",
    component: DeviceAndControls,
};

export default meta;
type Story = StoryObj<typeof DeviceAndControls>;

export const DeviceFrameDefaultPortraitRotateAble: Story = {
    name: "DeviceFrame Default Portrait, rotate-able",
    render: () => (
        <DeviceAndControls defaultLandscape={false} canRotate={true} url="">
            Portrait
        </DeviceAndControls>
    ),
};

export const DeviceFrameLandscapeOnlyWithRefreshButton: Story = {
    name: "DeviceFrame Landscape only with Refresh button",
    render: () => (
        <DeviceAndControls
            defaultLandscape={true}
            canRotate={false}
            url=""
            showPreviewButton={true}
        >
            Landscape
        </DeviceAndControls>
    ),
};

export const DeviceFrameLandscapeOnlyWithHighlightedRefreshButton: Story = {
    name: "DeviceFrame Landscape only with highlighted Refresh button",
    render: () => (
        <DeviceAndControls
            defaultLandscape={true}
            canRotate={false}
            url=""
            showPreviewButton={true}
            highlightPreviewButton={true}
        >
            Landscape
        </DeviceAndControls>
    ),
};

export const DeviceFrameLandscapeRotateAble: Story = {
    name: "DeviceFrame Landscape, rotate-able",
    render: () => (
        <DeviceAndControls defaultLandscape={true} canRotate={true} url="">
            Landscape
        </DeviceAndControls>
    ),
};
