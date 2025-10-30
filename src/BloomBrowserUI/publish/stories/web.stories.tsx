import { Meta, StoryObj } from "@storybook/react-vite";
import { LibraryPublishScreen } from "../LibraryPublish/LibraryPublishScreen";
import {
    IUploadCollisionDlgData,
    UploadCollisionDlg,
} from "../LibraryPublish/uploadCollisionDlg";
import { normalDialogEnvironmentForStorybook } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import "../storiesApiMocks";

const propsObject: IUploadCollisionDlgData = {
    userEmail: "testEmail@sil.org",
    newTitle: "Title of New Upload",
    newLanguages: ["Sokoro", "English"],
    existingTitle: "Title on BL Server",
    existingBookUrl: "https://dev.bloomlibrary.org/book/ALkGcILEG3",
    existingLanguages: ["English", "French"],
    existingCreatedDate: "10/21/2021",
    existingUpdatedDate: "10/29/2021",
    dialogEnvironment: normalDialogEnvironmentForStorybook,
    count: 1,
};

const lotsOfLanguages = ["Sokoro", "English", "Swahili", "Hausa"];

const meta: Meta = {
    title: "Publish/Web",
};

export default meta;
type Story = StoryObj;

export const LibraryPublishScreenStory: Story = {
    name: "LibraryPublishScreen",
    render: () => <LibraryPublishScreen />,
};

export const UploadCollisionDialogStory: Story = {
    name: "Upload Collision Dialog",
    render: () => (
        <UploadCollisionDlg
            {...propsObject}
            conflictIndex={0}
            setConflictIndex={() => {}}
        />
    ),
};

export const UploadCollisionDialogLotsOfLanguagesStory: Story = {
    name: "Upload Collision Dialog -- lots of languages",
    render: () => (
        <UploadCollisionDlg
            {...propsObject}
            newLanguages={lotsOfLanguages}
            conflictIndex={0}
            setConflictIndex={() => {}}
        />
    ),
};
