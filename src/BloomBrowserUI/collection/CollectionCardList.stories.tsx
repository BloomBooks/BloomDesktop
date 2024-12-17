import { Meta, StoryObj } from "@storybook/react";
import { CollectionCardList } from "./CollectionCardList";
import { withThemeWrapper, sampleCollections } from "./shared.stories";

const meta: Meta<typeof CollectionCardList> = {
    title: "Collection/Open or Create Collection/Collection Card List",
    component: CollectionCardList,
    decorators: [withThemeWrapper]
};

export default meta;
type Story = StoryObj<typeof meta>;

export const Scrolls: Story = {
    args: {
        collections: sampleCollections
    }
};
