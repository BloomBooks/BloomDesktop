module.exports = {
    stories: ["../**/stories.tsx", "../**/*.stories.tsx"],
    addons: [
        "@storybook/addon-docs",
        "@storybook/addon-controls",
        "@storybook/addon-a11y"
    ],
    core: {
        builder: "webpack5"
    },
    features: {
        emotionAlias: false
    }
};
