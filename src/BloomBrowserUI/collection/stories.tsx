/** @jsx jsx **/
/** @jsxFrag React.Fragment **/
import { jsx } from "@emotion/react";
import { useState } from "react";

import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { storiesOf, addDecorator } from "@storybook/react";
import { StorybookContext } from "../.storybook/StoryBookContext";

import { lightTheme } from "../bloomMaterialUITheme";
import { CollectionCardList } from "./CollectionCardList";
import { CollectionChooser } from "./CollectionChooser";
import { CollectionChooserDialog } from "./CollectionChooserDialog";

addDecorator(storyFn => (
    <StyledEngineProvider injectFirst>
        <ThemeProvider theme={lightTheme}>
            <StorybookContext.Provider value={true}>
                <div id="reactRoot">{storyFn()}</div>
            </StorybookContext.Provider>
        </ThemeProvider>
    </StyledEngineProvider>
));

const sampleCollections = [
    {
        title: "Collection 1",
        isTeamCollection: true,
        bookCount: 10,
        checkedOutCount: 3,
        unpublishedCount: 1,
        path: "C:/Collections/Collection1"
    },
    {
        title:
            "Collection 2 Collection 2 Collection 2 Collection 2 Collection 2",
        bookCount: 0,
        path: "C:/Collections/Collection2"
    },
    {
        title:
            "Collection 3 with a long title with a long title with a a long title",
        isTeamCollection: true,
        bookCount: 30,
        checkedOutCount: 1,
        path: "C:/Collections/Collection3"
    },
    {
        title: "Collection 4",
        bookCount: 100,
        unpublishedCount: 1,
        path: "C:/Collections/Collection4"
    },
    {
        title: "Collection 5",
        bookCount: 1,
        path: "C:/Collections/CollectionG"
    },
    {
        title: "Collection 6",
        bookCount: 5,
        path: "C:/Collections/Collection6"
    },
    {
        title: "Collection 7",
        bookCount: 15,
        path: "C:/Collections/Collection7"
    },
    {
        title: "Collection 8",
        bookCount: 25,
        path: "C:/Collections/Collection8"
    },
    {
        title: "Collection 9",
        bookCount: 35,
        path: "C:/Collections/Collection9"
    },
    {
        title: "Collection 10",
        bookCount: 45,
        path: "C:/Collections/Collection10"
    },
    {
        title: "Collection 11",
        bookCount: 55,
        path: "C:/Collections/Collection11"
    }
];

storiesOf(
    "Collection components/Open or Create Collection/Collection Card List",
    module
).add("Scrolls", () => <CollectionCardList collections={sampleCollections} />);
storiesOf(
    "Collection components/Open or Create Collection/Collection Chooser",
    module
)
    .add("Enough to scroll", () => (
        <CollectionChooser collections={sampleCollections} />
    ))
    .add("Not enough to scroll", () => (
        <CollectionChooser collections={sampleCollections.slice(0, 3)} />
    ))
    .add("No collections", () => <CollectionChooser collections={[]} />);

storiesOf(
    "Collection components/Open or Create Collection/Collection Chooser Dialog",
    module
)
    .add("Dialog with collections", () => {
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
    })
    .add("Dialog without collections", () => {
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
    });
