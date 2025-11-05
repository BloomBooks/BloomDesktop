import { Meta, StoryObj } from "@storybook/react-vite";
import { EPUBPublishScreen } from "../ePUBPublish/ePUBPublishScreen";
import BookMetadataDialog from "../metadata/BookMetadataDialog";
import "../storiesApiMocks";

const meta: Meta = {
    title: "Publish/ePUB",
};

export default meta;
type Story = StoryObj;

export const EpubPublishScreenStory: Story = {
    name: "EPUBPublishScreen",
    render: () => <EPUBPublishScreen />,
};

export const BookMetadataDialogStory: Story = {
    name: "BookMetadataDialog",
    render: () => (
        <BookMetadataDialog
            startOpen={true}
            onClose={() => alert("BookMetadataDialog closed with OK")}
        />
    ),
};
