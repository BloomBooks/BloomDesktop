module.exports = {
    stories: ["../**/stories.tsx", "../**/*.stories.tsx"],
    addons: [
        "@storybook/addon-docs",
        "@storybook/addon-controls",
        "@storybook/addon-a11y"
    ],
    features: {
        emotionAlias: false
    },
    framework: {
        name: "@storybook/react-webpack5",
        options: {}
    },
    docs: {
        autodocs: true
    }
};
