import type { StorybookConfig } from "@storybook/react-webpack5";

const config: StorybookConfig = {
    stories: ["../react_components/color-picking/stories.tsx"],

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
    ],

    webpackFinal: async (config) => {
        // Ensure we're using the project's version of React
        if (config.resolve) {
            config.resolve.alias = {
                ...config.resolve.alias,
                'react': require.resolve('react'),
                'react-dom': require.resolve('react-dom')
            };
        }
        return config;
    }
};

export default config;
