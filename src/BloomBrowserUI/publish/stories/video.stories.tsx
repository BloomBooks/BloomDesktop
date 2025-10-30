import { Meta, StoryObj } from "@storybook/react-vite";
import { PublishAudioVideo } from "../video/PublishAudioVideo";
import "../storiesApiMocks";

const meta: Meta<typeof PublishAudioVideo> = {
    title: "Publish/Video",
    component: PublishAudioVideo,
};

export default meta;
type Story = StoryObj<typeof PublishAudioVideo>;

export const PublishAudioVideoStory: Story = {
    name: "PublishAudioVideo",
    render: () => <PublishAudioVideo />,
};
