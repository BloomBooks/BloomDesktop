import { Meta, StoryObj } from "@storybook/react-vite";
import { CollectionCardList } from "./CollectionCardList";
import { withThemeWrapper, sampleCollections } from "./shared.stories";

const meta: Meta<typeof CollectionCardList> = {
    title: "Collection/Open or Create Collection/Collection Card List",
    component: CollectionCardList,
    decorators: [withThemeWrapper],
};

export default meta;
type Story = StoryObj<typeof meta>;

export const Scrolls: Story = {
    args: {
        collections: sampleCollections,
    },
};

// Dogfood batch 1, item 6: join cards for cloud collections the user belongs to but hasn't
// joined locally yet, appended after the regular collections (and NOT counted against their
// maxCardCount slice). Shows the reduced card content (title + team-collection icon + "Get" join
// cue only, no book/checked-out/unpublished counts) since none of that is available pre-join.
export const WithJoinCards: Story = {
    args: {
        collections: sampleCollections.slice(0, 3),
        joinCollections: [
            { collectionId: "join-1", title: "Sunshine Readers" },
            { collectionId: "join-2", title: "Rainforest Books" },
        ],
    },
};
