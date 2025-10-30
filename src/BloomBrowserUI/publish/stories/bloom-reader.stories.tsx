import { Meta, StoryObj } from "@storybook/react-vite";
import { ReaderPublishScreen } from "../ReaderPublish/ReaderPublishScreen";
import "../storiesApiMocks";

const meta: Meta<typeof ReaderPublishScreen> = {
    title: "Publish/Bloom Reader",
    component: ReaderPublishScreen,
};

export default meta;
type Story = StoryObj<typeof ReaderPublishScreen>;

export const ReaderPublishScreenStory: Story = {
    name: "ReaderPublishScreen",
    render: () => <ReaderPublishScreen />,
};
