module.exports = {
    env: {
        browser: true,
        es2021: true
    },
    extends: [
        "eslint:recommended",
        "plugin:react/recommended",
        "plugin:@typescript-eslint/recommended",
        "plugin:prettier/recommended"
    ],
    parser: "@typescript-eslint/parser",
    parserOptions: {
        ecmaFeatures: {
            jsx: true
        },
        ecmaVersion: "latest",
        sourceType: "module"
    },
    plugins: ["react", "@typescript-eslint"],
    settings: {
        react: {
            version: "detect" // React version. "detect" automatically picks the version you have installed.
        }
    },
    rules: {
        "prettier/prettier": "off",
        "no-var": "warn",
        "prefer-const": "warn",
        "no-useless-escape": "off",
        "no-warning-comments": [
            1,
            { terms: ["nocommit"], location: "anywhere" }
        ],
        // turned these on when first using eslint. TODO: Review these
        "@typescript-eslint/no-explicit-any": "off",
        "@typescript-eslint/no-empty-function": "off",
        "@typescript-eslint/no-inferrable-types": "off",
        "@typescript-eslint/no-unused-vars": "off",
        "react/no-unescaped-entities": "off", // review
        "react/prop-types": "off", // review
        "react/no-children-prop": "off",
        "@typescript-eslint/ban-types": "off",
        "@typescript-eslint/no-empty-interface": "off",
        "no-case-declarations": "warn",
        "@typescript-eslint/no-var-requires": "off",
        "@typescript-eslint/triple-slash-reference": "off",
        "prefer-rest-params": "warn",
        "prefer-spread": "warn",
        "react/jsx-key": "warn"
    }
};
