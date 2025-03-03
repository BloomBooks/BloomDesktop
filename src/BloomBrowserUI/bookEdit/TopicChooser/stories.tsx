import * as React from "react";

import type { Meta, StoryObj } from "@storybook/react";
import { TopicChooserDialog } from "./TopicChooserDialog";
import { normalDialogEnvironmentForStorybook } from "../../react_components/BloomDialog/BloomDialogPlumbing";

// Define the Meta object for the component
const meta: Meta<typeof TopicChooserDialog> = {
    title: "Choose Topic",
    component: TopicChooserDialog
};

export default meta;

// Define the Story type
type Story = StoryObj<typeof TopicChooserDialog>;

// Create the story
export const TopicChooserDialogStory: Story = {
    args: {
        currentTopic: "Healthy Eating",
        dialogEnvironment: normalDialogEnvironmentForStorybook,
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
    }
};
