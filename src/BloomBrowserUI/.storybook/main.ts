import type { StorybookConfig } from "@storybook/react-webpack5";

const config: StorybookConfig = {
    //stories: ["../**/stories.tsx", "../**/*.stories.tsx"],
    stories: ["../problemDialog/*.stories.tsx"],

    addons: [
        //"@storybook/addon-docs",
        "@storybook/addon-controls",
        "@storybook/addon-a11y",
        "@storybook/addon-webpack5-compiler-babel",
        "@chromatic-com/storybook"
    ],

    // features: {
    //     emotionAlias: false
    // },

    framework: {
        name: "@storybook/react-webpack5",
        options: {
            fastRefresh: true,
            docgen: false
        }
    },

    docs: {
        autodocs: false
    },
    staticDirs: [
        "../../../output/browser",
        ".",
        "../react_components",
        "../teamCollection"
    ]
};

export default config;
