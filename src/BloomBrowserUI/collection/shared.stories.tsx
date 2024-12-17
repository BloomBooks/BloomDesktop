import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { lightTheme } from "../bloomMaterialUITheme";

export const withThemeWrapper = (Story: React.ComponentType) => (
    <StyledEngineProvider injectFirst>
        <ThemeProvider theme={lightTheme}>
            <StorybookContext.Provider value={true}>
                <div id="reactRoot">
                    <Story />
                </div>
            </StorybookContext.Provider>
        </ThemeProvider>
    </StyledEngineProvider>
);

export const sampleCollections = [
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
