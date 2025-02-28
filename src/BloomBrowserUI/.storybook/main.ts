import type { StorybookConfig } from "@storybook/react-webpack5";

const config: StorybookConfig = {
    stories: ["../**/stories.tsx","../**/*.stories.tsx"],

    addons: [
        "@storybook/addon-controls",
        "@storybook/addon-a11y",
        "@storybook/addon-webpack5-compiler-swc",
        "@chromatic-com/storybook"
    ],

    framework: {
        name: "@storybook/react-webpack5",
        options: {
            fastRefresh: true,
            builder: {
                useSWC: true
            }
        }
    },

    typescript: {
        reactDocgen: false
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
