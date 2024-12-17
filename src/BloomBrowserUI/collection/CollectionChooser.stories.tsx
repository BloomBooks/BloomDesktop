import { Meta, StoryObj } from "@storybook/react";
import { CollectionChooser } from "./CollectionChooser";
import { withThemeWrapper, sampleCollections } from "./shared.stories";

const meta: Meta<typeof CollectionChooser> = {
    title: "Collection/Open or Create Collection/Collection Chooser",
    component: CollectionChooser,
    decorators: [withThemeWrapper]
};

export default meta;
type Story = StoryObj<typeof meta>;

export const EnoughToScroll: Story = {
    args: {
        collections: sampleCollections
    }
};

export const NotEnoughToScroll: Story = {
    args: {
        collections: sampleCollections.slice(0, 3)
    }
};

export const NoCollections: Story = {
    args: {
        collections: []
    }
};
