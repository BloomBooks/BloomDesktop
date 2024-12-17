import { useState } from "react";
import { Meta, StoryObj } from "@storybook/react";
import { CollectionChooserDialog } from "./CollectionChooserDialog";
import { withThemeWrapper, sampleCollections } from "./shared.stories";

const meta: Meta<typeof CollectionChooserDialog> = {
    title: "Collection/Open or Create Collection/Collection Chooser Dialog",
    component: CollectionChooserDialog,
    decorators: [withThemeWrapper]
};

export default meta;
type Story = StoryObj<typeof meta>;

export const DialogWithCollections: Story = {
    render: () => {
        const [open, setOpen] = useState(true);
        return (
            <>
                <button onClick={() => setOpen(true)}>Open Dialog</button>
                <CollectionChooserDialog
                    open={open}
                    onClose={() => setOpen(false)}
                    collections={sampleCollections}
                />
            </>
        );
    }
};

export const DialogWithoutCollections: Story = {
    render: () => {
        const [open, setOpen] = useState(true);
        return (
            <>
                <button onClick={() => setOpen(true)}>Open Dialog</button>
                <CollectionChooserDialog
                    open={open}
                    onClose={() => setOpen(false)}
                    collections={[]}
                />
            </>
        );
    }
};
