import * as React from "react";

import { ComponentStory } from "@storybook/react";
import { TopicChooserDialog } from "./TopicChooserDialog";
import { normalDialogEnvironmentForStorybook } from "../../react_components/BloomDialog/BloomDialogPlumbing";

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
