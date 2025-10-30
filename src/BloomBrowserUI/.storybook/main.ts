import { StorybookConfig } from "@storybook/react-vite";
import { mergeConfig } from "vite";

const config: StorybookConfig = {
    stories: ["../**/*.stories.tsx"],
    addons: ["@storybook/addon-a11y", "@chromatic-com/storybook"],

    framework: {
        name: "@storybook/react-vite",
        options: {},
    },

    typescript: {
        reactDocgen: false,
    },

    staticDirs: ["../../../output/browser"],

    async viteFinal(config) {
        return mergeConfig(config, {
            assetsInclude: [], // Don't treat .tsx as assets
        });
    },
};

export default config;
