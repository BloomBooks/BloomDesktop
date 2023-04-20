import * as React from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { lightTheme } from "../../bloomMaterialUITheme";
import { TopicChooserDialog } from "./TopicChooserDialog";
import { normalDialogEnvironmentForStorybook } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import { ComponentStory } from "@storybook/react";

/* addDecorator is gone
addDecorator(storyFn => (
    <StyledEngineProvider injectFirst>
        <ThemeProvider theme={lightTheme}>
            <StorybookContext.Provider value={true}>
                {storyFn()}
            </StorybookContext.Provider>
        </ThemeProvider>
    </StyledEngineProvider>
));
*/

export default {
    title: "Choose Topic"
};

const TopicChooserDialogTemplate: ComponentStory<typeof TopicChooserDialog> = args => (
    <TopicChooserDialog
        {...args}
        dialogEnvironment={normalDialogEnvironmentForStorybook}
    />
);
export const _TopicChooserDialog = TopicChooserDialogTemplate.bind({});

_TopicChooserDialog.args = {
    currentTopic: "Healthy Eating",
    availableTopics: [
        { englishKey: "No Topic", translated: "Pas de sujet" },
        { englishKey: "Healthy Eating", translated: "Mangez! Mangez!" },
        { englishKey: "Animal Story", translated: "Histoire d'animaux" },
        {
            englishKey: "Community Living Long",
            translated: "La vie communautaire et plus"
        },
        { englishKey: "Blah", translated: "Inconsequential" },
        { englishKey: "Eating", translated: "Mangez." },
        {
            englishKey: "Animal Identification",
            translated: "Identification d'animaux"
        },
        { englishKey: "Community Living", translated: "La vie communautaire" },
        { englishKey: "Blah Blah", translated: "Inconsequential x 2" },
        { englishKey: "Normal Eating", translated: "Junk Mangez." },
        { englishKey: "Many Animals", translated: "Beaucoup d'animaux" }
    ]
};
