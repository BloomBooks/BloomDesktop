import { BookInfoCard } from "../bookInfoCard";

import { Meta, StoryObj } from "@storybook/react-vite";

const meta: Meta = {
    title: "BookInformationCards",
};

export default meta;
type Story = StoryObj;

const languages1: string[] = ["Kanuri", "Swahili"];
const languages2: string[] = ["French", "Swahili"];
const languages3: string[] = [
    "French",
    "Swahili",
    "Tanzanian Sign Language",
    "English",
];
const uploadDate = "7/28/2020";
const updateDate = "10/26/2020";

export const PreviouslyUploadedStory: Story = {
    name: "PreviouslyUploaded",
    render: () => (
        <BookInfoCard
            title="02. Bigǝ Dinaro Gaana"
            languages={languages1}
            originalUpload={uploadDate}
            lastUpdated={updateDate}
        />
    ),
};

export const NewUploadStory: Story = {
    name: "NewUpload",
    render: () => <BookInfoCard title="02. Foo Bar" languages={languages2} />,
};

export const SeveralLanguagesStory: Story = {
    name: "Several languages",
    render: () => (
        <BookInfoCard title="Foo Bar Extended" languages={languages3} />
    ),
};
