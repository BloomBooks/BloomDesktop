module.exports = {
    //stories: ["../**/stories.tsx", "../**/*.stories.tsx"],
    stories: ["../problemDialog/*.stories.tsx"],

    addons: [
        "@storybook/addon-docs",
        "@storybook/addon-controls",
        "@storybook/addon-a11y",
        "@storybook/addon-webpack5-compiler-swc",
        "@chromatic-com/storybook"
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
    },
    staticDirs: [
        "../../../output/browser",
        ".",
        "../react_components",
        "../teamCollection"
    ]
};
